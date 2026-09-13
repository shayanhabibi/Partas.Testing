namespace Partas.Testing

open Partas.TestingPlatform

[<AutoOpen>]
module Entry =

    /// <summary>
    /// Runs a suite as a test application, returning the process exit code. The suite is built
    /// once per process, when the platform creates the session.
    /// </summary>
    let runTestsWithArgs argv (build: unit -> TestTree<TestBody>) =
        testFramework<TestBody> {
            uid "Partas.Testing"
            version "0.1.0"
            displayName "Partas.Testing"
            description "An Expecto-shaped test framework for Microsoft.Testing.Platform."
            tests build
            onRun Runner.run
        }
        |> TestApplication.run argv
