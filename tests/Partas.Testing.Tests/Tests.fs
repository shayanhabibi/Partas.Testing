module Partas.Testing.Tests.DslTests

open Partas.Testing
open Partas.TestingPlatform
open Expecto

[<Tests>]
let tests =
    testList "Test.case" [
        test "a case records the line it was written on" {
            let case, written = Test.case ("a", fun () -> ()), int __LINE__

            match case with
            | Leaf(_, Some location, _, _) -> Expect.equal location.Line written "the line"
            | other -> failtestf "expected a located leaf, got %A" other
        }

        test "a case records the file it was written in" {
            let case = Test.case ("a", fun () -> ())

            match case with
            | Leaf(_, Some location, _, _) ->
                Expect.stringEnds location.File "Tests.fs" "the file"
            | other -> failtestf "expected a located leaf, got %A" other
        }

        test "a case takes the given name" {
            match Test.case ("parses an int", fun () -> ()) with
            | Leaf(name, _, _, _) -> Expect.equal name "parses an int" "the name"
            | other -> failtestf "expected a leaf, got %A" other
        }

        test "a case runs the given body" {
            let mutable ran = false

            match Test.case ("a", fun () -> ran <- true) with
            | Leaf(_, _, _, body) -> Async.RunSynchronously body
            | other -> failtestf "expected a leaf, got %A" other

            Expect.isTrue ran "the body ran"
        }

        test "an async case runs the given workflow" {
            let mutable ran = false
            let body = async { ran <- true }

            match Test.caseAsync ("a", body) with
            | Leaf(_, _, _, body) -> Async.RunSynchronously body
            | other -> failtestf "expected a leaf, got %A" other

            Expect.isTrue ran "the workflow ran"
        }

        test "a list holds its children and records where it was written" {
            let group, written =
                Test.list ("parser", [ Test.case ("a", fun () -> ()) ]), int __LINE__

            match group with
            | Group(name, Some location, _, children) ->
                Expect.equal name "parser" "the name"
                Expect.equal location.Line written "the line"
                Expect.hasLength children 1 "one child"
            | other -> failtestf "expected a located group, got %A" other
        }

        test "a plain case carries no focus" {
            match Test.case ("a", fun () -> ()) with
            | Leaf(_, _, properties, _) ->
                Expect.isEmpty (properties |> List.filter (fun p -> p :? FocusProperty)) "no focus"
            | other -> failtestf "expected a leaf, got %A" other
        }

        test "a focused case carries the focused state" {
            match Test.focused ("a", fun () -> ()) with
            | Leaf(_, _, properties, _) ->
                let focus = properties |> List.pick (function :? FocusProperty as f -> Some f | _ -> None)
                Expect.equal focus.State TestFocus.Focused "focused"
            | other -> failtestf "expected a leaf, got %A" other
        }

        test "a pending case carries the pending state" {
            match Test.pending ("a", fun () -> ()) with
            | Leaf(_, _, properties, _) ->
                let focus = properties |> List.pick (function :? FocusProperty as f -> Some f | _ -> None)
                Expect.equal focus.State TestFocus.Pending "pending"
            | other -> failtestf "expected a leaf, got %A" other
        }

        test "a focused list carries the focused state" {
            match Test.focusedList ("parser", []) with
            | Group(_, _, properties, _) ->
                let focus = properties |> List.pick (function :? FocusProperty as f -> Some f | _ -> None)
                Expect.equal focus.State TestFocus.Focused "focused"
            | other -> failtestf "expected a group, got %A" other
        }
    ]
