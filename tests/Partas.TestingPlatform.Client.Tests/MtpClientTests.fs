module Partas.TestingPlatform.Client.Tests.MtpClientTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Expecto
open Partas.TestingPlatform.Client

type private UpstreamRequestHandler =
    Func<string, IDictionary<string, obj>, CancellationToken, Task<IDictionary<string, obj>>>

/// <summary>
/// An <c>IMtpServerClient</c> that records the handler assigned to its
/// <c>ServerRequestHandler</c> property. Every request method raises.
/// </summary>
let private fakeServer () =
    let updates =
        Event<EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdateEventArgs>, Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdateEventArgs>()
    let logs =
        Event<EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpLogEventArgs>, Microsoft.Testing.Platform.ServerMode.Client.MtpLogEventArgs>()
    let telemetry =
        Event<EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpTelemetryEventArgs>, Microsoft.Testing.Platform.ServerMode.Client.MtpTelemetryEventArgs>()
    let attachments =
        Event<EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpAttachmentsEventArgs>, Microsoft.Testing.Platform.ServerMode.Client.MtpAttachmentsEventArgs>()
    let mutable assigned: UpstreamRequestHandler = null

    let server =
        { new Microsoft.Testing.Platform.ServerMode.Client.IMtpServerClient with
            [<CLIEvent>]
            member _.TestNodesUpdated = updates.Publish
            [<CLIEvent>]
            member _.LogReceived = logs.Publish
            [<CLIEvent>]
            member _.TelemetryReceived = telemetry.Publish
            [<CLIEvent>]
            member _.AttachmentsReceived = attachments.Publish

            member _.ProcessId = 0
            member _.Capabilities = null
            member _.ServerExitCode = Nullable()

            member _.ServerRequestHandler
                with get () = assigned
                and set value = assigned <- value

            member _.InitializeAsync(_: CancellationToken) = failwith "not used"
            member _.DiscoverTestsAsync(_: CancellationToken) = failwith "not used"
            member _.DiscoverTestsAsync(_: IReadOnlyCollection<string>, _: CancellationToken) = failwith "not used"
            member _.DiscoverTestsWithFilterAsync(_: string, _: CancellationToken) = failwith "not used"
            member _.RunTestsAsync(_: CancellationToken) = failwith "not used"
            member _.RunTestsAsync(_: IReadOnlyCollection<string>, _: CancellationToken) = failwith "not used"
            member _.RunTestsWithFilterAsync(_: string, _: CancellationToken) = failwith "not used"
            member _.ExitAsync(_: CancellationToken) = failwith "not used"
            member _.ShutdownAsync() = Task.CompletedTask
          interface IDisposable with
            member _.Dispose() = () }

    server, (fun () -> assigned)

let private pairs (source: KeyValuePair<string, obj> seq) =
    source |> Seq.map (fun entry -> entry.Key, entry.Value) |> List.ofSeq

let private invoke (handler: UpstreamRequestHandler) (name: string) (parameters: IDictionary<string, obj>) =
    handler.Invoke(name, parameters, CancellationToken.None).GetAwaiter().GetResult()

[<Tests>]
let tests =
    testList "MtpClient" [
        testCase "ServerRequestHandler forwards the method name and parameters" <| fun () ->
            let server, assigned = fakeServer ()
            use client = new MtpClient(server, MtpClientOptions.Default, false)
            let mutable received = None
            let handler: ServerRequestHandler =
                fun name parameters _ ->
                    received <- Some(name, parameters)
                    Task.FromResult None
            client.ServerRequestHandler <- Some handler

            invoke (assigned ()) "client/attachDebugger" (Dictionary<string, obj>(dict [ "processId", box 7 ]))
            |> ignore

            match received with
            | Some(name, Some parameters) ->
                Expect.equal name "client/attachDebugger" "method name"
                Expect.equal (pairs parameters) [ "processId", box 7 ] "parameters"
            | other -> failtestf "expected a name and parameters, got %A" other

        testCase "ServerRequestHandler returns the handler's result as a dictionary" <| fun () ->
            let server, assigned = fakeServer ()
            use client = new MtpClient(server, MtpClientOptions.Default, false)
            let handler: ServerRequestHandler =
                fun _ _ _ ->
                    Dictionary<string, obj>(dict [ "attached", box true ]) :> IReadOnlyDictionary<string, obj>
                    |> Some
                    |> Task.FromResult
            client.ServerRequestHandler <- Some handler

            let result = invoke (assigned ()) "client/attachDebugger" (Dictionary<string, obj>())
            Expect.isNotNull result "result"
            Expect.equal (pairs result) [ "attached", box true ] "result pairs"

        testCase "ServerRequestHandler answers a None result with null" <| fun () ->
            let server, assigned = fakeServer ()
            use client = new MtpClient(server, MtpClientOptions.Default, false)
            client.ServerRequestHandler <- Some(fun _ _ _ -> Task.FromResult None)

            let result = invoke (assigned ()) "telemetry/update" (Dictionary<string, obj>())
            Expect.isNull result "result"

        testCase "ServerRequestHandler passes a null parameter object through as None" <| fun () ->
            let server, assigned = fakeServer ()
            use client = new MtpClient(server, MtpClientOptions.Default, false)
            let mutable received = Some(Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>)
            let handler: ServerRequestHandler =
                fun _ parameters _ ->
                    received <- parameters
                    Task.FromResult None
            client.ServerRequestHandler <- Some handler

            invoke (assigned ()) "telemetry/update" null |> ignore
            Expect.isNone received "parameters"

        testCase "setting ServerRequestHandler to None clears the upstream handler" <| fun () ->
            let server, assigned = fakeServer ()
            use client = new MtpClient(server, MtpClientOptions.Default, false)
            client.ServerRequestHandler <- Some(fun _ _ _ -> Task.FromResult None)
            Expect.isNotNull (assigned ()) "upstream handler assigned"

            client.ServerRequestHandler <- None
            Expect.isNone client.ServerRequestHandler "client handler"
            Expect.isNull (assigned ()) "upstream handler cleared"
    ]
