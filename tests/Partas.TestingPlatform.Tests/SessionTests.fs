module Partas.TestingPlatform.Tests.SessionTests

open Expecto
open System
open System.Diagnostics
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Partas.TestingPlatform

[<Tests>]
let tests =
    testList "Session.resolveTree" [
        test "a resolvable tree resolves" {
            let build () = Group("parser", None, [], [ Leaf("a", None, [], ()) ])

            match Session.resolveTree build with
            | Ok (ResolvedGroup(node, _)) -> Expect.equal node.Uid "/parser" "the resolved root"
            | other -> failtestf "expected a resolved group, got %A" other
        }

        test "a colliding tree reports the colliding uid" {
            let build () =
                Group("parser", None, [], [ Leaf("a", None, [], ()); Leaf("a", None, [], ()) ])

            match Session.resolveTree build with
            | Error message -> Expect.stringContains message "/parser/a" "names the collision"
            | Ok _ -> failtest "expected a collision"
        }

        test "a tree that fails to build reports the failure" {
            let build () : TestTree<unit> = failwith "no connection"

            match Session.resolveTree build with
            | Error message -> Expect.stringContains message "no connection" "names the failure"
            | Ok _ -> failtest "expected a build failure"
        }
    ]

let runTreeFailure () =
    testFramework<unit> {
        uid "failed-session-tree"
        onCreateSession (fun _ -> Task.FromResult(SessionOutcome.Succeeded(Some "setup degraded")))
        tests (fun () -> failwith "tree unavailable")
    }
    |> TestApplication.run [| "--list-tests"; "--progress"; "off" |]

[<Tests>]
let lifecycleTests =
    testSequenced <| testList "MTP session lifecycle" [
        test "discovery awaits setup before building and cleanup before returning" {
            let events = ResizeArray<string>()
            let mutable created = None
            let mutable closed = None
            let definition =
                testFramework<unit> {
                    uid "session-lifecycle"
                    onCreateSession (fun context -> task {
                        do! Task.Yield()
                        created <- Some context.SessionId
                        events.Add "create"
                        return SessionOutcome.Succeeded None
                    })
                    tests (fun () ->
                        events.Add "tree"
                        Group("root", None, [], [ Leaf("test", None, [], ()) ]))
                    onCloseSession (fun context -> task {
                        do! Task.Yield()
                        closed <- Some context.SessionId
                        events.Add "close"
                        return SessionOutcome.Succeeded None
                    })
                }
            let exitCode = TestApplication.run [| "--list-tests"; "--progress"; "off" |] definition
            Expect.equal exitCode 0 "MTP discovery succeeds"
            Expect.sequenceEqual events [ "create"; "tree"; "close" ] "callbacks are awaited in lifecycle order"
            Expect.isSome created "MTP supplied a session"
            Expect.equal closed created "cleanup receives the same MTP session"
        }

        test "failed setup prevents tree construction and execution" {
            let mutable built = false
            let mutable ran = false
            let definition =
                testFramework<unit> {
                    uid "failed-session-setup"
                    tests (fun () ->
                        built <- true
                        Group("root", None, [], [ Leaf("test", None, [], ()) ]))
                    onRun (fun _ -> ran <- true; Task.CompletedTask)
                }
                |> FrameworkDefinition.withCreateSession (fun _ ->
                    Task.FromResult(SessionOutcome.Failed("setup unavailable", Some "setup warning")))
            let exitCode = TestApplication.run [| "--progress"; "off" |] definition
            Expect.notEqual exitCode 0 "MTP rejects the failed session"
            Expect.isFalse built "tree must not access unavailable session resources"
            Expect.isFalse ran "no execution after setup failure"
        }

        test "failed cleanup makes otherwise successful discovery fail" {
            let definition =
                testFramework<unit> {
                    uid "failed-session-cleanup"
                    tests (fun () -> Group("root", None, [], [ Leaf("test", None, [], ()) ]))
                }
                |> FrameworkDefinition.withCloseSession (fun _ -> task {
                    do! Task.Yield()
                    return SessionOutcome.Failed("cleanup unavailable", Some "cleanup warning")
                })
            let exitCode = TestApplication.run [| "--list-tests"; "--progress"; "off" |] definition
            Expect.notEqual exitCode 0 "MTP receives cleanup failure"
        }

        test "tree failure preserves the setup warning in MTP diagnostics" {
            let start = ProcessStartInfo("dotnet", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location)
            start.ArgumentList.Add "--session-tree-failure"
            use child = Process.Start start
            let stdout = child.StandardOutput.ReadToEndAsync()
            let stderr = child.StandardError.ReadToEndAsync()
            try
                Expect.isTrue (child.WaitForExit 20000) "MTP process terminates"
                let output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult()
                Expect.notEqual child.ExitCode 0 "MTP receives tree failure"
                Expect.stringContains output "setup degraded" "setup warning survives tree failure"
                Expect.stringContains output "tree unavailable" "tree error reaches MTP"
            finally
                if not child.HasExited then child.Kill(true)
        }

        for phase, configure in [ "setup", FrameworkDefinition.withCreateSession; "cleanup", FrameworkDefinition.withCloseSession ] do
            for failure, callback in [
                "fault", (fun (_: SessionContext) -> Task.FromException<SessionOutcome>(InvalidOperationException "session callback fault"))
                "cancellation", (fun _ -> Task.FromCanceled<SessionOutcome>(CancellationToken(true)))
            ] do
                testCase (phase + " " + failure + " does not become successful discovery") <| fun _ ->
                    let definition =
                        testFramework<unit> {
                            uid "session-callback-failure"
                            tests (fun () -> Group("root", None, [], [ Leaf("test", None, [], ()) ]))
                        }
                        |> configure callback
                    let run () = TestApplication.run [| "--list-tests"; "--progress"; "off" |] definition |> ignore
                    match failure with
                    | "fault" -> Expect.throwsT<InvalidOperationException> run "MTP propagates the callback fault"
                    | _ -> Expect.throwsT<TaskCanceledException> run "MTP propagates callback cancellation"
    ]
