module Partas.TestingPlatform.Trx.Tests.TrxReportTests

open Expecto
open Microsoft.Testing.Extensions.TrxReport.Abstractions
open Microsoft.Testing.Platform.Builder
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

[<Tests>]
let builderExtensionTests =
    testList "BuilderExtension (internal, exercised only from a granted assembly)" [
        test "BuilderExtensions.set records the given actions" {
            let extension = BuilderExtension.create (fun _ -> ())
            let definition = FrameworkDefinition.empty<unit> |> BuilderExtensions.set [ extension ]

            Expect.equal definition.BuilderExtensions.Length 1 "one action recorded"
        }

        test "BuilderExtensions.run invokes every declared action" {
            let mutable calls = 0
            let extension = BuilderExtension.create (fun _ -> calls <- calls + 1)

            let definition =
                FrameworkDefinition.empty<unit> |> BuilderExtensions.set [ extension; extension ]

            definition |> BuilderExtensions.run Unchecked.defaultof<ITestApplicationBuilder>

            Expect.equal calls 2 "both registered actions ran"
        }

        test "BuilderExtensions.run passes the given builder through to each action" {
            let builder = Unchecked.defaultof<ITestApplicationBuilder>
            let mutable seen = ValueNone
            let extension = BuilderExtension.create (fun b -> seen <- ValueSome b)
            let definition = FrameworkDefinition.empty<unit> |> BuilderExtensions.set [ extension ]

            definition |> BuilderExtensions.run builder

            match seen with
            | ValueSome received -> Expect.isTrue (obj.ReferenceEquals(received, builder)) "same builder reference reaches the action"
            | ValueNone -> failtest "the action never ran"
        }
    ]

[<Tests>]
let enableTests =
    testList "TrxReport.enable" [
        test "adds the TRX capability" {
            let definition = TrxReport.enable FrameworkDefinition.empty<unit>
            Expect.equal definition.Capabilities.Length 1 "one capability added"
        }

        test "adds a builder extension" {
            let definition = TrxReport.enable FrameworkDefinition.empty<unit>
            Expect.equal definition.BuilderExtensions.Length 1 "one builder extension added"
        }

        test "does not discard previously declared capabilities or builder extensions" {
            let definition =
                { FrameworkDefinition.empty<unit> with
                    Capabilities = [ TrxReport.capability ] }
                |> TrxReport.enable

            Expect.equal definition.Capabilities.Length 2 "the prior capability is retained"
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
/// tests.
/// </summary>
[<Tests>]
let endToEndTests =
    testList "TrxReport.enable, run through TestApplication.run (out of process)" [
        testCase "registers AddTrxReportProvider, so --report-trx produces a report" (fun () ->
            let resultsDirectory =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "trx-e2e-" + System.Guid.NewGuid().ToString("N"))

            System.IO.Directory.CreateDirectory resultsDirectory |> ignore

            try
                let sampleDll =
                    System.IO.Path.Combine(
                        __SOURCE_DIRECTORY__,
                        "..",
                        "..",
                        "samples",
                        "Partas.TestingPlatform.Sample",
                        "bin",
                        "Debug",
                        "net10.0",
                        "Partas.TestingPlatform.Sample.dll"
                    )
                    |> System.IO.Path.GetFullPath

                Expect.isTrue
                    (System.IO.File.Exists sampleDll)
                    $"the sample must be built first (looked at {sampleDll})"

                let startInfo =
                    System.Diagnostics.ProcessStartInfo(
                        "dotnet",
                        [ sampleDll
                          "--report-trx"
                          "--report-trx-filename"
                          "e2e.trx"
                          "--results-directory"
                          resultsDirectory ]
                        |> String.concat " "
                    )

                startInfo.UseShellExecute <- false

                use proc = System.Diagnostics.Process.Start startInfo
                proc.WaitForExit 60_000 |> ignore

                let trxPath = System.IO.Path.Combine(resultsDirectory, "e2e.trx")

                Expect.isTrue
                    (System.IO.File.Exists trxPath)
                    "AddTrxReportProvider ran and wrote a report; if the BuilderExtensions.run call
                     site in TestApplication.run were deleted, --report-trx would go unrecognised
                     and no file would appear here"
            finally
                System.IO.Directory.Delete(resultsDirectory, true))
    ]
