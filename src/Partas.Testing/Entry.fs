namespace Partas.Testing

open Partas.TestingPlatform

[<AutoOpen>]
module Entry =

    /// <summary>
    /// A suite as a framework definition, carrying Partas.Testing's own platform identity and
    /// execution walk. Pipe it through a companion package's registration
    /// (<c>Partas.TestingPlatform.Trx</c>'s <c>TrxReport.enable</c>), through
    /// <c>FrameworkDefinition.addCommandLineOptionsProvider</c>, or through any other
    /// definition-to-definition function, then into <c>TestApplication.run</c>. The suite is built
    /// once per process, when the platform creates the session.
    /// </summary>
    let testSuite (build: unit -> TestTree<TestBody>) : FrameworkDefinition<TestBody> =
        testFramework<TestBody> {
            uid "Partas.Testing"
            version "0.1.0"
            displayName "Partas.Testing"
            description "An Expecto-shaped test framework for Microsoft.Testing.Platform."
            tests build
            onRun Runner.run
        }

    /// <summary>
    /// Runs a suite as a test application, returning the process exit code. For a run that also
    /// registers a companion package, pipe <c>testSuite</c> into <c>TestApplication.run</c>
    /// instead.
    /// </summary>
    let runTestsWithArgs argv (build: unit -> TestTree<TestBody>) =
        testSuite build |> TestApplication.run argv
