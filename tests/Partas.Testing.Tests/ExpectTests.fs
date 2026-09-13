module Partas.Testing.Tests.ExpectTests

open Partas.Testing
open Expecto

let private raised f : exn option =
    try
        f ()
        None
    with error -> Some error

[<Tests>]
let tests =
    testList "Expect" [
        test "equal passes when values are structurally equal" {
            Partas.Testing.Expect.equal 1 1 "same"
        }

        test "equal fails naming the intent, expected and actual values" {
            match raised (fun () -> Partas.Testing.Expect.equal 2 1 "parses an int") with
            | Some ex ->
                Expect.stringContains ex.Message "parses an int" "the intent"
                Expect.stringContains ex.Message "1" "the expected value"
                Expect.stringContains ex.Message "2" "the actual value"
            | None -> failtest "expected an AssertionException"
        }

        test "equal records expected and actual on the exception" {
            match raised (fun () -> Partas.Testing.Expect.equal 2 1 "parses an int") with
            | Some(AssertionException(_, expected, actual)) ->
                Expect.equal expected (Some "1") "expected"
                Expect.equal actual (Some "2") "actual"
            | other -> failtestf "expected an AssertionException, got %A" other
        }

        test "notEqual fails when values are structurally equal" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.notEqual 1 1 "differ")) "raised"
        }

        test "notEqual passes when values differ" {
            Partas.Testing.Expect.notEqual 1 2 "differ"
        }

        test "isTrue passes on true" {
            Partas.Testing.Expect.isTrue true "truthy"
        }

        test "isTrue fails on false" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.isTrue false "truthy")) "raised"
        }

        test "isFalse passes on false" {
            Partas.Testing.Expect.isFalse false "falsy"
        }

        test "isFalse fails on true" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.isFalse true "falsy")) "raised"
        }

        test "isSome passes on Some" {
            Partas.Testing.Expect.isSome (Some 1) "present"
        }

        test "isSome fails on None" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.isSome None "present")) "raised"
        }

        test "isNone passes on None" {
            Partas.Testing.Expect.isNone None "absent"
        }

        test "isNone fails on Some" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.isNone (Some 1) "absent")) "raised"
        }

        test "isOk passes on Ok" {
            Partas.Testing.Expect.isOk (Ok 1) "ok"
        }

        test "isOk fails on Error" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.isOk (Error "e") "ok")) "raised"
        }

        test "isError passes on Error" {
            Partas.Testing.Expect.isError (Error "e") "error"
        }

        test "isError fails on Ok" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.isError (Ok 1) "error")) "raised"
        }

        test "throws passes when the function raises" {
            Partas.Testing.Expect.throws (fun () -> failwith "boom") "raises"
        }

        test "throws fails when the function does not raise" {
            Expect.isSome (raised (fun () -> Partas.Testing.Expect.throws (fun () -> ()) "raises")) "raised"
        }

        test "failure always fails carrying no expected or actual value" {
            match raised (fun () -> Partas.Testing.Expect.failure "always fails") with
            | Some(AssertionException(_, expected, actual)) ->
                Expect.isNone expected "no expected value"
                Expect.isNone actual "no actual value"
            | other -> failtestf "expected an AssertionException, got %A" other
        }
    ]
