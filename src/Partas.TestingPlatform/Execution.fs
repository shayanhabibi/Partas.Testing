namespace Partas.TestingPlatform

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Messages
open Microsoft.Testing.Platform.TestHost

/// <summary>A surviving leaf, its payload, and the uid of the group holding it.</summary>
type ExecutableLeaf<'T> =
    { Node: ResolvedNode
      Parent: string option
      Payload: 'T }

module Execution =

    /// <summary>
    /// The subtree of <paramref name="tree"/> holding the leaves admitted by
    /// <paramref name="admits"/> and the groups on their paths.
    /// </summary>
    let prune (admits: string -> PropertyBag -> bool) (tree: ResolvedTestTree<'T>) : ResolvedTestTree<'T> option =
        let rec go tree =
            match tree with
            | ResolvedLeaf(node, _) when admits node.Uid (PropertyBag(node.Properties)) -> Some tree
            | ResolvedLeaf _ -> None
            | ResolvedGroup(node, children) ->
                match List.choose go children with
                | [] -> None
                | survivors -> Some(ResolvedGroup(node, survivors))

        go tree

    /// <summary>The leaves of <paramref name="tree"/>, in pre-order.</summary>
    let leaves (tree: ResolvedTestTree<'T>) : ExecutableLeaf<'T> list =
        let rec go parent tree =
            match tree with
            | ResolvedLeaf(node, payload) -> [ { Node = node; Parent = parent; Payload = payload } ]
            | ResolvedGroup(node, children) -> List.collect (go (Some node.Uid)) children

        go None tree

    /// <summary>
    /// Publishes every group of <paramref name="tree"/> as discovered, a parent before its
    /// children.
    /// </summary>
    let publishGroups
        (bus: IMessageBus)
        (producer: IDataProducer)
        (session: SessionUid)
        (tree: ResolvedTestTree<'T>)
        : Task =
        let send node (parent: TestNodeUid) =
            let testNode = Nodes.toTestNode None node
            bus.PublishAsync(producer, TestNodeUpdateMessage(session, testNode, parent))

        let rec go parent tree =
            task {
                match tree with
                | ResolvedLeaf _ -> ()
                | ResolvedGroup(node, children) ->
                    do! send node parent

                    for child in children do
                        do! go (TestNodeUid node.Uid) child
            }

        go null tree
