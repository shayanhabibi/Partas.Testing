module Partas.TestingPlatform.Sample.Program

open System.CommandLine
open System.Threading.Tasks
open Partas.TestingPlatform
open Partas.TestingPlatform.CommandLine
open Partas.TestingPlatform.Trx

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

/// <summary>Declares a custom flag to MTP, so it survives MTP's unrecognised-option rejection.</summary>
let private myCustomFilterProvider () =
    let option = Option<string>("--my-custom-filter")
    option.Description <- "Demonstrates a framework-declared option surviving MTP's registration."

    let metadata: ProviderMetadata =
        { Uid = "Partas.TestingPlatform.Sample.CommandLine"
          Version = "0.1.0"
          DisplayName = "Sample options"
          Description = "Custom sample options" }

    CommandLineOptions.toProvider metadata [ option ]

/// <summary>The execution walk, which a framework above the binding owns.</summary>
let runTests (context: RunContext<Body>) : Task =
    task {
        match context.CommandLineOptions.TryGetOptionArgumentList("my-custom-filter") with
        | true, arguments -> printfn $"""my-custom-filter received: {String.concat ", " arguments}"""
        | false, _ -> printfn "my-custom-filter not set"

        for leaf in context.Leaves do
            let body _ =
                let result = TestResult.create (leaf.Payload ())
                Task.FromResult { result with ExtraProperties = [ TrxReport.fullyQualifiedTypeName leaf ] }

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
        commandLineOptions [ myCustomFilterProvider ]
    }
    |> TrxReport.enable
    |> TestApplication.run argv
