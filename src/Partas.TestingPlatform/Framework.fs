// The capability and filter types this file touches are [Experimental("TPEXP")].
#nowarn "57"

namespace Partas.TestingPlatform

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.Builder
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.CommandLine
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Extensions.TestFramework
open Microsoft.Testing.Platform.Helpers
open Microsoft.Testing.Platform.Requests
open Microsoft.Testing.Platform.Services

/// <summary>
/// Platform switch to ask a run to stop early; <c>--maximum-failed-tests</c> sets it
/// once the failure count reaches its argument.
/// </summary>
/// <remarks>
/// Frameworks read it between tests, so a test already running continues to its own end.
/// </remarks>
type GracefulStop() =
    let mutable requested = 0

    member _.IsRequested = Interlocked.And(&requested, Int32.MaxValue) <> 0

    member _.Request() = Interlocked.Increment(&requested) |> ignore

/// <summary>The surviving tree, a reporter for its leaves, and the session's cancellation.</summary>
type RunContext<'T> =
    { Tree: ResolvedTestTree<'T>
      Leaves: ExecutableLeaf<'T> list
      Reporter: Reporter
      CancellationToken: CancellationToken
      /// <summary>Whether the platform narrowed this run, rather than asking for everything.</summary>
      FilterApplied: bool
      /// <summary>MTP's parsed command-line options, for reading a declared option's value.</summary>
      CommandLineOptions: ICommandLineOptions
      GracefulStop: GracefulStop }

/// <summary>
/// An action run on the builder before it builds, for another package to register onto
/// <c>ITestApplicationBuilder</c> itself (e.g. TRX's report writer). An MTP builder
/// registration can defer a closure's execution into the running session, reaching
/// <c>IServiceProvider</c> and <c>IMessageBus</c> from there. Construction is limited to the
/// assemblies this binding names in <c>InternalsVisibleTo</c> — an unsigned request, raising
/// the bar against an accidental caller rather than closing the door on a deliberate one; an
/// assembly compiled with a matching name gets the same access.
/// </summary>
type BuilderExtension = internal BuilderExtension of (ITestApplicationBuilder -> unit)

module internal BuilderExtension =
    let create (action: ITestApplicationBuilder -> unit) = BuilderExtension action
    let invoke (builder: ITestApplicationBuilder) (BuilderExtension action) = action builder

/// <summary>
/// A test framework's identity, tree, execution, and extension points. The
/// representation is internal: a caller outside the assemblies this binding grants
/// <c>InternalsVisibleTo</c> builds and reads one exclusively through <c>testFramework</c>,
/// <c>TestApplication.run</c>, and the read accessors in the <c>FrameworkDefinition</c> module.
/// </summary>
type FrameworkDefinition<'T> =
    internal
        { Uid: string
          Version: string
          DisplayName: string
          Description: string
          Banner: string option
          BuildTree: unit -> TestTree<'T>
          RunTests: RunContext<'T> -> Task
          CreateSession: SessionContext -> Task<SessionOutcome>
          CloseSession: SessionContext -> Task<SessionOutcome>
          /// <summary>
          /// Factories for MTP command-line option providers, declared to the platform alongside
          /// the framework so custom options survive MTP's unrecognised-option rejection.
          /// </summary>
          CommandLineOptionsProviders: (unit -> ICommandLineOptionsProvider) list
          /// <summary>
          /// Factories for capabilities registered alongside the banner capability, letting a
          /// companion package (e.g. TRX reporting) contribute one without core binding taking
          /// its dependency.
          /// </summary>
          Capabilities: (unit -> ITestFrameworkCapability) list
          /// <summary>
          /// Properties published with every outcome the binding reports, letting a companion
          /// package (e.g. TRX reporting) put what its writer requires on every result,
          /// independent of the framework's execution walk.
          /// </summary>
          LeafProperties: ExecutableLeaf<unit> -> IProperty list
          BuilderExtensions: BuilderExtension list }

type private BannerCapability(message: string) =
    interface IBannerMessageOwnerCapability with
        member _.GetBannerMessageAsync() = Task.FromResult message

/// <summary>
/// Sets the given switch when the platform asks a run to stop, and reports the request honoured.
/// Reporting <c>true</c> holds the platform to its graceful path, so tests already running reach
/// their own end and publish their own results.
/// </summary>
type private GracefulStopCapability(stop: GracefulStop) =
    interface IGracefulStopTestExecutionCapability with
        member _.StopTestExecutionAsync _ =
            stop.Request()
            Task.CompletedTask

    interface IGracefulStopTestExecutionResultCapability with
        member _.TryStopTestExecutionAsync _ =
            stop.Request()
            Task.FromResult true

/// <summary>
/// The capabilities a definition declares to the platform: the graceful stop paired with the
/// <c>--maximum-failed-tests</c> registration, the banner when the definition declares one, and
/// every capability factory the definition carries.
/// </summary>
type FrameworkCapabilities<'T>(definition: FrameworkDefinition<'T>, stop: GracefulStop) =
    let capabilities =
        let banner =
            definition.Banner
            |> Option.map (fun message -> BannerCapability message :> ITestFrameworkCapability)
            |> Option.toList

        (GracefulStopCapability stop :> ITestFrameworkCapability)
        :: (banner @ List.map (fun factory -> factory ()) definition.Capabilities)
        |> Array.ofList
        :> IReadOnlyCollection<_>

    new(definition: FrameworkDefinition<'T>) = FrameworkCapabilities<'T>(definition, GracefulStop())

    interface ITestFrameworkCapabilities with
        member _.Capabilities = capabilities

type Framework<'T>(definition: FrameworkDefinition<'T>) =
    let mutable resolved = Error "The test session has not been created."
    let mutable services: IServiceProvider = null
    let stop = GracefulStop()

    /// <summary>The latch the framework's graceful stop capability sets.</summary>
    member internal _.GracefulStop = stop

    /// <summary>Records the service provider MTP hands the framework at registration time.</summary>
    member internal _.SetServices(serviceProvider: IServiceProvider) = services <- serviceProvider

    interface IExtension with
        member _.Uid = definition.Uid
        member _.Version = definition.Version
        member _.DisplayName = definition.DisplayName
        member _.Description = definition.Description
        member _.IsEnabledAsync() = Task.FromResult true

    interface IDataProducer with
        member _.DataTypesProduced = [| typeof<TestNodeUpdateMessage> |]

    interface ITestFramework with
        member _.CreateTestSessionAsync context =
            task {
                resolved <- Error "The test session has not been created."
                let! outcome = definition.CreateSession (SessionContext.ofCreate context)
                let result =
                    match outcome with
                    | SessionOutcome.Failed _ -> outcome
                    | SessionOutcome.Succeeded warning ->
                        resolved <- Session.resolveTree definition.BuildTree
                        match resolved with
                        | Ok _ -> outcome
                        | Error message -> SessionOutcome.Failed(message, warning)
                return SessionOutcome.toCreateResult result
            }

        member _.CloseTestSessionAsync context =
            task {
                try
                    let! outcome = definition.CloseSession (SessionContext.ofClose context)
                    return SessionOutcome.toCloseResult outcome
                finally
                    resolved <- Error "The test session has been closed."
            }

        member this.ExecuteRequestAsync(context) =
            task {
                try
                    match resolved, context.Request with
                    | Ok tree, (:? TestExecutionRequest as request) ->
                        let producer = this :> IDataProducer
                        let session = request.Session.SessionUid
                        let admits = Filtering.toPredicate request.Filter

                        match Execution.prune admits tree with
                        | None -> ()
                        | Some surviving ->
                            match request with
                            | :? DiscoverTestExecutionRequest ->
                                do! Discovery.publish context.MessageBus producer session surviving
                            | _ ->
                                do! Execution.publishGroups context.MessageBus producer session surviving

                                let runContext: RunContext<'T> =
                                    { Tree = surviving
                                      Leaves = Execution.leaves surviving
                                      Reporter =
                                        Reporter(
                                            context.MessageBus,
                                            producer,
                                            session,
                                            definition.LeafProperties
                                        )
                                      CancellationToken = context.CancellationToken
                                      FilterApplied = not (request.Filter :? NopFilter)
                                      CommandLineOptions = services.GetCommandLineOptions()
                                      GracefulStop = stop }

                                do! definition.RunTests runContext
                    | _ -> ()
                finally
                    context.Complete()
            }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FrameworkDefinition =

    let empty<'T> : FrameworkDefinition<'T> =
        { FrameworkDefinition.Uid = ""
          Version = "0.0.0"
          DisplayName = ""
          Description = ""
          Banner = None
          BuildTree = fun () -> Group("", None, [], [])
          RunTests = fun _ -> Task.CompletedTask
          CreateSession = fun _ -> Task.FromResult(SessionOutcome.Succeeded None)
          CloseSession = fun _ -> Task.FromResult(SessionOutcome.Succeeded None)
          CommandLineOptionsProviders = []
          Capabilities = []
          LeafProperties = fun _ -> []
          BuilderExtensions = [] }

    /// <summary>The command-line option provider factories declared on this definition.</summary>
    let commandLineOptionsProviders (definition: FrameworkDefinition<'T>) =
        definition.CommandLineOptionsProviders

    /// <summary>Replaces session setup. MTP awaits it before the tree is built; failure skips tree construction.</summary>
    let withCreateSession (callback: SessionContext -> Task<SessionOutcome>) (definition: FrameworkDefinition<'T>) =
        { definition with CreateSession = callback }

    /// <summary>Replaces cleanup invoked when MTP closes the session. Callback faults and cancellation propagate to MTP.</summary>
    let withCloseSession (callback: SessionContext -> Task<SessionOutcome>) (definition: FrameworkDefinition<'T>) =
        { definition with CloseSession = callback }

    /// <summary>The capability factories declared on this definition.</summary>
    let capabilities (definition: FrameworkDefinition<'T>) = definition.Capabilities

    /// <summary>
    /// The definition with the given capability factory declared after the ones already on it.
    /// The composable counterpart of the <c>capabilities</c> CE operation, for a definition a
    /// caller received already built.
    /// </summary>
    let addCapability (factory: unit -> ITestFrameworkCapability) (definition: FrameworkDefinition<'T>) =
        { definition with Capabilities = definition.Capabilities @ [ factory ] }

    /// <summary>
    /// The definition with the given command-line option provider factory declared after the ones
    /// already on it. The composable counterpart of the <c>commandLineOptions</c> CE operation,
    /// for a definition a caller received already built.
    /// </summary>
    let addCommandLineOptionsProvider
        (factory: unit -> ICommandLineOptionsProvider)
        (definition: FrameworkDefinition<'T>)
        =
        { definition with CommandLineOptionsProviders = definition.CommandLineOptionsProviders @ [ factory ] }

    /// <summary>The builder extensions declared on this definition.</summary>
    let builderExtensions (definition: FrameworkDefinition<'T>) = definition.BuilderExtensions

    /// <summary>The properties this definition publishes with every reported outcome.</summary>
    let leafProperties (definition: FrameworkDefinition<'T>) = definition.LeafProperties

    /// <summary>
    /// The definition publishing the given properties with every reported outcome, after any it
    /// already contributes.
    /// </summary>
    let addLeafProperties
        (contribute: ExecutableLeaf<unit> -> IProperty list)
        (definition: FrameworkDefinition<'T>)
        =
        let declared = definition.LeafProperties

        { definition with LeafProperties = fun leaf -> declared leaf @ contribute leaf }

type TestFrameworkBuilder<'T>() =
    member _.Yield(_: unit) = FrameworkDefinition.empty<'T>

    [<CustomOperation "uid">]
    member _.Uid(definition: FrameworkDefinition<'T>, value) = { definition with Uid = value }

    [<CustomOperation "version">]
    member _.Version(definition: FrameworkDefinition<'T>, value) = { definition with Version = value }

    [<CustomOperation "displayName">]
    member _.DisplayName(definition: FrameworkDefinition<'T>, value) = { definition with DisplayName = value }

    [<CustomOperation "description">]
    member _.Description(definition: FrameworkDefinition<'T>, value) = { definition with Description = value }

    [<CustomOperation "banner">]
    member _.Banner(definition: FrameworkDefinition<'T>, value) = { definition with Banner = Some value }

    [<CustomOperation "tests">]
    member _.Tests(definition: FrameworkDefinition<'T>, build) = { definition with BuildTree = build }

    [<CustomOperation "onRun">]
    member _.OnRun(definition: FrameworkDefinition<'T>, run) = { definition with RunTests = run }

    [<CustomOperation "onCreateSession">]
    member _.OnCreateSession(definition: FrameworkDefinition<'T>, callback) =
        FrameworkDefinition.withCreateSession callback definition

    [<CustomOperation "onCloseSession">]
    member _.OnCloseSession(definition: FrameworkDefinition<'T>, callback) =
        FrameworkDefinition.withCloseSession callback definition

    /// <summary>Declares command-line option providers, replacing any previously declared.</summary>
    [<CustomOperation "commandLineOptions">]
    member _.CommandLineOptions(definition: FrameworkDefinition<'T>, providers) =
        { definition with CommandLineOptionsProviders = providers }

    /// <summary>Declares capability factories, replacing any previously declared.</summary>
    [<CustomOperation "capabilities">]
    member _.Capabilities(definition: FrameworkDefinition<'T>, factories) =
        { definition with Capabilities = factories }

[<AutoOpen>]
module Builders =
    let testFramework<'T> = TestFrameworkBuilder<'T>()

/// <summary>
/// Sets <c>FrameworkDefinition.BuilderExtensions</c>, replacing any previously declared.
/// <c>internal</c> for the same reason the field is: only a companion package this binding
/// grants <c>InternalsVisibleTo</c> may reach the builder.
/// </summary>
module internal BuilderExtensions =
    let set (actions: BuilderExtension list) (definition: FrameworkDefinition<'T>) =
        { definition with BuilderExtensions = actions }

    /// <summary>Runs every declared action on the builder, in declaration order.</summary>
    let run (builder: ITestApplicationBuilder) (definition: FrameworkDefinition<'T>) =
        for extension in definition.BuilderExtensions do
            BuilderExtension.invoke builder extension

module TestApplication =
    let configure (builder: ITestApplicationBuilder) (definition: FrameworkDefinition<'T>) =
        if String.IsNullOrWhiteSpace definition.Uid then
            invalidArg (nameof definition) "A framework definition requires a uid."
        let definition =
            { definition with
                DisplayName = (if definition.DisplayName = "" then definition.Uid else definition.DisplayName)
                Description = (if definition.Description = "" then definition.Uid else definition.Description) }
        let framework = Framework definition
        builder.RegisterTestFramework(
            (fun _ -> FrameworkCapabilities(definition, framework.GracefulStop) :> ITestFrameworkCapabilities),
            (fun _ serviceProvider ->
                framework.SetServices serviceProvider
                framework :> ITestFramework)
            )
        |> ignore
        builder.AddTreeNodeFilterService framework
        builder.AddMaximumFailedTestsService framework
        for factory in definition.CommandLineOptionsProviders do
            builder.CommandLine.AddProvider(fun () -> factory())
        definition |> BuilderExtensions.run builder



    /// <summary>
    /// Runs the definition as a test application, returning the process exit code.
    /// </summary>
    let run (argv: string[]) (definition: FrameworkDefinition<'T>) =
        task {
            let! builder = TestApplication.CreateBuilderAsync argv
            configure builder definition
            use! app = builder.BuildAsync()
            return! app.RunAsync()
        }
        |> _.GetAwaiter().GetResult()
