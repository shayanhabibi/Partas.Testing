module Partas.Testing.Tests.RunnerTests

open System
open System.Threading
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.Testing
open Partas.TestingPlatform
open Expecto
open Partas.Testing.Tests.Fakes

let private noop () = ()

let private stateAt (states: Map<string, TestNodeStateProperty>) uid = states.[uid]

let private assertionAt (updates: Map<string, TestNodeUpdateMessage>) uid =
    updates.[uid].TestNode.Properties.OfType<AssertionFailureProperty>() |> Array.tryHead

[<Tests>]
let tests =
    testList "Runner.run" [
        test "a body that returns is reported passed" {
            let states = runSuite false (Test.list "s" [ Test.case "a" noop ])

            Expect.isTrue
                (stateAt states "/s/a" :? PassedTestNodeStateProperty)
                "passed"
        }

        test "a body that raises is reported failed, carrying the exception" {
            let boom = exn "expected 1, got 2"

            let states =
                runSuite false (Test.list "s" [ Test.case "a" (fun () -> raise boom) ])

            match stateAt states "/s/a" with
            | :? FailedTestNodeStateProperty as failed -> Expect.equal failed.Exception boom "the exception"
            | other -> failtestf "expected failed, got %A" other
        }

        test "one failure does not stop the rest of the run" {
            let tree =
                Test.list "s" [
                    Test.case "a" (fun () -> failwith "boom")
                    Test.case "b" noop
                ]

            let states = runSuite false tree

            Expect.isTrue (stateAt states "/s/a" :? FailedTestNodeStateProperty) "the failure"
            Expect.isTrue (stateAt states "/s/b" :? PassedTestNodeStateProperty) "the test after it"
        }

        test "a pending test is reported skipped and never runs" {
            let mutable ran = false

            let states =
                runSuite false (Test.list "s" [ Test.pending "a" (fun () -> ran <- true) ])

            Expect.isTrue (stateAt states "/s/a" :? SkippedTestNodeStateProperty) "skipped"
            Expect.isFalse ran "the body did not run"
        }

        test "the skip reason is reported" {
            let states = runSuite false (Test.list "s" [ Test.pending "a" noop ])
            Expect.equal (stateAt states "/s/a").Explanation "pending" "the reason"
        }

        test "focus narrows the run and skips the rest" {
            let mutable ranB = false

            let tree =
                Test.list "s" [
                    Test.focused "a" noop
                    Test.case "b" (fun () -> ranB <- true)
                ]

            let states = runSuite false tree

            Expect.isTrue (stateAt states "/s/a" :? PassedTestNodeStateProperty) "the focused test"
            Expect.isTrue (stateAt states "/s/b" :? SkippedTestNodeStateProperty) "the rest"
            Expect.isFalse ranB "the unfocused body did not run"
        }

        test "a platform filter overrides focus" {
            let tree =
                Test.list "s" [ Test.focused "a" noop; Test.case "b" noop ]

            let states = runSuite true tree

            Expect.isTrue (stateAt states "/s/b" :? PassedTestNodeStateProperty) "the unfocused test runs"
        }

        test "a body raising AssertionException is reported failed, carrying the assertion" {
            let raised = AssertionException("parses an int", Some "1", Some "2")

            let updates =
                runSuiteUpdates false (Test.list "s" [ Test.case "a" (fun () -> raise raised) ])
                |> terminalUpdates

            match updates.["/s/a"].TestNode.Properties.OfType<TestNodeStateProperty>() |> Array.tryHead with
            | Some(:? FailedTestNodeStateProperty as failed) ->
                Expect.equal failed.Exception raised "the exception"
            | other -> failtestf "expected failed, got %A" other

            match assertionAt updates "/s/a" with
            | Some assertion ->
                Expect.equal assertion.Expected "1" "expected"
                Expect.equal assertion.Actual "2" "actual"
            | None -> failtest "expected an assertion failure property"
        }

        test "a body cancelled on the session token is reported skipped, naming cancellation" {
            use cts = new CancellationTokenSource()
            cts.Cancel()

            let body = async { do! Async.Sleep 1000 }

            let states =
                runSuiteUnder cts.Token false (Test.list "s" [ Test.caseAsync "a" body ])

            Expect.isTrue (stateAt states "/s/a" :? SkippedTestNodeStateProperty) "skipped, not failed"
            Expect.isTrue
                ((stateAt states "/s/a").Explanation.Contains "cancel")
                "the explanation names cancellation"
        }

        test "an OperationCanceledException the test raises for its own reasons is reported failed" {
            let body = async { raise (OperationCanceledException "own reasons") }

            let states =
                runSuiteUnder CancellationToken.None false (Test.list "s" [ Test.caseAsync "a" body ])

            Expect.isTrue (stateAt states "/s/a" :? FailedTestNodeStateProperty) "failed, not skipped"
        }

        test "a leaf after the session is cancelled by an earlier leaf reports skipped without running its body" {
            let log = ResizeArray<string>()
            use cts = new CancellationTokenSource()

            let b =
                async {
                    log.Add "b ran"
                    raise (OperationCanceledException "b's own timeout")
                }

            let tree =
                Test.sequentialList "s" [ Test.case "a" (fun () -> cts.Cancel()); Test.caseAsync "b" b ]

            let states = runSuiteUnder cts.Token false tree

            Expect.isEmpty (List.ofSeq log) "b's body never ran once the session was cancelled"
            Expect.isTrue (stateAt states "/s/b" :? SkippedTestNodeStateProperty) "skipped, not failed"
        }

        test "a leaf that cancels the session and then raises its own reason is still reported failed" {
            use cts = new CancellationTokenSource()

            // No `do!` between the two statements: the cancellation and the raise happen in
            // the same synchronous step, so the token is already cancelled by the time the
            // exception is caught, yet this leaf's own logic — not the session — raised it.
            let body =
                async {
                    cts.Cancel()
                    raise (OperationCanceledException "own reasons, mid-flight")
                }

            let states =
                runSuiteUnder cts.Token false (Test.list "s" [ Test.caseAsync "a" body ])

            Expect.isTrue (stateAt states "/s/a" :? FailedTestNodeStateProperty) "failed, not skipped"
        }

        test "every admitted leaf reaches a terminal state" {
            let tree =
                Test.list "s" [
                    Test.case "a" noop
                    Test.case "b" (fun () -> failwith "boom")
                    Test.pending "c" noop
                ]

            let states = runSuite false tree

            Expect.equal (Map.count states) 3 "one terminal state per leaf"
        }
    ]

[<Tests>]
let gracefulStopTests =
    testList "Runner.run under a graceful stop" [
        test "a leaf reached after the stop reports skipped without running its body" {
            let stop = GracefulStop()
            let log = ResizeArray<string>()

            let tree =
                Test.sequentialList "s" [
                    Test.case "a" (fun () ->
                        log.Add "a"
                        stop.Request())
                    Test.case "b" (fun () -> log.Add "b")
                ]

            let states = runSuiteStopping stop false tree

            Expect.sequenceEqual (List.ofSeq log) [ "a" ] "b's body never ran"
            Expect.isTrue (stateAt states "/s/a" :? PassedTestNodeStateProperty) "the leaf that ran keeps its result"
            Expect.isTrue (stateAt states "/s/b" :? SkippedTestNodeStateProperty) "skipped, not failed"
            Expect.stringContains (stateAt states "/s/b").Explanation "stopped" "the explanation names the stop"
        }

        test "a stop already requested skips every leaf" {
            let stop = GracefulStop()
            stop.Request()
            let log = ResizeArray<string>()

            let tree =
                Test.list "s" [ Test.case "a" (fun () -> log.Add "a"); Test.case "b" (fun () -> log.Add "b") ]

            let states = runSuiteStopping stop false tree

            Expect.isEmpty (List.ofSeq log) "no body ran"
            Expect.equal (Map.count states) 2 "both leaves still reach a terminal state"
            Expect.isTrue (stateAt states "/s/a" :? SkippedTestNodeStateProperty) "the first leaf is skipped"
            Expect.isTrue (stateAt states "/s/b" :? SkippedTestNodeStateProperty) "the second leaf is skipped"
        }

        test "a pending leaf keeps its own reason under a stop" {
            let stop = GracefulStop()
            stop.Request()

            let states = runSuiteStopping stop false (Test.list "s" [ Test.pending "a" noop ])

            Expect.equal (stateAt states "/s/a").Explanation "pending" "the source marking wins"
        }

        test "a fixture group not yet entered when the stop arrives runs neither setup nor teardown" {
            let stop = GracefulStop()
            let log = ResizeArray<string>()

            let tree =
                Test.sequentialList "root" [
                    Test.case "first" (fun () -> stop.Request())
                    Test.listWith (
                        "s",
                        (fun () -> async { log.Add "setup" }),
                        (fun () -> async { log.Add "teardown" })
                    )
                        (fun _ -> [ Test.case "a" (fun () -> log.Add "a") ])
                ]

            let states = runSuiteStopping stop false tree

            Expect.isEmpty (List.ofSeq log) "the fixture stayed unused"
            Expect.isTrue (stateAt states "/root/s/a" :? SkippedTestNodeStateProperty) "the leaf under it is skipped"
        }
    ]
