module Partas.TestingPlatform.Program

open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.Builder
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Extensions.TestFramework
open Microsoft.Testing.Platform.Requests
open Microsoft.Testing.Platform.TestHost

#nowarn 3391
[<Struct>]
type SessionContext =
    {
        SessionId: SessionUid
        CancellationToken: CancellationToken
    }
    static member inline op_Implicit (ctx: CreateTestSessionContext): SessionContext = { SessionId = ctx.SessionUid; CancellationToken = ctx.CancellationToken }
    static member inline op_Implicit (ctx: CloseTestSessionContext): SessionContext = { SessionId = ctx.SessionUid; CancellationToken = ctx.CancellationToken }

[<Struct>]
type SessionOpResult =
    {
        Success: bool
        Error: string voption
        Warning: string voption
    }
    static member op_Implicit(result: SessionOpResult): CloseTestSessionResult =
        let opResult = CloseTestSessionResult(IsSuccess = result.Success)
        result.Error |> ValueOption.iter (fun msg -> opResult.ErrorMessage <- msg)
        result.Warning |> ValueOption.iter (fun msg -> opResult.WarningMessage <- msg)
        opResult
    static member op_Implicit(result: SessionOpResult): CreateTestSessionResult =
        let opResult = CreateTestSessionResult(IsSuccess = result.Success)
        result.Error |> ValueOption.iter (fun msg -> opResult.ErrorMessage <- msg)
        result.Warning |> ValueOption.iter (fun msg -> opResult.WarningMessage <- msg)
        opResult

module SessionContext =
    let sessionUid (ctx: SessionContext) = ctx.SessionId
    let cancellationToken (ctx: SessionContext) = ctx.CancellationToken

module SessionOpResult =
    let create isSuccess = { Success = isSuccess; Error = ValueNone; Warning = ValueNone }
    let withError msg result = { result with Error = ValueSome msg }
    let withWarning msg result = { result with Warning = ValueSome msg }

module ExecuteRequestContext =
    let cancellationToken (ctx: ExecuteRequestContext) = ctx.CancellationToken
    let messageBus (ctx: ExecuteRequestContext) = ctx.MessageBus
    let request (ctx: ExecuteRequestContext) = ctx.Request
    let complete (ctx: ExecuteRequestContext) = ctx.Complete()
type FrameworkCapabilities() =
    interface ITestFrameworkCapabilities with
        member _.Capabilities =
            Array.empty<ITestFrameworkCapability>
            :> IReadOnlyCollection<_>

type Framework() =
    interface IExtension with
        member _.IsEnabledAsync() = Task.FromResult true
        member _.Uid = "Partas.Testing"
        member _.Version = "0.0.1"
        member _.DisplayName = "Partas.Testing"
        member _.Description = "Partas.Testing proof of concept"

    interface IDataProducer with
        member _.DataTypesProduced =
            [| typeof<TestNodeUpdateMessage> |]

    interface ITestFramework with
        member this.CloseTestSessionAsync(context) = task {
            return SessionOpResult.create true
        }
        member this.CreateTestSessionAsync(context) = task {
            SessionContext.sessionUid context
            |> ignore
            return SessionOpResult.create true
        }

        member this.ExecuteRequestAsync(context) =
            task {
                match context.Request with
                | :? DiscoverTestExecutionRequest as request ->

                    let root =
                        TestNode(
                            Uid = "partas.parser",
                            DisplayName = "Parser",
                            Properties =
                                PropertyBag(
                                    DiscoveredTestNodeStateProperty.CachedInstance
                                )
                        )

                    let child1 =
                        TestNode(
                            Uid = "partas.parser.identifier",
                            DisplayName = "parses identifier",
                            Properties =
                                PropertyBag(
                                    DiscoveredTestNodeStateProperty.CachedInstance
                                )
                        )

                    let child2 =
                        TestNode(
                            Uid = "partas.parser.invalid",
                            DisplayName = "rejects invalid input",
                            Properties =
                                PropertyBag(
                                    DiscoveredTestNodeStateProperty.CachedInstance
                                )
                        )

                    // Root
                    do!
                        context.MessageBus.PublishAsync(
                            this,
                            TestNodeUpdateMessage(
                                request.Session.SessionUid,
                                root
                            )
                        )

                    // Child 1
                    do!
                        context.MessageBus.PublishAsync(
                            this,
                            TestNodeUpdateMessage(
                                request.Session.SessionUid,
                                child1,
                                root.Uid
                            )
                        )

                    // Child 2
                    do!
                        context.MessageBus.PublishAsync(
                            this,
                            TestNodeUpdateMessage(
                                request.Session.SessionUid,
                                child2,
                                root.Uid
                            )
                        )

                | :? RunTestExecutionRequest as request ->

                    // Keep this stupid for now:
                    // mark everything as passed.
                    for uid, name in
                        [
                            "partas.parser", "Parser"
                            "partas.parser.identifier", "parses identifier"
                            "partas.parser.invalid", "rejects invalid input"
                        ] do

                    let node =
                        TestNode(
                            Uid = uid,
                            DisplayName = name,
                            Properties =
                                PropertyBag(
                                    PassedTestNodeStateProperty.CachedInstance
                                )
                        )

                    do!
                        context.MessageBus.PublishAsync(
                            this,
                            TestNodeUpdateMessage(
                                request.Session.SessionUid,
                                node
                            )
                        )

                | _ ->
                    ()

                context.Complete()
            }




[<EntryPoint>]
let main argv =
    task {
        let! builder = TestApplication.CreateBuilderAsync(argv)
        builder.RegisterTestFramework(
            (fun _ -> FrameworkCapabilities() :> ITestFrameworkCapabilities),
            (fun _capabilities _services -> Framework() :> ITestFramework)
            )
        |> ignore
        use! app = builder.BuildAsync()
        return! app.RunAsync()
    }
    |> _.GetAwaiter().GetResult()
