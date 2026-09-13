module Partas.Testing.Tests.ParallelTests

open System
open System.Collections.Concurrent
open System.Threading
open Partas.Testing
open Expecto
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.Testing.Tests.Fakes
open Partas.TestingPlatform

let private noop () = ()

let private idle () = async { return () }

/// <summary>Milliseconds a body waits for a partner it expects to be running already.</summary>
let private patient = 10_000

/// <summary>Milliseconds a body waits for a partner it expects never to arrive.</summary>
let private impatient = 250

/// <summary>
/// Two bodies that each complete once the other has started. The body that runs first raises
/// after the given milliseconds elapse, so execution in order fails rather than hangs.
/// </summary>
let private rendezvous (patience: int) =
    let first = new SemaphoreSlim(0)
    let second = new SemaphoreSlim(0)

    let meet (mine: SemaphoreSlim) (theirs: SemaphoreSlim) =
        async {
            mine.Release() |> ignore
            let! met = theirs.WaitAsync patience |> Async.AwaitTask

            if not met then
                failwith "the partner leaf never started"
        }

    meet first second, meet second first

let private overlapping patience =
    let a, b = rendezvous patience
    [ Test.caseAsync "a" a; Test.caseAsync "b" b ]

let private passed (states: Map<string, TestNodeStateProperty>) uid =
    states.[uid] :? PassedTestNodeStateProperty

let private outcomes (states: Map<string, TestNodeStateProperty>) =
    states |> Map.map (fun _ state -> state.GetType().Name)

[<Tests>]
let tests =
    testList "Runner.run parallelism" [
        test "leaves in a parallel group overlap" {
            let states = runSuite false (Test.parallelList "s" (overlapping patient))

            Expect.isTrue (passed states "/s/a") "the leaf that waited first"
            Expect.isTrue (passed states "/s/b") "the leaf that waited second"
        }

        test "leaves in a sequential group do not overlap" {
            let states = runSuite false (Test.sequentialList "s" (overlapping impatient))

            Expect.isTrue
                (states.["/s/a"] :? FailedTestNodeStateProperty)
                "the leaf that waited first ran alone"
        }

        test "leaves in a sequential group run in the order written" {
            let log = ConcurrentQueue<string>()

            let tree =
                Test.sequentialList "s" [
                    for name in [ "a"; "b"; "c"; "d" ] -> Test.case name (fun () -> log.Enqueue name)
                ]

            runSuite false tree |> ignore

            Expect.sequenceEqual (List.ofSeq log) [ "a"; "b"; "c"; "d" ] "the order run"
        }

        test "the root runs its children in parallel" {
            let states = runSuite false (Test.list "s" (overlapping patient))

            Expect.isTrue (passed states "/s/a") "the leaf that waited first"
            Expect.isTrue (passed states "/s/b") "the leaf that waited second"
        }

        test "a group inherits its parent's mode" {
            let tree = Test.sequentialList "outer" [ Test.list "inner" (overlapping impatient) ]

            let states = runSuite false tree

            Expect.isTrue
                (states.["/outer/inner/a"] :? FailedTestNodeStateProperty)
                "the inherited mode reached the unmarked group"
        }

        test "a group overrides the mode it inherits" {
            let tree = Test.sequentialList "outer" [ Test.parallelList "inner" (overlapping patient) ]

            let states = runSuite false tree

            Expect.isTrue (passed states "/outer/inner/a") "the leaf that waited first"
            Expect.isTrue (passed states "/outer/inner/b") "the leaf that waited second"
        }

        test "sibling groups of a parallel parent overlap" {
            let a, b = rendezvous patient

            let tree =
                Test.parallelList "outer" [
                    Test.list "left" [ Test.caseAsync "a" a ]
                    Test.list "right" [ Test.caseAsync "b" b ]
                ]

            let states = runSuite false tree

            Expect.isTrue (passed states "/outer/left/a") "the leaf in the first group"
            Expect.isTrue (passed states "/outer/right/b") "the leaf in the second group"
        }

        test "a fixture group runs its leaves in order under a parallel parent" {
            let tree =
                Test.parallelList "outer" [
                    Test.listWith ("s", (fun () -> idle ()), (fun () -> idle ()))
                        (fun _ -> overlapping impatient)
                ]

            let states = runSuite false tree

            Expect.isTrue
                (states.["/outer/s/a"] :? FailedTestNodeStateProperty)
                "the fixture group defaulted to sequential"
        }

        test "a fixture group marked parallel overlaps its leaves" {
            let tree =
                Test.parallelListWith ("s", (fun () -> idle ()), (fun () -> idle ()))
                    (fun _ -> overlapping patient)

            let states = runSuite false tree

            Expect.isTrue (passed states "/s/a") "the leaf that waited first"
            Expect.isTrue (passed states "/s/b") "the leaf that waited second"
        }

        test "a parallel fixture group brackets its leaves with setup and teardown" {
            let log = ConcurrentQueue<string>()
            let a, b = rendezvous patient

            let tree =
                Test.parallelListWith (
                    "s",
                    (fun () -> async { log.Enqueue "setup" }),
                    (fun () -> async { log.Enqueue "teardown" })
                )
                    (fun _ ->
                        [ Test.caseAsync "a" (async {
                              do! a
                              log.Enqueue "a"
                          })
                          Test.caseAsync "b" (async {
                              do! b
                              log.Enqueue "b"
                          }) ])

            runSuite false tree |> ignore

            let entries = List.ofSeq log

            Expect.equal (List.head entries) "setup" "setup preceded every leaf"
            Expect.equal (List.last entries) "teardown" "teardown followed every leaf"
            Expect.containsAll entries [ "a"; "b" ] "both leaves ran"
        }

        test "a nested fixture group's setup precedes its own leaves under a parallel ancestor" {
            let log = ConcurrentQueue<string>()

            let tree =
                Test.parallelList "outer" [
                    Test.listWith (
                        "s",
                        (fun () -> async { log.Enqueue "setup" }),
                        (fun () -> async { log.Enqueue "teardown" })
                    )
                        (fun _ -> [ Test.case "a" (fun () -> log.Enqueue "a") ])
                ]

            runSuite false tree |> ignore

            Expect.sequenceEqual (List.ofSeq log) [ "setup"; "a"; "teardown" ] "the order run"
        }

        test "every leaf of a parallel group is reported exactly once" {
            let tree =
                Test.parallelList "s" [
                    for index in 1..32 -> Test.case (string index) noop
                ]

            let terminal = runSuiteUpdates false tree |> terminalList

            Expect.equal (List.length terminal) 32 "one terminal update per leaf"
            Expect.equal (List.length (List.distinct (List.map fst terminal))) 32 "each uid reported once"

            Expect.isTrue
                (terminal |> List.forall (fun (_, update) -> stateOf update :? PassedTestNodeStateProperty))
                "every leaf passed"
        }

        test "a parallel group reports the outcomes its sequential twin reports" {
            let suite (list: string -> TestTree<TestBody> list -> TestTree<TestBody>) =
                list "s" [
                    Test.case "passes" noop
                    Test.case "fails" (fun () -> failwith "boom")
                    Test.pending "dormant" noop
                    list "inner" [ Test.case "nested" noop; Test.caseAsync "async" (idle ()) ]
                ]

            let ordered = outcomes (runSuite false (suite Test.sequentialList))
            let concurrent = outcomes (runSuite false (suite Test.parallelList))

            Expect.equal concurrent ordered "the same outcome per uid"
        }

        test "a leaf not yet started when the session is already cancelled is reported skipped" {
            use source = new CancellationTokenSource()
            source.Cancel()

            let tree =
                Test.parallelList "s" [ Test.caseAsync "a" (async { do! Async.Sleep 1000 }) ]

            let states = runSuiteUnder source.Token false tree

            Expect.isTrue (states.["/s/a"] :? SkippedTestNodeStateProperty) "skipped, not failed"
        }

        test "a leaf awaiting when a sibling cancels the session is reported skipped" {
            use source = new CancellationTokenSource()
            let started = new SemaphoreSlim(0)
            let cancelled = new SemaphoreSlim(0)

            let tree =
                Test.parallelList "s" [
                    Test.caseAsync "waits" (async {
                        started.Release() |> ignore
                        let! _ = cancelled.WaitAsync patient |> Async.AwaitTask
                        do! Async.Sleep 20
                    })
                    Test.caseAsync "cancels" (async {
                        let! _ = started.WaitAsync patient |> Async.AwaitTask
                        source.Cancel()
                        cancelled.Release() |> ignore
                    })
                ]

            let states = runSuiteUnder source.Token false tree

            Expect.isTrue
                (states.["/s/waits"] :? SkippedTestNodeStateProperty)
                "the leaf that was mid-flight"
        }

        test "a leaf inside a synchronous body when a sibling cancels the session is reported skipped" {
            use source = new CancellationTokenSource()
            let started = new SemaphoreSlim(0)
            let cancelled = new SemaphoreSlim(0)

            let tree =
                Test.parallelList "s" [
                    Test.case "computes" (fun () ->
                        started.Release() |> ignore
                        cancelled.Wait patient |> ignore)
                    Test.caseAsync "cancels" (async {
                        let! _ = started.WaitAsync patient |> Async.AwaitTask
                        source.Cancel()
                        cancelled.Release() |> ignore
                    })
                ]

            let states = runSuiteUnder source.Token false tree

            Expect.isTrue
                (states.["/s/computes"] :? SkippedTestNodeStateProperty)
                "the token is read once more after the body returns"
        }

        test "a leaf that finished before a sibling cancels the session keeps its result" {
            use source = new CancellationTokenSource()
            let reported = new SemaphoreSlim(0)

            // Teardown runs after the group's last leaf was reported, so releasing there orders
            // the cancellation strictly after the terminal update for "early".
            let tree =
                Test.parallelList "s" [
                    Test.listWith ("finished", (fun () -> idle ()), (fun () -> async { reported.Release() |> ignore }))
                        (fun _ -> [ Test.case "early" noop ])
                    Test.caseAsync "cancels" (async {
                        let! _ = reported.WaitAsync patient |> Async.AwaitTask
                        source.Cancel()
                    })
                ]

            let updates = runSuiteUpdatesUnder source.Token false tree
            let terminal = terminalList updates |> List.filter (fun (uid, _) -> uid = "/s/finished/early")

            match terminal with
            | [ (_, update) ] ->
                Expect.isTrue (stateOf update :? PassedTestNodeStateProperty) "the leaf that had already finished"
            | other -> failtestf "expected one terminal update, got %i" (List.length other)
        }

        test "a body raising its own cancellation inside a parallel group is reported failed" {
            let tree =
                Test.parallelList "s" [
                    Test.caseAsync "a" (async { raise (OperationCanceledException "own reasons") })
                ]

            let states = runSuite false tree

            Expect.isTrue (states.["/s/a"] :? FailedTestNodeStateProperty) "failed, not skipped"
        }

        test "focus narrows a parallel group" {
            let tree = Test.parallelList "s" [ Test.focused "a" noop; Test.case "b" noop ]

            let states = runSuite false tree

            Expect.isTrue (passed states "/s/a") "the focused leaf"
            Expect.isTrue (states.["/s/b"] :? SkippedTestNodeStateProperty) "the rest"
        }
    ]
