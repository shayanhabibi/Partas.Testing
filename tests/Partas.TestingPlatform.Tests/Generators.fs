module Partas.TestingPlatform.Tests.Generators

open FsCheck
open Partas.TestingPlatform

/// <summary>Names spanning the plain case, the separator, the escape character, and both together.</summary>
let nameGen = Gen.elements [ "a"; "b"; "c"; "a/b"; @"a\b"; @"a\/b"; "" ]

let treeGen =
    let rec go size =
        gen {
            let! name = nameGen

            if size <= 1 then
                return Leaf(name, None, [], ())
            else
                let! count = Gen.choose (0, 3)
                let! children = Gen.listOfLength count (go (size / (count + 1)))
                return Group(name, None, [], children)
        }

    Gen.sized go

let arbTree = Arb.fromGen treeGen

/// <summary>Every uid of a resolved tree, in pre-order.</summary>
let rec uidsOf =
    function
    | ResolvedLeaf(node, ()) -> [ node.Uid ]
    | ResolvedGroup(node, children) -> node.Uid :: List.collect uidsOf children

/// <summary>Every child uid paired with the uid of its parent, in pre-order.</summary>
let parentLinksOf resolved =
    let rec go parent tree =
        match tree with
        | ResolvedLeaf(node, ()) -> [ node.Uid, parent ]
        | ResolvedGroup(node, children) ->
            (node.Uid, parent) :: List.collect (go (Some node.Uid)) children

    go None resolved
