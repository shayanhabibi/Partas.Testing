module Partas.TestingPlatform.Client.Tests.InProcessTests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Partas.TestingPlatform.Client
open Partas.TestingPlatform.Client.Tests.Fixture

/// <summary>
/// Runs <paramref name="body"/> against a server hosted in this process and tears the server down
/// afterwards.
/// </summary>
let private withClient (body: MtpClient -> Task<unit>) : Task<unit> =
    task {
        let! client = launchInProcess []

        try
            do! body client
        finally
            client.ShutdownAsync().GetAwaiter().GetResult()
            (client :> IDisposable).Dispose()
    }

[<Tests>]
let tests =
    testSequenced
    <| testList "in process" [
        testTask "discover-all publishes every leaf as discovered" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! caps = client.InitializeAsync()
                        Expect.isTrue caps.SupportsDiscovery "supports discovery"
                        Expect.equal client.ProcessId Environment.ProcessId "the server runs in this process"
                        do! client.DiscoverTestsAsync()

                        let byType =
                            collector.Updates |> List.groupBy _.NodeType |> Map.ofList

                        let leaves =
                            byType[Some NodeType.Action] |> List.map _.Uid |> List.distinct |> List.sort

                        Expect.equal leaves (List.sort sampleLeafUids) "a node per leaf"

                        Expect.all
                            byType[Some NodeType.Action]
                            (fun u -> u.ExecutionState = Some ExecutionState.Discovered)
                            "every leaf is discovered"

                        Expect.all
                            byType[Some NodeType.Group]
                            (fun u -> u.ExecutionState = None)
                            "a group carries no execution state"
                    })
        }

        testTask "run-all reports each leaf's outcome" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! _ = client.InitializeAsync()
                        let! _ = client.RunTestsAsync()

                        let byUid =
                            collector.Updates
                            |> List.filter terminal
                            |> List.map (fun u -> u.Uid, u)
                            |> Map.ofList

                        Expect.equal
                            (byUid |> Map.keys |> List.ofSeq |> List.sort)
                            (List.sort sampleLeafUids)
                            "a terminal update per leaf"

                        Expect.equal
                            byUid["/parser/literals/parses an int"].ExecutionState
                            (Some ExecutionState.Passed)
                            "passed"

                        Expect.equal
                            byUid["/parser/literals/rejects a malformed int"].ExecutionState
                            (Some ExecutionState.Failed)
                            "failed"

                        Expect.equal
                            byUid["/parser/literals/parses a float"].ExecutionState
                            (Some ExecutionState.Skipped)
                            "skipped"
                    })
        }

        testTask "the attachment stream stays empty without a report producer" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! _ = client.InitializeAsync()
                        let! result = client.RunTestsAsync()
                        Expect.isEmpty result.Attachments "the run result carries no attachment"
                        Expect.isEmpty collector.Attachments "no attachment notification arrives"
                    })
        }

        testTask "exit ends the hosted application" {
            let! (client: MtpClient) = launchInProcess []

            try
                let! _ = client.InitializeAsync()
                do! client.ExitAsync()
                do! client.ShutdownAsync()
                Expect.equal client.ServerExitCode (Some 0) "the hosted application exited cleanly"
            finally
                client.ShutdownAsync().GetAwaiter().GetResult()
                (client :> IDisposable).Dispose()
        }

        testTask "cancelling a run mid-flight settles the call" {
            let! (client: MtpClient) = launchSlowInProcess ()
            let cts = new CancellationTokenSource()

            try
                let inProgress = TaskCompletionSource()

                client.TestNodesUpdated.Add(fun batch ->
                    if batch.Updates |> List.exists (fun u -> u.ExecutionState = Some ExecutionState.InProgress) then
                        inProgress.TrySetResult() |> ignore)

                let! _ = client.InitializeAsync()
                let run = client.RunTestsAsync cts.Token
                let! started = Task.WhenAny(inProgress.Task, Task.Delay(TimeSpan.FromSeconds 30.0))
                Expect.equal started inProgress.Task "the slow leaf reported in progress"
                cts.Cancel()
                let! finished = Task.WhenAny((run :> Task), Task.Delay(TimeSpan.FromSeconds 30.0))
                Expect.equal finished (run :> Task) "the run call settled after the cancel"

                let error =
                    try
                        run.GetAwaiter().GetResult() |> ignore
                        None
                    with e ->
                        Some e

                match error with
                | Some e ->
                    Expect.isTrue
                        (e :? OperationCanceledException)
                        $"cancellation passes through, got %s{e.GetType().Name}: %s{e.Message}"
                | None -> ()
            finally
                cts.Dispose()
                client.ShutdownAsync().GetAwaiter().GetResult()
                (client :> IDisposable).Dispose()
        }
    ]
