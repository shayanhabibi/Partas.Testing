module Partas.TestingPlatform.Tests.DiscoveryTests

open Expecto
open FsCheck
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.TestHost
open Partas.TestingPlatform
open Partas.TestingPlatform.Tests.Fakes
open Partas.TestingPlatform.Tests.Generators

let private session = SessionUid "session"

let private discoverResolved resolved =
    let bus = RecordingMessageBus()
    (Discovery.publish bus (StubProducer()) session resolved).GetAwaiter().GetResult()
    bus.Updates

let private resolved tree =
    match TestTree.resolve tree with
    | Ok resolved -> resolved
    | Error collisions -> failtestf "the fixture does not resolve: %A" collisions

let private discover tree = discoverResolved (resolved tree)

let private uidOf (update: TestNodeUpdateMessage) = update.TestNode.Uid.Value

let private parentOf (update: TestNodeUpdateMessage) =
    update.ParentTestNodeUid |> Option.ofObj |> Option.map (fun uid -> uid.Value)

[<Tests>]
let tests =
    testList "Discovery.publish" [
        test "a lone leaf is published without a parent" {
            let updates = discover (Leaf("parses", None, [], ()))

            Expect.hasLength updates 1 "one update"
            Expect.equal updates.[0].TestNode.Uid.Value "/parses" "uid"
            Expect.equal updates.[0].TestNode.DisplayName "parses" "display name"
            Expect.isNull updates.[0].ParentTestNodeUid "a root node has no parent"
        }

        test "a parent precedes its children and is named as their parent" {
            let updates =
                discover (Group("parser", None, [], [ Leaf("a", None, [], ()); Leaf("b", None, [], ()) ]))

            let uids = updates |> List.map (fun update -> update.TestNode.Uid.Value)
            Expect.equal uids [ "/parser"; "/parser/a"; "/parser/b" ] "parent first, then children in order"

            let parents =
                updates
                |> List.map (fun update ->
                    update.ParentTestNodeUid |> Option.ofObj |> Option.map (fun uid -> uid.Value))

            Expect.equal parents [ None; Some "/parser"; Some "/parser" ] "parent links"
        }

        test "every published node carries the discovered state" {
            let updates = discover (Group("parser", None, [], [ Leaf("a", None, [], ()) ]))

            for update in updates do
                Expect.isTrue
                    (update.TestNode.Properties.Any<DiscoveredTestNodeStateProperty>())
                    $"discovered state on {update.TestNode.Uid.Value}"
        }

        test "a captured location is published as a file location" {
            let updates = discover (Leaf("a", Some { File = "Tests.fs"; Line = 42 }, [], ()))
            let location = updates.[0].TestNode.Properties.SingleOrDefault<TestFileLocationProperty>()

            Expect.isNotNull location "a file location"
            Expect.equal location.FilePath "Tests.fs" "file path"
            Expect.equal location.LineSpan.Start.Line 42 "line"
        }

        test "a node without a location publishes no file location" {
            let updates = discover (Leaf("a", None, [], ()))

            Expect.isFalse
                (updates.[0].TestNode.Properties.Any<TestFileLocationProperty>())
                "no file location"
        }

        test "node properties are published alongside the state" {
            let metadata = TestMetadataProperty("category", "fast")
            let updates = discover (Leaf("a", None, [ metadata ], ()))
            let published = updates.[0].TestNode.Properties.SingleOrDefault<TestMetadataProperty>()

            Expect.equal published.Key "category" "key"
            Expect.equal published.Value "fast" "value"
        }

        testProperty "publication is the resolved tree in pre-order"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Error _ -> true
            | Ok resolved -> (discoverResolved resolved |> List.map uidOf) = uidsOf resolved)

        testProperty "every parent uid was published earlier in the session"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Error _ -> true
            | Ok resolved ->
                (Some Set.empty, discoverResolved resolved)
                ||> List.fold (fun seen update ->
                    match seen with
                    | None -> None
                    | Some seen ->
                        match parentOf update with
                        | Some parent when not (Set.contains parent seen) -> None
                        | _ -> Some(Set.add (uidOf update) seen))
                |> Option.isSome)

        testProperty "parent links reproduce the resolved tree"
        <| Prop.forAll arbTree (fun tree ->
            match TestTree.resolve tree with
            | Error _ -> true
            | Ok resolved ->
                let links = discoverResolved resolved |> List.map (fun update -> uidOf update, parentOf update)
                links = parentLinksOf resolved)
    ]
