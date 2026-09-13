module Partas.TestingPlatform.Tests.ExecutionTests

open Expecto
open FsCheck
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.TestHost
open Partas.TestingPlatform
open Partas.TestingPlatform.Tests.Fakes
open Partas.TestingPlatform.Tests.Generators

let private session = SessionUid "session"

let private resolved tree =
    match TestTree.resolve tree with
    | Ok resolved -> resolved
    | Error collisions -> failtestf "the fixture does not resolve: %A" collisions

let private admitting uids : string -> PropertyBag -> bool =
    let admitted = Set.ofList uids
    fun uid _ -> Set.contains uid admitted

let private prunedUids admits tree =
    Execution.prune admits (resolved tree) |> Option.map uidsOf

let private fixture =
    Group("parser", None, [], [
        Group("literals", None, [], [ Leaf("int", None, [], ()); Leaf("string", None, [], ()) ])
        Leaf("top", None, [], ())
    ])

[<Tests>]
let tests =
    testList "Execution" [
        test "an admitted lone leaf survives" {
            Expect.equal (prunedUids (admitting [ "/a" ]) (Leaf("a", None, [], ()))) (Some [ "/a" ]) "the leaf"
        }

        test "an excluded lone leaf leaves nothing" {
            Expect.equal (prunedUids (admitting []) (Leaf("a", None, [], ()))) None "nothing survives"
        }

        test "a surviving leaf keeps the groups on its path" {
            Expect.equal
                (prunedUids (admitting [ "/parser/literals/int" ]) fixture)
                (Some [ "/parser"; "/parser/literals"; "/parser/literals/int" ])
                "the leaf and its ancestors"
        }

        test "a group whose leaves are all excluded is dropped" {
            Expect.equal
                (prunedUids (admitting [ "/parser/top" ]) fixture)
                (Some [ "/parser"; "/parser/top" ])
                "the empty group is gone"
        }

        test "a group admitted on its own uid does not rescue its leaves" {
            Expect.equal (prunedUids (admitting [ "/parser/literals" ]) fixture) None "groups are not tests"
        }

        test "leaves are listed in pre-order with their parent" {
            let leaves = Execution.leaves (resolved fixture)

            Expect.equal
                (leaves |> List.map (fun leaf -> leaf.Node.Uid, leaf.Parent))
                [ "/parser/literals/int", Some "/parser/literals"
                  "/parser/literals/string", Some "/parser/literals"
                  "/parser/top", Some "/parser" ]
                "uid and parent of each leaf"
        }

        test "publishing groups sends the groups and no leaves" {
            let bus = RecordingMessageBus()
            (Execution.publishGroups bus (StubProducer()) session (resolved fixture)).GetAwaiter().GetResult()

            Expect.equal
                (bus.Updates |> List.map (fun update -> update.TestNode.Uid.Value))
                [ "/parser"; "/parser/literals" ]
                "groups only, parent first"
        }

        test "a published group carries no state" {
            let bus = RecordingMessageBus()
            (Execution.publishGroups bus (StubProducer()) session (resolved fixture)).GetAwaiter().GetResult()

            for update in bus.Updates do
                Expect.isFalse
                    (update.TestNode.Properties.Any<TestNodeStateProperty>())
                    $"no state on {update.TestNode.Uid.Value}"
        }

        testProperty "pruning keeps exactly the admitted leaves"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Error _ -> true
            | Ok resolved ->
                let all = Execution.leaves resolved |> List.map (fun leaf -> leaf.Node.Uid)
                let admitted = all |> List.filter (fun uid -> uid.Length % 2 = 0)
                let admits = admitting admitted

                let survivors =
                    Execution.prune admits resolved
                    |> Option.map (Execution.leaves >> List.map (fun leaf -> leaf.Node.Uid))
                    |> Option.defaultValue []

                survivors = List.distinct admitted)

        testProperty "a pruned tree publishes no group without a surviving descendant"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Error _ -> true
            | Ok resolved ->
                let admitted =
                    Execution.leaves resolved
                    |> List.map (fun leaf -> leaf.Node.Uid)
                    |> List.filter (fun uid -> uid.Length % 2 = 0)

                match Execution.prune (admitting admitted) resolved with
                | None -> true
                | Some pruned ->
                    let rec everyGroupHasALeaf =
                        function
                        | ResolvedLeaf _ -> true
                        | ResolvedGroup(_, children) ->
                            not (List.isEmpty children) && List.forall everyGroupHasALeaf children

                    everyGroupHasALeaf pruned)
    ]
