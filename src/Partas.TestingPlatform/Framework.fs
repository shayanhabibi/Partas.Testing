// The capability and filter types this file touches are [Experimental("TPEXP")].
#nowarn "57"

namespace Partas.TestingPlatform

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.Builder
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Extensions.TestFramework
open Microsoft.Testing.Platform.Helpers
open Microsoft.Testing.Platform.Requests

/// <summary>The surviving tree, a reporter for its leaves, and the session's cancellation.</summary>
type RunContext<'T> =
    { Tree: ResolvedTestTree<'T>
      Leaves: ExecutableLeaf<'T> list
      Reporter: Reporter
      CancellationToken: CancellationToken
      /// <summary>Whether the platform narrowed this run, rather than asking for everything.</summary>
      FilterApplied: bool }

type FrameworkDefinition<'T> =
    { Uid: string
      Version: string
      DisplayName: string
      Description: string
      Banner: string option
      BuildTree: unit -> TestTree<'T>
      RunTests: RunContext<'T> -> Task }

type private BannerCapability(message: string) =
    interface IBannerMessageOwnerCapability with
        member _.GetBannerMessageAsync() = Task.FromResult message

type FrameworkCapabilities<'T>(definition: FrameworkDefinition<'T>) =
    interface ITestFrameworkCapabilities with
        member _.Capabilities =
            match definition.Banner with
            | Some message -> [| BannerCapability message :> ITestFrameworkCapability |]
            | None -> Array.empty
            :> IReadOnlyCollection<_>

type Framework<'T>(definition: FrameworkDefinition<'T>) =
    let mutable resolved = Error "The test session has not been created."

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
                                      FilterApplied = not (request.Filter :? NopFilter) }

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
          RunTests = fun _ -> Task.CompletedTask }

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

[<AutoOpen>]
module Builders =
    let testFramework<'T> = TestFrameworkBuilder<'T>()

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
                (fun _ _ -> framework :> ITestFramework)
            )
            |> ignore

            builder.AddTreeNodeFilterService framework

            use! app = builder.BuildAsync()
            return! app.RunAsync()
        }
        |> _.GetAwaiter().GetResult()
