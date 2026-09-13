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

/// <summary>The surviving tree, a reporter for its leaves, and the session's cancellation.</summary>
type RunContext<'T> =
    { Tree: ResolvedTestTree<'T>
      Leaves: ExecutableLeaf<'T> list
      Reporter: Reporter
      CancellationToken: CancellationToken
      /// <summary>Whether the platform narrowed this run, rather than asking for everything.</summary>
      FilterApplied: bool
      /// <summary>MTP's parsed command-line options, for reading a declared option's value.</summary>
      CommandLineOptions: ICommandLineOptions }

/// <summary>
/// An action run on the builder before it builds, for a companion package to register onto
/// <c>ITestApplicationBuilder</c> itself (e.g. TRX's report writer). An MTP builder
/// registration can defer a closure's execution into the running session, reaching
/// <c>IServiceProvider</c> and <c>IMessageBus</c> from there; construction is restricted to
/// this binding's own companion packages, granted access through <c>InternalsVisibleTo</c>.
/// </summary>
type BuilderExtension = internal BuilderExtension of (ITestApplicationBuilder -> unit)

module internal BuilderExtension =
    let create (action: ITestApplicationBuilder -> unit) = BuilderExtension action
    let invoke (builder: ITestApplicationBuilder) (BuilderExtension action) = action builder

type FrameworkDefinition<'T> =
    { Uid: string
      Version: string
      DisplayName: string
      Description: string
      Banner: string option
      BuildTree: unit -> TestTree<'T>
      RunTests: RunContext<'T> -> Task
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
      BuilderExtensions: BuilderExtension list }

type private BannerCapability(message: string) =
    interface IBannerMessageOwnerCapability with
        member _.GetBannerMessageAsync() = Task.FromResult message

type FrameworkCapabilities<'T>(definition: FrameworkDefinition<'T>) =
    let capabilities =
        let banner =
            definition.Banner
            |> Option.map (fun message -> BannerCapability message :> ITestFrameworkCapability)
            |> Option.toList

        (banner @ List.map (fun factory -> factory ()) definition.Capabilities)
        |> Array.ofList
        :> IReadOnlyCollection<_>

    interface ITestFrameworkCapabilities with
        member _.Capabilities = capabilities

type Framework<'T>(definition: FrameworkDefinition<'T>) =
    let mutable resolved = Error "The test session has not been created."
    let mutable services: IServiceProvider = null

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
        member this.CreateTestSessionAsync _ =
            resolved <- Session.resolveTree definition.BuildTree

            match resolved with
            | Ok _ -> SessionOutcome.Succeeded None
            | Error message -> SessionOutcome.Failed(message, None)
            |> SessionOutcome.toCreateResult
            |> Task.FromResult

        member _.CloseTestSessionAsync _ =
            SessionOutcome.Succeeded None |> SessionOutcome.toCloseResult |> Task.FromResult

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
                                      Reporter = Reporter(context.MessageBus, producer, session)
                                      CancellationToken = context.CancellationToken
                                      FilterApplied = not (request.Filter :? NopFilter)
                                      CommandLineOptions = services.GetCommandLineOptions() }

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
          CommandLineOptionsProviders = []
          Capabilities = []
          BuilderExtensions = [] }

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

    /// <summary>
    /// Runs the definition as a test application, returning the process exit code.
    /// </summary>
    let run (argv: string[]) (definition: FrameworkDefinition<'T>) =
        if String.IsNullOrWhiteSpace definition.Uid then
            invalidArg (nameof definition) "A framework definition requires a uid."

        let definition =
            { definition with
                DisplayName = (if definition.DisplayName = "" then definition.Uid else definition.DisplayName)
                Description = (if definition.Description = "" then definition.Uid else definition.Description) }

        task {
            let! builder = TestApplication.CreateBuilderAsync argv
            let framework = Framework definition

            builder.RegisterTestFramework(
                (fun _ -> FrameworkCapabilities definition :> ITestFrameworkCapabilities),
                (fun _ serviceProvider ->
                    framework.SetServices serviceProvider
                    framework :> ITestFramework)
            )
            |> ignore

            builder.AddTreeNodeFilterService framework

            for factory in definition.CommandLineOptionsProviders do
                builder.CommandLine.AddProvider(fun () -> factory ())

            definition |> BuilderExtensions.run builder

            use! app = builder.BuildAsync()
            return! app.RunAsync()
        }
        |> _.GetAwaiter().GetResult()
