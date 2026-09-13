module Partas.Testing.Sample.Program

open Partas.Testing

let suite =
    Test.list ("parser", [
        Test.list ("literals", [
            Test.case ("parses an int", fun () -> Expect.equal 2 1 "parses an int")

            Test.case ("rejects a malformed int", fun () ->
                failwith "expected a digit, got 'x'")

            Test.pending ("parses a float", fun () -> ())
        ])

        Test.caseAsync ("reads a file", async { return () })

        Test.case ("reports the source span", fun () -> ())
    ])

[<EntryPoint>]
let main argv = runTestsWithArgs argv (fun () -> suite)
