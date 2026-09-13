module Partas.TestingPlatform.Trx.Tests.TrxReportTests

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
