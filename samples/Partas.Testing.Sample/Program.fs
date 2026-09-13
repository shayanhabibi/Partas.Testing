module Partas.Testing.Sample.Program

open Partas.Testing

let suite =
    Test.list "parser" [
        Test.list "literals" [
            Test.case "parses an int" (fun () -> Expect.equal 2 1 "parses an int")

            Test.case "rejects a malformed int" (fun () ->
                failwith "expected a digit, got 'x'")

            Test.pending "parses a float" (fun () -> ())
        ]

        Test.caseAsync "reads a file" (async { return () })

        Test.case "reports the source span" (fun () -> ())

        Test.parallelList "concurrent phases" [
            Test.caseAsync "lexes" (async { do! Async.Sleep 250 })
            Test.caseAsync "resolves" (async { do! Async.Sleep 250 })
        ]

        Test.sequentialList "ordered phases" [
            Test.caseAsync "reads" (async { do! Async.Sleep 250 })
            Test.caseAsync "writes" (async { do! Async.Sleep 250 })
        ]

        Test.listWith (
            "a session",
            (fun () -> async { return "1 2 3" }),
            (fun _ -> async { return () })
        ) (fun source -> [
            Test.case "reads its source" (fun () -> Expect.equal source.Value "1 2 3" "the source")
        ])

        Test.listWith (
            "a broken session",
            (fun () -> async { return failwith "the connection refused": string }),
            (fun _ -> async { return () })
        ) (fun _ -> [ Test.case "never runs" (fun () -> ()) ])
    ]

[<EntryPoint>]
let main argv = runTestsWithArgs argv (fun () -> suite)
