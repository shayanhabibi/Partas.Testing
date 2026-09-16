module Partas.TestingPlatform.Client.Tests.Fixture

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Partas.TestingPlatform
open Partas.TestingPlatform.Client

let sampleDll = Path.Combine(AppContext.BaseDirectory, "Partas.TestingPlatform.Sample.dll")

let sampleLeafUids =
    [ "/parser/literals/parses an int"
      "/parser/literals/rejects a malformed int"
      "/parser/literals/parses a float"
      "/parser/reports the source span" ]

let options =
    { MtpClientOptions.Default with
        ConnectionTimeout = TimeSpan.FromSeconds 60.0
        ServerShutdownTimeout = TimeSpan.FromSeconds 20.0 }

let launchChild () : Task<MtpClient> =
    if not (File.Exists sampleDll) then
        failwithf $"expected the sample dll beside the test output at %s{sampleDll}"

    MtpClient.LaunchAsync(sampleDll, options)

let private hostInProcess (definition: FrameworkDefinition<'T>) (extraArgs: string list) : Task<MtpClient> =
    let run (args: string[]) (_: CancellationToken) : Task<int> =
        Task.Run(fun () -> definition |> TestApplication.run (Array.append args (List.toArray extraArgs)))

    MtpClient.LaunchInProcessAsync(run, options)

let launchInProcess (extraArgs: string list) : Task<MtpClient> =
    hostInProcess Partas.TestingPlatform.Sample.Program.definition extraArgs

/// <summary>A suite with a single leaf that blocks until the run is cancelled.</summary>
let slowDefinition =
    let suite: TestTree<unit -> Task<TestOutcome>> =
        Group(
            "slow",
            None,
            [],
            [ Leaf("waits for cancellation", None, [], (fun () -> Task.FromResult Passed)) ]
        )

    let runTests (context: RunContext<unit -> Task<TestOutcome>>) : Task =
        task {
            for leaf in context.Leaves do
                let body _ =
                    task {
                        do! Task.Delay(TimeSpan.FromMinutes 5.0, context.CancellationToken)
                        return TestResult.create Passed
                    }

                let! _ = context.Reporter.Run(leaf, body, context.CancellationToken)
                ()
        }

    testFramework<unit -> Task<TestOutcome>> {
        uid "Partas.TestingPlatform.Client.Tests.Slow"
        version "0.1.0"
        displayName "Slow suite"
        tests (fun () -> suite)
        onRun runTests
    }

let launchSlowInProcess () : Task<MtpClient> = hostInProcess slowDefinition []

/// <summary>Accumulates a client's node updates and attachment batches in arrival order.</summary>
type Collector(client: MtpClient) =
    let updates = ResizeArray<TestNodeUpdate>()
    let attachments = ResizeArray<Attachment list>()
    let completed = TaskCompletionSource()

    do
        client.TestNodesUpdated.Add(fun batch ->
            lock updates (fun () -> updates.AddRange batch.Updates)

            if List.isEmpty batch.Updates then
                completed.TrySetResult() |> ignore)

        client.AttachmentsReceived.Add(fun a -> lock attachments (fun () -> attachments.Add a))

    member _.Updates: TestNodeUpdate list = lock updates (fun () -> List.ofSeq updates)
    member _.Attachments: Attachment list list = lock attachments (fun () -> List.ofSeq attachments)

    /// <summary>
    /// Resolves on the first batch carrying no updates. The upstream client discards the
    /// protocol's completion sentinel, leaving this pending against a server whose batches all
    /// carry updates; await the discovery or run request for the ordering guarantee.
    /// </summary>
    member _.Completed: Task = completed.Task

let collect (client: MtpClient) = Collector client

/// <summary>Whether an update reports an execution state that ends a node.</summary>
let terminal (u: TestNodeUpdate) =
    match u.ExecutionState with
    | Some ExecutionState.Passed
    | Some ExecutionState.Skipped
    | Some ExecutionState.Failed
    | Some ExecutionState.TimedOut
    | Some ExecutionState.Error
    | Some ExecutionState.Canceled -> true
    | _ -> false
