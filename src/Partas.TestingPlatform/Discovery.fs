namespace Partas.TestingPlatform

open System.Threading.Tasks
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Messages
open Microsoft.Testing.Platform.TestHost

module Discovery =

    /// <summary>
    /// Publishes every node of <paramref name="tree"/>, each linked to its parent, in an order
    /// placing a parent before its children.
    /// </summary>
    let publish
        (bus: IMessageBus)
        (producer: IDataProducer)
        (session: SessionUid)
        (tree: ResolvedTestTree<'T>)
        : Task =
        let send state node (parent: TestNodeUid) =
            let testNode = Nodes.toTestNode state node
            bus.PublishAsync(producer, TestNodeUpdateMessage(session, testNode, parent))

        let discovered = Some(DiscoveredTestNodeStateProperty.CachedInstance :> IProperty)

        let rec go parent tree =
            task {
                match tree with
                | ResolvedLeaf(node, _) -> do! send discovered node parent
                | ResolvedGroup(node, children) ->
                    do! send None node parent

                    for child in children do
                        do! go (TestNodeUid node.Uid) child
            }

        go null tree
