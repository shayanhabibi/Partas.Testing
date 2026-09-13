namespace Partas.Testing

/// <summary>Structured expectation data raised by a failed <c>Expect</c> call.</summary>
exception AssertionException of message: string * expected: string option * actual: string option with
    override this.Message =
        [ Some this.message
          this.expected |> Option.map (sprintf "expected: %s")
          this.actual |> Option.map (sprintf "actual: %s") ]
        |> List.choose id
        |> String.concat "\n"

/// <summary>Assertions that raise <see cref="T:Partas.Testing.AssertionException"/> on failure.</summary>
module Expect =

    let private fail message expected actual =
        raise (AssertionException(message, expected, actual))

    /// <summary>Fails unless <c>actual</c> and <c>expected</c> are structurally equal.</summary>
    let equal (actual: 'a) (expected: 'a) message =
        if actual <> expected then
            fail message (Some(sprintf "%A" expected)) (Some(sprintf "%A" actual))

    /// <summary>Fails when <c>actual</c> and <c>expected</c> are structurally equal.</summary>
    let notEqual (actual: 'a) (expected: 'a) message =
        if actual = expected then
            fail message (Some(sprintf "%A" expected)) (Some(sprintf "%A" actual))

    /// <summary>Fails unless <c>value</c> is <c>true</c>.</summary>
    let isTrue (value: bool) message =
        if not value then fail message (Some "true") (Some "false")

    /// <summary>Fails unless <c>value</c> is <c>false</c>.</summary>
    let isFalse (value: bool) message =
        if value then fail message (Some "false") (Some "true")

    /// <summary>Fails unless <c>value</c> is <c>Some</c>.</summary>
    let isSome (value: 'a option) message =
        match value with
        | Some _ -> ()
        | None -> fail message (Some "Some") (Some "None")

    /// <summary>Fails unless <c>value</c> is <c>None</c>.</summary>
    let isNone (value: 'a option) message =
        match value with
        | None -> ()
        | Some actual -> fail message (Some "None") (Some(sprintf "%A" actual))

    /// <summary>Fails unless <c>value</c> is <c>Ok</c>.</summary>
    let isOk (value: Result<'a, 'b>) message =
        match value with
        | Ok _ -> ()
        | Error error -> fail message (Some "Ok") (Some(sprintf "Error %A" error))

    /// <summary>Fails unless <c>value</c> is <c>Error</c>.</summary>
    let isError (value: Result<'a, 'b>) message =
        match value with
        | Error _ -> ()
        | Ok actual -> fail message (Some "Error") (Some(sprintf "Ok %A" actual))

    /// <summary>Fails unless <c>fn</c> raises.</summary>
    let throws (fn: unit -> unit) message =
        let raised =
            try
                fn ()
                false
            with _ ->
                true

        if not raised then fail message None None

    /// <summary>Always fails, carrying no expected or actual value.</summary>
    let failure message = fail message None None
