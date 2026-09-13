module Partas.Testing.Tests.EndToEndTests

open System
open System.Diagnostics
open System.IO
open System.Xml.Linq
open Expecto

/// <summary>
/// The exit code and the console output of the sample application, run out of process with the
/// given arguments. Out of process, rather than calling <c>TestApplication.run</c> from inside a
/// test, because that call builds a second MTP session inside the session already running these
/// tests. The sample is a <c>ProjectReference</c> of this test project (build-only, never
/// opened), so its dll lands next to this project's own output at any configuration.
/// </summary>
let private runSample (arguments: string list) =
    let sampleDll = Path.Combine(AppContext.BaseDirectory, "Partas.Testing.Sample.dll")

    Expect.isTrue
        (File.Exists sampleDll)
        $"expected the sample's dll copied alongside this project's own output by its
          ProjectReference (looked at {sampleDll})"

    let startInfo = ProcessStartInfo "dotnet"
    startInfo.ArgumentList.Add sampleDll
    arguments |> List.iter startInfo.ArgumentList.Add
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true

    use proc = Process.Start startInfo
    // Read concurrently with the process running: WaitForExit before draining a redirected
    // stream can deadlock once its buffer fills.
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()
    let exited = proc.WaitForExit 120_000
    let diagnostics = $"stdout:\n{stdout.Result}\nstderr:\n{stderr.Result}"

    Expect.isTrue exited $"the sample did not exit within 120s\n{diagnostics}"

    proc.ExitCode, diagnostics

/// <summary>
/// Exercises what <c>testSuite</c> exists for: a framework user pipes the definition through a
/// companion package's registration before <c>TestApplication.run</c>. The sample
/// (<c>samples/Partas.Testing.Sample/Program.fs</c>) is the only place the whole path runs, and
/// it is the path a unit test of <c>TrxReport.enable</c> alone cannot cover.
/// </summary>
[<Tests>]
let trxTests =
    testList "the sample, piped through TrxReport.enable (out of process)" [
        testCase "--report-trx writes a report of the suite, grouped by containing group" (fun () ->
            let resultsDirectory =
                Path.Combine(Path.GetTempPath(), "partas-testing-e2e-" + Guid.NewGuid().ToString "N")

            Directory.CreateDirectory resultsDirectory |> ignore

            try
                let exitCode, diagnostics =
                    runSample [
                        "--report-trx"
                        "--report-trx-filename"
                        "e2e.trx"
                        "--results-directory"
                        resultsDirectory
                    ]

                // The sample declares deliberately failing tests; MTP's exit code for a run that
                // completed with a failure is 2.
                Expect.equal exitCode 2 $"unexpected exit code\n{diagnostics}"

                let trxPath = Path.Combine(resultsDirectory, "e2e.trx")

                Expect.isTrue
                    (File.Exists trxPath)
                    $"--report-trx is a recognised option and its writer ran\n{diagnostics}"

                let document = XDocument.Load trxPath
                let ns = document.Root.Name.Namespace

                let names =
                    document.Descendants(ns + "UnitTest")
                    |> Seq.map (fun test -> test.Attribute(XName.Get "name").Value)
                    |> Set.ofSeq

                Expect.contains names "parses an int" "a leaf of the suite is reported"

                let classNames =
                    document.Descendants(ns + "TestMethod")
                    |> Seq.map (fun testMethod -> testMethod.Attribute(XName.Get "className").Value)
                    |> Set.ofSeq

                Expect.contains
                    classNames
                    "parser/literals"
                    "TrxReport.enable put the grouping name on results the framework's own walk never touches"

                Expect.isFalse (classNames.Contains "") "every result carries a grouping name"
            finally
                Directory.Delete(resultsDirectory, true))
    ]

/// <summary>
/// Covers the <c>AddMaximumFailedTestsService</c> registration: without it MTP rejects
/// <c>--maximum-failed-tests</c> as unrecognised and exits 5, and no unit test of the framework
/// sees the difference.
/// </summary>
[<Tests>]
let maximumFailedTestsTests =
    testList "the sample under --maximum-failed-tests (out of process)" [
        testCase "--maximum-failed-tests 1 is recognised and stops the run" (fun () ->
            let exitCode, diagnostics = runSample [ "--maximum-failed-tests"; "1" ]

            // 13 is MTP's TestExecutionStoppedForMaxFailedTests. An unregistered option exits 5,
            // and a run that merely completed with failures exits 2.
            Expect.equal exitCode 13 $"unexpected exit code\n{diagnostics}")
    ]
