module Partas.TestingPlatform.Tests.TestTreeTests

open Expecto
open FsCheck
open Partas.TestingPlatform
open Partas.TestingPlatform.Tests.Generators

let private leaf name = Leaf(name, None, [], ())

let rec private leavesOf =
    function
    | Leaf(name, _, _, _) -> [ name ]
    | Group(_, _, _, children) -> List.collect leavesOf children

let rec private resolvedLeavesOf =
    function
    | ResolvedLeaf(node, ()) -> [ node.Name ]
    | ResolvedGroup(_, children) -> List.collect resolvedLeavesOf children

[<Tests>]
let tests =
    testList "TestTree.resolve" [
        test "a root leaf takes a slash-prefixed uid" {
            match TestTree.resolve (leaf "parses") with
            | Ok (ResolvedLeaf(node, ())) -> Expect.equal node.Uid "/parses" "uid of a root leaf"
            | other -> failtestf "expected a resolved leaf, got %A" other
        }

        test "a child takes its parent's uid as a prefix" {
            let tree = Group("parser", None, [], [ leaf "parses" ])
            match TestTree.resolve tree with
            | Ok (ResolvedGroup(group, [ ResolvedLeaf(child, ()) ])) ->
                Expect.equal group.Uid "/parser" "uid of the group"
                Expect.equal child.Uid "/parser/parses" "uid of the child"
            | other -> failtestf "expected a group with one leaf, got %A" other
        }

        test "duplicate sibling names are reported with the colliding uid" {
            let tree = Group("parser", None, [], [ leaf "parses"; leaf "parses" ])
            Expect.equal
                (TestTree.resolve tree)
                (Error [ { Uid = "/parser/parses"; Name = "parses" } ])
                "the collision"
        }

        test "the same name under different parents is not a collision" {
            let tree =
                Group("root", None, [], [
                    Group("a", None, [], [ leaf "parses" ])
                    Group("b", None, [], [ leaf "parses" ])
                ])
            Expect.isOk (TestTree.resolve tree) "distinct parents give distinct uids"
        }

        test "a separator in a name is escaped" {
            match TestTree.resolve (leaf "a/b") with
            | Ok (ResolvedLeaf(node, ())) -> Expect.equal node.Uid @"/a\/b" "escaped separator"
            | other -> failtestf "expected a resolved leaf, got %A" other
        }

        test "the escape character in a name is escaped" {
            match TestTree.resolve (leaf @"a\b") with
            | Ok (ResolvedLeaf(node, ())) -> Expect.equal node.Uid @"/a\\b" "escaped escape"
            | other -> failtestf "expected a resolved leaf, got %A" other
        }

        test "a name containing a separator cannot forge a nested uid" {
            let nested = TestTree.resolve (Group("a", None, [], [ leaf "b" ]))
            let forged = TestTree.resolve (leaf "a/b")

            let uidOf result =
                match result with
                | Ok (ResolvedGroup(_, [ ResolvedLeaf(node, ()) ])) -> node.Uid
                | Ok (ResolvedLeaf(node, ())) -> node.Uid
                | other -> failtestf "unexpected %A" other

            Expect.notEqual (uidOf nested) (uidOf forged) "a nested leaf and a slashed name"
        }

        testProperty "a resolved tree has unique uids"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Ok resolved ->
                let uids = uidsOf resolved
                List.length uids = List.length (List.distinct uids)
            | Error _ -> true)

        testProperty "every leaf resolves exactly once, in order"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Ok resolved -> resolvedLeavesOf resolved = leavesOf tree
            | Error _ -> true)

        testProperty "resolution fails only when a group holds duplicate sibling names"
        <| Prop.forAll arbTree (fun tree ->
            let rec hasDuplicateSiblings =
                function
                | Leaf _ -> false
                | Group(_, _, _, children) ->
                    let names =
                        children
                        |> List.map (function
                            | Leaf(name, _, _, _) -> name
                            | Group(name, _, _, _) -> name)

                    List.length names <> List.length (List.distinct names)
                    || List.exists hasDuplicateSiblings children

            Result.isError (TestTree.resolve tree) = hasDuplicateSiblings tree)
    ]
