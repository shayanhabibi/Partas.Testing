module Partas.Testing.Tests.RunnerTests

open Expecto
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.Testing
open Partas.Testing.Tests.Fakes

let private noop () = ()

let private stateAt (states: Map<string, TestNodeStateProperty>) uid = states.[uid]

[<Tests>]
let tests =
    testList "Runner.run" [
        test "a body that returns is reported passed" {
            let states = runSuite false (Test.list ("s", [ Test.case ("a", noop) ]))

            Expect.isTrue
                (stateAt states "/s/a" :? PassedTestNodeStateProperty)
                "passed"
        }

        test "a body that raises is reported failed, carrying the exception" {
            let boom = exn "expected 1, got 2"

            let states =
                runSuite false (Test.list ("s", [ Test.case ("a", fun () -> raise boom) ]))

            match stateAt states "/s/a" with
            | :? FailedTestNodeStateProperty as failed -> Expect.equal failed.Exception boom "the exception"
            | other -> failtestf "expected failed, got %A" other
        }

        test "one failure does not stop the rest of the run" {
            let tree =
                Test.list ("s", [
                    Test.case ("a", fun () -> failwith "boom")
                    Test.case ("b", noop)
                ])

            let states = runSuite false tree

            Expect.isTrue (stateAt states "/s/a" :? FailedTestNodeStateProperty) "the failure"
            Expect.isTrue (stateAt states "/s/b" :? PassedTestNodeStateProperty) "the test after it"
        }

        test "a pending test is reported skipped and never runs" {
            let mutable ran = false

            let states =
                runSuite false (Test.list ("s", [ Test.pending ("a", fun () -> ran <- true) ]))

            Expect.isTrue (stateAt states "/s/a" :? SkippedTestNodeStateProperty) "skipped"
            Expect.isFalse ran "the body did not run"
        }

        test "the skip reason is reported" {
            let states = runSuite false (Test.list ("s", [ Test.pending ("a", noop) ]))
            Expect.equal (stateAt states "/s/a").Explanation "pending" "the reason"
        }

        test "focus narrows the run and skips the rest" {
            let mutable ranB = false

            let tree =
                Test.list ("s", [
                    Test.focused ("a", noop)
                    Test.case ("b", fun () -> ranB <- true)
                ])

            let states = runSuite false tree

            Expect.isTrue (stateAt states "/s/a" :? PassedTestNodeStateProperty) "the focused test"
            Expect.isTrue (stateAt states "/s/b" :? SkippedTestNodeStateProperty) "the rest"
            Expect.isFalse ranB "the unfocused body did not run"
        }

        test "a platform filter overrides focus" {
            let tree =
                Test.list ("s", [ Test.focused ("a", noop); Test.case ("b", noop) ])

            let states = runSuite true tree

            Expect.isTrue (stateAt states "/s/b" :? PassedTestNodeStateProperty) "the unfocused test runs"
        }

        test "every admitted leaf reaches a terminal state" {
            let tree =
                Test.list ("s", [
                    Test.case ("a", noop)
                    Test.case ("b", fun () -> failwith "boom")
                    Test.pending ("c", noop)
                ])

            let states = runSuite false tree

            Expect.equal (Map.count states) 3 "one terminal state per leaf"
        }
    ]
