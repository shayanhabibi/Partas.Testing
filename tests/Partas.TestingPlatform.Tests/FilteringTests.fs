module Partas.TestingPlatform.Tests.FilteringTests

#nowarn "57"

open Expecto
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Requests
open Partas.TestingPlatform

type private UnrecognisedFilter() =
    interface ITestExecutionFilter

let private admits filter uid =
    Filtering.toPredicate filter uid (PropertyBag())

let private uidList uids =
    TestNodeUidListFilter(uids |> List.map TestNodeUid |> Array.ofList)

[<Tests>]
let tests =
    testList "Filtering.toPredicate" [
        test "a nop filter admits every node" {
            Expect.isTrue (admits (NopFilter()) "/anything") "any uid"
        }

        test "a uid list admits only the listed uids" {
            let filter = uidList [ "/parser/a"; "/parser/b" ]

            Expect.isTrue (admits filter "/parser/a") "a listed uid"
            Expect.isFalse (admits filter "/parser/c") "an unlisted uid"
        }

        test "a composite admits only what every member admits" {
            let filter =
                CompositeTestExecutionFilter(
                    TestExecutionFilterOperator.And,
                    uidList [ "/a"; "/b" ],
                    uidList [ "/b"; "/c" ]
                )

            Expect.isFalse (admits filter "/a") "admitted by one member"
            Expect.isTrue (admits filter "/b") "admitted by both"
            Expect.isFalse (admits filter "/c") "admitted by the other member"
        }

        test "a composite is applied recursively" {
            // A composite requires at least two children.
            let inner =
                CompositeTestExecutionFilter(
                    TestExecutionFilterOperator.And,
                    uidList [ "/a"; "/b" ],
                    uidList [ "/a"; "/b"; "/c" ]
                )

            let filter =
                CompositeTestExecutionFilter(TestExecutionFilterOperator.And, inner, uidList [ "/b" ])

            Expect.isFalse (admits filter "/a") "excluded by the outer member"
            Expect.isTrue (admits filter "/b") "admitted throughout"
        }

        test "an unrecognised filter admits every node" {
            Expect.isTrue (admits (UnrecognisedFilter()) "/anything") "any uid"
        }
    ]
