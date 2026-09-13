module Partas.Testing.Tests.FocusTests

open Expecto
open Partas.Testing
open Partas.TestingPlatform

let private noop () = ()

let private resolved tree =
    match TestTree.resolve tree with
    | Ok resolved -> resolved
    | Error collisions -> failtestf "the fixture does not resolve: %A" collisions

let private plan honourFocus tree = Focus.plan honourFocus (resolved tree)

[<Tests>]
let tests =
    testList "Focus.plan" [
        test "without any focus every leaf runs" {
            let plan =
                plan true (Test.list ("s", [ Test.case ("a", noop); Test.case ("b", noop) ]))

            Expect.equal plan.["/s/a"] TestPlan.Run "a"
            Expect.equal plan.["/s/b"] TestPlan.Run "b"
        }

        test "a focused leaf runs and its siblings are skipped" {
            let plan =
                plan true (Test.list ("s", [ Test.focused ("a", noop); Test.case ("b", noop) ]))

            Expect.equal plan.["/s/a"] TestPlan.Run "the focused leaf"
            Expect.equal plan.["/s/b"] (TestPlan.Skip "not focused") "the unfocused sibling"
        }

        test "focus on a group reaches its descendants" {
            let tree =
                Test.list ("s", [
                    Test.focusedList ("inner", [ Test.case ("a", noop) ])
                    Test.case ("b", noop)
                ])

            let plan = plan true tree

            Expect.equal plan.["/s/inner/a"] TestPlan.Run "inside the focused group"
            Expect.equal plan.["/s/b"] (TestPlan.Skip "not focused") "outside it"
        }

        test "a pending leaf is skipped even without focus" {
            let plan = plan true (Test.list ("s", [ Test.pending ("a", noop) ]))
            Expect.equal plan.["/s/a"] (TestPlan.Skip "pending") "pending"
        }

        test "pending on a group outranks focus on a descendant" {
            let tree =
                Test.list ("s", [ Test.pendingList ("inner", [ Test.focused ("a", noop) ]) ])

            Expect.equal (plan true tree).["/s/inner/a"] (TestPlan.Skip "pending") "pending wins"
        }

        test "a platform filter overrides focus" {
            let tree = Test.list ("s", [ Test.focused ("a", noop); Test.case ("b", noop) ])

            Expect.equal (plan false tree).["/s/b"] TestPlan.Run "the unfocused leaf still runs"
        }

        test "a platform filter does not revive a pending leaf" {
            let tree = Test.list ("s", [ Test.pending ("a", noop) ])

            Expect.equal (plan false tree).["/s/a"] (TestPlan.Skip "pending") "still pending"
        }

        test "focus is detected anywhere in the tree" {
            let unfocused = Test.list ("s", [ Test.case ("a", noop) ])
            let focused = Test.list ("s", [ Test.list ("i", [ Test.focused ("a", noop) ]) ])

            Expect.isFalse (Focus.anyFocused (resolved unfocused)) "no focus"
            Expect.isTrue (Focus.anyFocused (resolved focused)) "nested focus"
        }
    ]
