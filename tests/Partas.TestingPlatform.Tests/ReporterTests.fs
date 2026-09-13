module Partas.TestingPlatform.Tests.ReporterTests

open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.TestHost
open Partas.TestingPlatform
open Partas.TestingPlatform.Tests.Fakes

let private session = SessionUid "session"

let private leaf =
    { Node = { Uid = "/parser/a"; Name = "a"; Location = None; Properties = [] }
      Parent = Some "/parser"
      Payload = () }

let private run body =
    let bus = RecordingMessageBus()
    let reporter = Reporter(bus, StubProducer(), session)
    let result = reporter.Run(leaf, body, CancellationToken.None).GetAwaiter().GetResult()
    bus.Updates, result

let private returning result = fun (_: CancellationToken) -> Task.FromResult result

let private stateOf (update: TestNodeUpdateMessage) =
    update.TestNode.Properties.Single<TestNodeStateProperty>()

[<Tests>]
let tests =
    testList "Reporter.Run" [
        test "progress precedes the outcome" {
            let updates, _ = run (returning (TestResult.create Passed))

            Expect.hasLength updates 2 "two updates"
            Expect.isTrue (stateOf updates.[0] :? InProgressTestNodeStateProperty) "first is in progress"
            Expect.isTrue (stateOf updates.[1] :? PassedTestNodeStateProperty) "second is the outcome"
        }

        test "progress is published before the body runs" {
            let bus = RecordingMessageBus()
            let reporter = Reporter(bus, StubProducer(), session)
            let mutable seenByBody = -1

            let body _ =
                seenByBody <- List.length bus.Updates
                Task.FromResult(TestResult.create Passed)

            reporter.Run(leaf, body, CancellationToken.None).GetAwaiter().GetResult() |> ignore

            Expect.equal seenByBody 1 "the body sees the in-progress update already published"
        }

        test "every update carries the leaf's uid and parent" {
            let updates, _ = run (returning (TestResult.create Passed))

            for update in updates do
                Expect.equal update.TestNode.Uid.Value "/parser/a" "uid"
                Expect.equal update.ParentTestNodeUid.Value "/parser" "parent"
        }

        test "the outcome carries the elapsed timing" {
            let updates, _ = run (returning (TestResult.create Passed))
            let timing = updates.[1].TestNode.Properties.Single<TimingProperty>()

            Expect.isGreaterThanOrEqual
                timing.GlobalTiming.EndTime
                timing.GlobalTiming.StartTime
                "the run does not end before it starts"
        }

        test "progress carries no timing" {
            let updates, _ = run (returning (TestResult.create Passed))
            Expect.isFalse (updates.[0].TestNode.Properties.Any<TimingProperty>()) "no timing yet"
        }

        test "a body that raises is published as errored and returned" {
            let boom = exn "boom"
            let updates, result = run (fun _ -> raise boom)

            match stateOf updates.[1] with
            | :? ErrorTestNodeStateProperty as errored -> Expect.equal errored.Exception boom "the exception"
            | other -> failtestf "expected error, got %A" other

            Expect.equal result.Outcome (Errored(Some boom)) "the returned outcome"
        }

        test "a faulted task is published as errored" {
            let boom = exn "boom"
            let updates, _ = run (fun _ -> Task.FromException<TestResult> boom)

            Expect.isTrue (stateOf updates.[1] :? ErrorTestNodeStateProperty) "errored"
        }
    ]
