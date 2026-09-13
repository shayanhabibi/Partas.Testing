module Partas.TestingPlatform.Tests.OutcomeTests

open System
open Expecto
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.TestingPlatform

let private bagOf result = PropertyBag(TestResult.properties result)

let private stateOf result =
    (bagOf result).Single<TestNodeStateProperty>()

[<Tests>]
let tests =
    testList "TestResult.properties" [
        test "a passed result carries the passed state" {
            let state = stateOf (TestResult.create Passed)
            Expect.isTrue (state :? PassedTestNodeStateProperty) $"expected passed, got %A{state}"
        }

        test "a skipped result carries the skipped state and its explanation" {
            let state = stateOf { TestResult.create Skipped with Explanation = Some "not focused" }

            Expect.isTrue (state :? SkippedTestNodeStateProperty) $"expected skipped, got %A{state}"
            Expect.equal state.Explanation "not focused" "explanation"
        }

        test "a failed result carries the exception" {
            let boom = exn "boom"
            let state = stateOf (TestResult.create (Failed(Some boom, None)))

            match state with
            | :? FailedTestNodeStateProperty as failed -> Expect.equal failed.Exception boom "exception"
            | other -> failtestf "expected failed, got %A" other
        }

        test "an assertion failure is published alongside the state" {
            let result =
                TestResult.create (Failed(None, Some { Expected = Some "1"; Actual = Some "2" }))

            let assertion = (bagOf result).Single<AssertionFailureProperty>()

            Expect.equal assertion.Expected "1" "expected"
            Expect.equal assertion.Actual "2" "actual"
        }

        test "a result without an assertion failure publishes none" {
            let bag = bagOf (TestResult.create (Failed(None, None)))
            Expect.isFalse (bag.Any<AssertionFailureProperty>()) "no assertion property"
        }

        test "an errored result carries the error state" {
            let state = stateOf (TestResult.create (Errored None))
            Expect.isTrue (state :? ErrorTestNodeStateProperty) $"expected error, got %A{state}"
        }

        test "a timed out result carries the timeout and its duration" {
            let timeout = TimeSpan.FromSeconds 3.0
            let state = stateOf (TestResult.create (TimedOut(None, Some timeout)))

            match state with
            | :? TimeoutTestNodeStateProperty as timedOut ->
                Expect.equal timedOut.Timeout (Nullable timeout) "timeout"
            | other -> failtestf "expected timeout, got %A" other
        }

        test "captured output is published" {
            let result =
                { TestResult.create Passed with
                    StandardOutput = Some "out"
                    StandardError = Some "err" }

            let bag = bagOf result
            Expect.equal (bag.Single<StandardOutputProperty>().StandardOutput) "out" "stdout"
            Expect.equal (bag.Single<StandardErrorProperty>().StandardError) "err" "stderr"
        }

        test "a result without captured output publishes none" {
            let bag = bagOf (TestResult.create Passed)

            Expect.isFalse (bag.Any<StandardOutputProperty>()) "no stdout property"
            Expect.isFalse (bag.Any<StandardErrorProperty>()) "no stderr property"
        }

        test "a retry attempt is published" {
            let result =
                { TestResult.create Passed with
                    RetryAttempt = Some { AttemptNumber = 2; IsSuperseded = true } }

            let retry = (bagOf result).Single<RetryAttemptProperty>()

            Expect.equal retry.AttemptNumber 2 "attempt number"
            Expect.isTrue retry.IsSuperseded "superseded"
        }

        test "extra properties are published unchanged" {
            let metadata = TestMetadataProperty("category", "slow")

            let result =
                { TestResult.create Passed with ExtraProperties = [ metadata ] }

            Expect.equal ((bagOf result).Single<TestMetadataProperty>()) metadata "the extra property"
        }
    ]
