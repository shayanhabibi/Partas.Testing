module Partas.TestingPlatform.Trx.Tests.TrxReportTests

open System
open Expecto
open Microsoft.Testing.Extensions.TrxReport.Abstractions
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.TestingPlatform
open Partas.TestingPlatform.Trx

[<Tests>]
let capabilityTests =
    testList "TrxReportCapability" [
        test "is supported" {
            let capability = TrxReportCapability() :> ITrxReportCapability
            Expect.isTrue capability.IsSupported "always supported"
        }

        test "enabling does not raise" { (TrxReportCapability() :> ITrxReportCapability).Enable() }

        test "the capability factory produces an ITrxReportCapability" {
            let produced = TrxReport.capability ()
            Expect.isTrue (produced :? ITrxReportCapability) "instance implements ITrxReportCapability"
        }
    ]

let private leaf parent uid name : ExecutableLeaf<unit> =
    { Node = { Uid = uid; Name = name; Location = None; Properties = [] }
      Parent = parent
      Payload = () }

[<Tests>]
let fullyQualifiedTypeNameTests =
    testList "TrxReport.fullyQualifiedTypeName" [
        test "a root-level leaf carries an empty type name" {
            let property =
                TrxReport.fullyQualifiedTypeName (leaf None "/parses" "parses")
                :?> TrxFullyQualifiedTypeNameProperty

            Expect.equal property.FullyQualifiedTypeName "" "no containing group"
        }

        test "a nested leaf's type name is the containing group's path, without the leaf's own name" {
            let property =
                TrxReport.fullyQualifiedTypeName (leaf (Some "/parser/literals") "/parser/literals/parses int" "parses int")
                :?> TrxFullyQualifiedTypeNameProperty

            Expect.equal property.FullyQualifiedTypeName "parser/literals" "leading separator stripped, leaf name excluded"
        }

        test "a leaf's own name containing a separator does not leak into the type name" {
            let property =
                TrxReport.fullyQualifiedTypeName (leaf (Some "/grp") @"/grp/a\/b" "a/b")
                :?> TrxFullyQualifiedTypeNameProperty

            Expect.equal property.FullyQualifiedTypeName "grp" "escaped leaf name plays no part in grouping"
        }

        test "groups results by their containing group, not by leaf identity" {
            let a = TrxReport.fullyQualifiedTypeName (leaf (Some "/parser/literals") "/parser/literals/parses an int" "parses an int")
            let b = TrxReport.fullyQualifiedTypeName (leaf (Some "/parser/literals") "/parser/literals/parses a float" "parses a float")

            let nameOf (p: IProperty) = (p :?> TrxFullyQualifiedTypeNameProperty).FullyQualifiedTypeName
            Expect.equal (nameOf a) (nameOf b) "two leaves under the same group share a type name"
            Expect.equal (nameOf a) "parser/literals" "pinned to the expected group path"
        }

        test "a group's own name containing a separator is unescaped, not left as an escaped uid" {
            let property =
                TrxReport.fullyQualifiedTypeName (leaf (Some @"/a\/b") @"/a\/b/c" "c")
                :?> TrxFullyQualifiedTypeNameProperty

            Expect.equal property.FullyQualifiedTypeName "a/b" "the group's own separator is unescaped, not left as \\/"
        }
    ]

// BuilderExtension's case constructor and FrameworkDefinition's representation are internal to
// Partas.TestingPlatform, granted only to Partas.TestingPlatform.Trx — not to this test
// project (see AssemblyInfo.fs in both projects). The BuilderExtensions.set/.run mechanism
// TrxReport.enable relies on is exercised end to end by endToEndTests below, through the real
// production wiring, rather than unit-tested directly here.

[<Tests>]
let enableTests =
    testList "TrxReport.enable" [
        test "adds the TRX capability" {
            let definition = TrxReport.enable FrameworkDefinition.empty<unit>
            Expect.equal (definition |> FrameworkDefinition.capabilities).Length 1 "one capability added"
        }

        test "adds a builder extension" {
            let definition = TrxReport.enable FrameworkDefinition.empty<unit>
            Expect.equal (definition |> FrameworkDefinition.builderExtensions).Length 1 "one builder extension added"
        }

        test "does not discard previously declared capabilities or builder extensions" {
            let definition =
                testFramework<unit> { capabilities [ TrxReport.capability ] }
                |> TrxReport.enable

            Expect.equal (definition |> FrameworkDefinition.capabilities).Length 2 "the prior capability is retained"
        }
    ]

/// <summary>
/// An out-of-process run of the sample application (<c>testFramework { ... } |>
/// TrxReport.enable |> TestApplication.run argv</c>), the only path that exercises the wiring
/// call site (<c>definition |> BuilderExtensions.run builder</c> in <c>TestApplication.run</c>)
/// that a unit test of <c>BuilderExtensions.run</c> alone cannot cover: deleting that call site
/// does not fail any unit test, since the function it calls still works in isolation, but it
/// does stop <c>--report-trx</c> from being a recognised option at all. Runs out-of-process,
/// rather than calling <c>TestApplication.run</c> directly from inside this test, because that
/// call builds and runs a second, nested MTP session inside the session already running these
/// tests. The sample is a <c>ProjectReference</c> of this test project (build-only, never
/// opened), so its dll lands next to this project's own output at any configuration.
/// </summary>
[<Tests>]
let endToEndTests =
    testList "TrxReport.enable, run through TestApplication.run (out of process)" [
        testCase "registers AddTrxReportProvider, so --report-trx produces a correct report" (fun () ->
            let resultsDirectory =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "trx-e2e-" + System.Guid.NewGuid().ToString("N"))

            System.IO.Directory.CreateDirectory resultsDirectory |> ignore

            try
                let sampleDll = System.IO.Path.Combine(AppContext.BaseDirectory, "Partas.TestingPlatform.Sample.dll")

                Expect.isTrue
                    (System.IO.File.Exists sampleDll)
                    $"expected the sample's dll copied alongside this project's own output by its
                      ProjectReference (looked at {sampleDll})"

                let startInfo = System.Diagnostics.ProcessStartInfo("dotnet")
                startInfo.ArgumentList.Add sampleDll
                startInfo.ArgumentList.Add "--report-trx"
                startInfo.ArgumentList.Add "--report-trx-filename"
                startInfo.ArgumentList.Add "e2e.trx"
                startInfo.ArgumentList.Add "--results-directory"
                startInfo.ArgumentList.Add resultsDirectory
                startInfo.UseShellExecute <- false
                startInfo.RedirectStandardOutput <- true
                startInfo.RedirectStandardError <- true

                use proc = System.Diagnostics.Process.Start startInfo
                // Read concurrently with the process running: WaitForExit before draining a
                // redirected stream can deadlock once its buffer fills.
                let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                let stderrTask = proc.StandardError.ReadToEndAsync()
                let exited = proc.WaitForExit 60_000
                let diagnostics () = $"stdout:\n{stdoutTask.Result}\nstderr:\n{stderrTask.Result}"

                Expect.isTrue exited $"the sample did not exit within 60s\n{diagnostics ()}"

                // The sample declares one deliberately failing test (see Program.fs); MTP's
                // documented exit code for a run that completed with a failure is 2.
                Expect.equal proc.ExitCode 2 $"unexpected exit code\n{diagnostics ()}"

                let trxPath = System.IO.Path.Combine(resultsDirectory, "e2e.trx")

                Expect.isTrue
                    (System.IO.File.Exists trxPath)
                    $"AddTrxReportProvider ran and wrote a report; if the BuilderExtensions.run
                      call site in TestApplication.run were deleted, --report-trx would go
                      unrecognised and no file would appear here\n{diagnostics ()}"

                let document = System.Xml.Linq.XDocument.Load trxPath
                let ns = document.Root.Name.Namespace

                let unitTests = document.Descendants(ns + "UnitTest") |> List.ofSeq

                Expect.equal unitTests.Length 4 "the sample's four declared leaves are all reported"

                let classNames =
                    document.Descendants(ns + "TestMethod")
                    |> Seq.map (fun testMethod -> testMethod.Attribute(System.Xml.Linq.XName.Get "className").Value)
                    |> Set.ofSeq

                Expect.equal
                    classNames
                    (set [ "parser/literals"; "parser" ])
                    "leaves are grouped under their containing group's path, not left ungrouped or per-leaf"
            finally
                System.IO.Directory.Delete(resultsDirectory, true))
    ]
