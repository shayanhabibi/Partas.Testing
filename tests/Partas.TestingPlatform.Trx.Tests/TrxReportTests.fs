module Partas.TestingPlatform.Trx.Tests.TrxReportTests

open Expecto
open Microsoft.Testing.Extensions.TrxReport.Abstractions
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

[<Tests>]
let fullyQualifiedTypeNameTests =
    testList "TrxReport.fullyQualifiedTypeName" [
        test "a root leaf's uid loses its leading separator" {
            let node =
                { Uid = "/parses"
                  Name = "parses"
                  Location = None
                  Properties = [] }

            let property = TrxReport.fullyQualifiedTypeName node :?> TrxFullyQualifiedTypeNameProperty
            Expect.equal property.FullyQualifiedTypeName "parses" "leading separator stripped"
        }

        test "a nested leaf's uid keeps its interior separators" {
            let node =
                { Uid = "/parser/literals/parses int"
                  Name = "parses int"
                  Location = None
                  Properties = [] }

            let property = TrxReport.fullyQualifiedTypeName node :?> TrxFullyQualifiedTypeNameProperty
            Expect.equal property.FullyQualifiedTypeName "parser/literals/parses int" "interior structure preserved"
        }
    ]
