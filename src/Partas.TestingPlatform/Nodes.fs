namespace Partas.TestingPlatform

open Microsoft.Testing.Platform.Extensions.Messages

module internal Nodes =

    /// <summary>
    /// A platform node carrying <paramref name="state"/>, the node's file location, and the
    /// node's own properties.
    /// </summary>
    let toTestNode (state: IProperty) (node: ResolvedNode) =
        let properties = PropertyBag(state)

        node.Location
        |> Option.iter (fun location ->
            let position = LinePosition(location.Line, 0)
            properties.Add(TestFileLocationProperty(location.File, LinePositionSpan(position, position))))

        node.Properties |> List.iter properties.Add

        TestNode(Uid = TestNodeUid node.Uid, DisplayName = node.Name, Properties = properties)
