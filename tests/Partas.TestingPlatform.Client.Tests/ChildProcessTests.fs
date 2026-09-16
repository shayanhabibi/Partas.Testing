module Partas.TestingPlatform.Client.Tests.ChildProcessTests

open System
open System.Threading.Tasks
open Expecto
open Partas.TestingPlatform.Client
open Partas.TestingPlatform.Client.Tests.Fixture

/// <summary>
/// Runs <paramref name="body"/> against a freshly launched child-process server and tears the
/// server down afterwards.
/// </summary>
let private withClient (body: MtpClient -> Task<unit>) : Task<unit> =
    task {
        let! client = launchChild ()

        try
            do! body client
        finally
            client.ShutdownAsync().GetAwaiter().GetResult()
            (client :> IDisposable).Dispose()
    }

[<Tests>]
let tests =
    testSequenced
    <| testList "child process" [
        testTask "initialize returns capabilities" {
            do!
                withClient (fun client ->
                    task {
                        let! caps = client.InitializeAsync()
                        Expect.isTrue caps.SupportsDiscovery "supports discovery"
                        Expect.isSome caps.ProtocolVersion "protocol version negotiated"
                        Expect.equal client.Capabilities (Some caps) "cached on the client"
                        Expect.notEqual client.ProcessId Environment.ProcessId "the server runs out of process"
                    })
        }

        testTask "discover-all publishes every leaf once, parents first" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! _ = client.InitializeAsync()
                        do! client.DiscoverTestsAsync()
                        let updates = collector.Updates
                        let leaves = updates |> List.filter (fun u -> u.NodeType = Some NodeType.Action)

                        Expect.equal
                            (leaves |> List.map _.Uid |> List.sort)
                            (List.sort sampleLeafUids)
                            "every leaf exactly once"

                        for leaf in leaves do
                            Expect.equal leaf.ExecutionState (Some ExecutionState.Discovered) leaf.Uid

                        for i, u in List.indexed updates do
                            match u.ParentUid with
                            | Some parent ->
                                let earlier = updates |> List.take i |> List.exists (fun p -> p.Uid = parent)
                                Expect.isTrue earlier $"parent {parent} of {u.Uid} published earlier"
                            | None -> ()
                    })
        }

        testTask "run-all publishes a terminal state for every leaf" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! _ = client.InitializeAsync()
                        let! result = client.RunTestsAsync()

                        let byUid =
                            collector.Updates
                            |> List.filter terminal
                            |> List.map (fun u -> u.Uid, u)
                            |> Map.ofList

                        Expect.equal
                            (byUid |> Map.keys |> List.ofSeq |> List.sort)
                            (List.sort sampleLeafUids)
                            "terminal for every leaf"

                        Expect.equal
                            byUid["/parser/literals/parses an int"].ExecutionState
                            (Some ExecutionState.Passed)
                            "passed"

                        Expect.equal
                            byUid["/parser/literals/parses a float"].ExecutionState
                            (Some ExecutionState.Skipped)
                            "skipped"

                        let failed = byUid["/parser/literals/rejects a malformed int"]
                        Expect.equal failed.ExecutionState (Some ExecutionState.Failed) "failed"

                        Expect.stringContains
                            (failed.Error |> Option.bind _.Message |> Option.defaultValue "")
                            "unexpected 'x'"
                            "error message"

                        Expect.isSome byUid["/parser/literals/parses an int"].Duration "duration reported"
                        Expect.isEmpty result.Attachments "a plain run produces no attachments"
                    })
        }

        testTask "run by uid list executes only the named leaves" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! _ = client.InitializeAsync()
                        let! _ = client.RunTestsAsync([ "/parser/reports the source span" ])
                        let ran = collector.Updates |> List.filter terminal |> List.map _.Uid |> List.distinct
                        Expect.equal ran [ "/parser/reports the source span" ] "only the named leaf"
                    })
        }

        testTask "run with a graph filter executes only matching leaves" {
            do!
                withClient (fun client ->
                    task {
                        let collector = collect client
                        let! _ = client.InitializeAsync()
                        let! _ = client.RunTestsWithFilterAsync "/parser/literals/*"
                        let ran = collector.Updates |> List.filter terminal |> List.map _.Uid |> List.sort

                        let expected =
                            sampleLeafUids
                            |> List.filter (fun u -> u.StartsWith "/parser/literals/")
                            |> List.sort

                        Expect.equal ran expected "literals only"
                    })
        }

        testTask "exit then shutdown yields an exit code and dispose is idempotent" {
            let! (client: MtpClient) = launchChild ()

            try
                let! _ = client.InitializeAsync()
                do! client.ExitAsync()
                let exitCode = client.ServerExitCode
                Expect.isSome exitCode "the exit notification ended the application"
                do! client.ShutdownAsync()
                Expect.equal client.ServerExitCode exitCode "shutdown preserves the observed exit code"
                (client :> IDisposable).Dispose()
                (client :> IDisposable).Dispose()
            finally
                client.ShutdownAsync().GetAwaiter().GetResult()
                (client :> IDisposable).Dispose()
        }
    ]
