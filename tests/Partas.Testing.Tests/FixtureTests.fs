module Partas.Testing.Tests.FixtureTests

open System.Threading
open Partas.Testing
open Expecto
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.Testing.Tests.Fakes

let private noop () = ()

let private idle () = async { return () }

[<Tests>]
let tests =
    testList "Test.listWith" [
        test "setup runs once before the descendant leaves and teardown once after them" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { log.Add "setup" }),
                    (fun () -> async { log.Add "teardown" })
                )
                    (fun _ -> [ Test.case "a" (fun () -> log.Add "a"); Test.case "b" (fun () -> log.Add "b") ])

            runSuite false tree |> ignore

            Expect.sequenceEqual (List.ofSeq log) [ "setup"; "a"; "b"; "teardown" ] "the order of the run"
        }

        test "a descendant reads the value setup produced" {
            let seen = ResizeArray<int>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun _ -> async { return () })
                )
                    (fun fixture -> [ Test.case "a" (fun () -> seen.Add fixture.Value) ])

            runSuite false tree |> ignore

            Expect.sequenceEqual (List.ofSeq seen) [ 7 ] "the value the body read"
        }

        test "teardown receives the value setup produced" {
            let torn = ResizeArray<int>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun value -> async { torn.Add value })
                )
                    (fun _ -> [ Test.case "a" noop ])

            runSuite false tree |> ignore

            Expect.sequenceEqual (List.ofSeq torn) [ 7 ] "the value teardown received"
        }

        test "a fixture group's leaves still report their own outcomes" {
            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun _ -> async { return () })
                )
                    (fun _ -> [ Test.case "a" noop ])

            let states = runSuite false tree

            Expect.isTrue (states.["/s/a"] :? PassedTestNodeStateProperty) "passed"
        }

        test "teardown runs after a leaf whose body raises" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { log.Add "setup" }),
                    (fun () -> async { log.Add "teardown" })
                )
                    (fun _ ->
                        [ Test.case "a" (fun () -> failwith "the body raised")
                          Test.case "b" (fun () -> log.Add "b") ])

            let states = runSuite false tree

            Expect.sequenceEqual (List.ofSeq log) [ "setup"; "b"; "teardown" ] "the order of the run"
            Expect.isTrue (states.["/s/a"] :? FailedTestNodeStateProperty) "the failing leaf"
        }

        test "teardown runs after the session is cancelled" {
            let log = ResizeArray<string>()
            use source = new CancellationTokenSource()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { log.Add "setup" }),
                    (fun () -> async { log.Add "teardown" })
                )
                    (fun _ -> [ Test.case "a" (fun () -> source.Cancel()) ])

            let states = runSuiteUnder source.Token false tree

            Expect.sequenceEqual (List.ofSeq log) [ "setup"; "teardown" ] "teardown released the fixture"
            Expect.isFalse (states.ContainsKey "/s") "the group carries no failure of its own"
        }

        test "setup cancelled on the session token reports the group skipped, not failed" {
            use source = new CancellationTokenSource()
            source.Cancel()

            let tree =
                Test.listWith ("s", (fun () -> async { do! Async.Sleep 1000 }), (fun _ -> idle ()))
                    (fun _ -> [ Test.case "a" noop ])

            let states = runSuiteUnder source.Token false tree

            Expect.isTrue (states.["/s"] :? SkippedTestNodeStateProperty) "skipped, not failed"
            Expect.isTrue ((states.["/s"]).Explanation.Contains "cancel") "the explanation names cancellation"
        }

        test "a pending leaf under a failed setup keeps its own reason" {
            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { raise (exn "the setup raised") }),
                    (fun _ -> idle ())
                )
                    (fun _ -> [ Test.pending "dormant" noop; Test.case "a" noop ])

            let states = runSuite false tree

            Expect.equal (states.["/s/dormant"]).Explanation "pending" "the pending leaf keeps its reason"
            Expect.stringContains (states.["/s/a"]).Explanation "/s" "the ordinary leaf names the group"
        }

        test "an unfocused leaf under a failed setup keeps its own reason" {
            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { raise (exn "the setup raised") }),
                    (fun _ -> idle ())
                )
                    (fun _ -> [ Test.focused "chosen" noop; Test.case "passed over" noop ])

            let states = runSuite false tree

            Expect.equal (states.["/s/passed over"]).Explanation "not focused" "the unfocused leaf keeps its reason"
            Expect.stringContains (states.["/s/chosen"]).Explanation "/s" "the focused leaf names the group"
        }

        test "a fixture group that succeeds reports no state of its own" {
            let tree =
                Test.listWith ("s", (fun () -> async { return 7 }), (fun _ -> idle ())) (fun _ -> [ Test.case "a" noop ])

            let states = runSuite false tree

            Expect.isFalse (states.ContainsKey "/s") "the group stays a group"
            Expect.equal (Map.count states) 1 "one terminal state, for the leaf"
        }

        test "a setup that raises reports the group failed, carrying the exception" {
            let boom = exn "the setup raised"

            let tree =
                Test.listWith ("s", (fun () -> async { raise boom }), (fun _ -> idle ())) (fun _ -> [ Test.case "a" noop ])

            let states = runSuite false tree

            match states.["/s"] with
            | :? FailedTestNodeStateProperty as failed -> Expect.equal failed.Exception boom "the exception"
            | other -> failtestf "expected failed, got %A" other
        }

        test "a setup that raises skips the descendant leaves, naming the group" {
            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { raise (exn "the setup raised") }),
                    (fun _ -> idle ())
                )
                    (fun _ -> [ Test.case "a" noop ])

            let states = runSuite false tree

            Expect.isTrue (states.["/s/a"] :? SkippedTestNodeStateProperty) "skipped"
            Expect.stringContains (states.["/s/a"]).Explanation "/s" "the explanation names the group"
        }

        test "a setup that raises runs neither the descendant bodies nor teardown" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { raise (exn "the setup raised") }),
                    (fun _ -> async { log.Add "teardown" })
                )
                    (fun _ -> [ Test.list "inner" [ Test.case "a" (fun () -> log.Add "a") ] ])

            let states = runSuite false tree

            Expect.isEmpty log "nothing under the group ran"
            Expect.isTrue (states.["/s/inner/a"] :? SkippedTestNodeStateProperty) "the nested leaf is skipped"
        }

        test "a teardown that raises reports the group failed and leaves a passing leaf passed" {
            let boom = exn "the teardown raised"

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun _ -> async { raise boom })
                )
                    (fun _ -> [ Test.case "a" noop ])

            let states = runSuite false tree

            Expect.isTrue (states.["/s/a"] :? PassedTestNodeStateProperty) "the leaf keeps its result"

            match states.["/s"] with
            | :? FailedTestNodeStateProperty as failed -> Expect.equal failed.Exception boom "the exception"
            | other -> failtestf "expected failed, got %A" other
        }

        test "a teardown that raises leaves a failing leaf carrying its own exception" {
            let fromBody = exn "the body raised"

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun _ -> async { raise (exn "the teardown raised") })
                )
                    (fun _ -> [ Test.case "a" (fun () -> raise fromBody) ])

            let states = runSuite false tree

            match states.["/s/a"] with
            | :? FailedTestNodeStateProperty as failed -> Expect.equal failed.Exception fromBody "the leaf's exception"
            | other -> failtestf "expected failed, got %A" other
        }

        test "a fixture group whose every leaf is pending stays unused" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { log.Add "setup" }),
                    (fun () -> async { log.Add "teardown" })
                )
                    (fun _ -> [ Test.pending "a" noop ])

            let states = runSuite false tree

            Expect.isEmpty log "the fixture stayed unused"
            Expect.isTrue (states.["/s/a"] :? SkippedTestNodeStateProperty) "the pending leaf is still skipped"
        }

        test "focus on a leaf outside a fixture group leaves that group unused" {
            let log = ResizeArray<string>()

            let tree =
                Test.list "root" [
                    Test.listWith (
                        "s",
                        (fun () -> async { log.Add "setup" }),
                        (fun () -> async { log.Add "teardown" })
                    )
                        (fun _ -> [ Test.case "a" noop ])
                    Test.list "other" [ Test.focused "b" noop ]
                ]

            runSuite false tree |> ignore

            Expect.isEmpty log "the fixture stayed unused"
        }

        test "focus on a leaf inside a fixture group still runs setup and teardown" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { log.Add "setup" }),
                    (fun () -> async { log.Add "teardown" })
                )
                    (fun _ -> [ Test.focused "a" (fun () -> log.Add "a"); Test.case "b" (fun () -> log.Add "b") ])

            runSuite false tree |> ignore

            Expect.sequenceEqual (List.ofSeq log) [ "setup"; "a"; "teardown" ] "the order of the run"
        }

        test "an inner fixture group nests inside its outer one" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "outer",
                    (fun () ->
                        async {
                            log.Add "outer setup"
                            return 1
                        }),
                    (fun _ -> async { log.Add "outer teardown" })
                )
                    (fun outer ->
                        [ Test.listWith (
                              "inner",
                              (fun () ->
                                  async {
                                      log.Add "inner setup"
                                      return outer.Value + 1
                                  }),
                              (fun _ -> async { log.Add "inner teardown" })
                          )
                              (fun inner -> [ Test.case "a" (fun () -> log.Add $"a reads {outer.Value} and {inner.Value}") ]) ])

            runSuite false tree |> ignore

            Expect.sequenceEqual
                (List.ofSeq log)
                [ "outer setup"; "inner setup"; "a reads 1 and 2"; "inner teardown"; "outer teardown" ]
                "the order of the run"
        }

        test "an inner fixture group under a failed outer setup leaves its own bracket unrun" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "outer",
                    (fun () -> async { raise (exn "the outer setup raised") }),
                    (fun _ -> async { log.Add "outer teardown" })
                )
                    (fun _ ->
                        [ Test.listWith (
                              "inner",
                              (fun () -> async { log.Add "inner setup" }),
                              (fun _ -> async { log.Add "inner teardown" })
                          )
                              (fun _ -> [ Test.case "a" (fun () -> log.Add "a") ]) ])

            let states = runSuite false tree

            Expect.isEmpty (List.ofSeq log) "neither the inner bracket nor the leaf under it ran"
            Expect.isTrue (states.["/outer"] :? FailedTestNodeStateProperty) "the outer group failed"
            Expect.isFalse (states.ContainsKey "/outer/inner") "the inner group carries no outcome of its own"
            Expect.isTrue (states.["/outer/inner/a"] :? SkippedTestNodeStateProperty) "the nested leaf is skipped"
            Expect.stringContains (states.["/outer/inner/a"]).Explanation "/outer" "the explanation names the outer group"
        }

        test "an inner setup that raises still tears the outer fixture down" {
            let log = ResizeArray<string>()

            let tree =
                Test.listWith (
                    "outer",
                    (fun () ->
                        async {
                            log.Add "outer setup"
                            return 1
                        }),
                    (fun _ -> async { log.Add "outer teardown" })
                )
                    (fun _ ->
                        [ Test.listWith (
                              "inner",
                              (fun () -> async { raise (exn "the inner setup raised") }),
                              (fun _ -> async { log.Add "inner teardown" })
                          )
                              (fun _ -> [ Test.case "a" (fun () -> log.Add "a") ]) ])

            let states = runSuite false tree

            Expect.sequenceEqual (List.ofSeq log) [ "outer setup"; "outer teardown" ] "the order of the run"
            Expect.isTrue (states.["/outer/inner"] :? FailedTestNodeStateProperty) "the inner group failed"
            Expect.isTrue (states.["/outer/inner/a"] :? SkippedTestNodeStateProperty) "the leaf under it is skipped"
        }

        test "the handle raises before setup completes and after teardown runs" {
            let handles = ResizeArray<Fixture<int>>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun _ -> idle ())
                )
                    (fun fixture ->
                        handles.Add fixture
                        [ Test.case "a" noop ])

            let fixture = handles.[0]

            Expect.throwsT<System.InvalidOperationException> (fun () -> fixture.Value |> ignore) "before setup"

            runSuite false tree |> ignore

            Expect.throwsT<System.InvalidOperationException> (fun () -> fixture.Value |> ignore) "after teardown"
        }

        test "the handle is released even when teardown raises" {
            let handles = ResizeArray<Fixture<int>>()

            let tree =
                Test.listWith (
                    "s",
                    (fun () -> async { return 7 }),
                    (fun _ -> async { raise (exn "the teardown raised") })
                )
                    (fun fixture ->
                        handles.Add fixture
                        [ Test.case "a" noop ])

            runSuite false tree |> ignore

            Expect.throwsT<System.InvalidOperationException> (fun () -> handles.[0].Value |> ignore) "after teardown"
        }
    ]
