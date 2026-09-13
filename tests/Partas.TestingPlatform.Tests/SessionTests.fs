module Partas.TestingPlatform.Tests.SessionTests

open Expecto
open Partas.TestingPlatform

[<Tests>]
let tests =
    testList "Session.resolveTree" [
        test "a resolvable tree resolves" {
            let build () = Group("parser", None, [], [ Leaf("a", None, [], ()) ])

            match Session.resolveTree build with
            | Ok (ResolvedGroup(node, _)) -> Expect.equal node.Uid "/parser" "the resolved root"
            | other -> failtestf "expected a resolved group, got %A" other
        }

        test "a colliding tree reports the colliding uid" {
            let build () =
                Group("parser", None, [], [ Leaf("a", None, [], ()); Leaf("a", None, [], ()) ])

            match Session.resolveTree build with
            | Error message -> Expect.stringContains message "/parser/a" "names the collision"
            | Ok _ -> failtest "expected a collision"
        }

        test "a tree that fails to build reports the failure" {
            let build () : TestTree<unit> = failwith "no connection"

            match Session.resolveTree build with
            | Error message -> Expect.stringContains message "no connection" "names the failure"
            | Ok _ -> failtest "expected a build failure"
        }
    ]
