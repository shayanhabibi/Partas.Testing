module Partas.TestingPlatform.Sample.Program

open System.Threading.Tasks
open Partas.TestingPlatform

/// <summary>A test body, which the binding never interprets.</summary>
type Body = unit -> TestOutcome

let private at (line: string) : SourceLocation option =
    Some { File = __SOURCE_FILE__; Line = int line }

let suite: TestTree<Body> =
    Group("parser", at __LINE__, [], [
        Group("literals", at __LINE__, [], [
            Leaf("parses an int", at __LINE__, [], fun () -> Passed)
            Leaf("rejects a malformed int", at __LINE__, [], fun () -> Failed(Some(exn "unexpected 'x'"), None))
            Leaf("parses a float", at __LINE__, [], fun () -> Skipped)
        ])
        Leaf("reports the source span", at __LINE__, [], fun () -> Passed)
    ])

/// <summary>The execution walk, which a framework above the binding owns.</summary>
let runTests (context: RunContext<Body>) : Task =
    task {
        for leaf in context.Leaves do
            let body _ = Task.FromResult(TestResult.create (leaf.Payload ()))
            let! _ = context.Reporter.Run(leaf, body, context.CancellationToken)
            ()
    }

[<EntryPoint>]
let main argv =
    testFramework<Body> {
        uid "Partas.TestingPlatform.Sample"
        version "0.1.0"
        displayName "Partas sample"
        tests (fun () -> suite)
        onRun runTests
    }
    |> TestApplication.run argv
