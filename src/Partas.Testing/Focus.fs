namespace Partas.Testing

open Microsoft.Testing.Platform.Extensions.Messages
open Partas.TestingPlatform

/// <summary>What the runner will do with a leaf.</summary>
[<RequireQualifiedAccess>]
type TestPlan =
    | Run
    | Skip of reason: string

module Focus =

    let stateOf (properties: IProperty list) =
        properties
        |> List.tryPick (function
            | :? FocusProperty as focus -> Some focus.State
            | _ -> None)
        |> Option.defaultValue TestFocus.Normal

    /// <summary>Whether the tree marks any node as focused.</summary>
    let rec anyFocused (tree: ResolvedTestTree<'T>) : bool =
        match tree with
        | ResolvedLeaf(node, _) -> stateOf node.Properties = TestFocus.Focused
        | ResolvedGroup(node, children) ->
            stateOf node.Properties = TestFocus.Focused || List.exists anyFocused children

    /// <summary>
    /// The plan for every leaf, by uid. A pending node never runs. When the tree marks anything
    /// as focused and focus is honoured, only focused leaves run. A marking on a group applies
    /// to its descendants.
    /// </summary>
    let plan (honourFocus: bool) (tree: ResolvedTestTree<'T>) : Map<string, TestPlan> =
        let narrowing = honourFocus && anyFocused tree

        let rec go pending focused tree plans =
            let carry (node: ResolvedNode) =
                let own = stateOf node.Properties
                pending || own = TestFocus.Pending, focused || own = TestFocus.Focused

            match tree with
            | ResolvedLeaf(node, _) ->
                let pending, focused = carry node

                let outcome =
                    if pending then TestPlan.Skip "pending"
                    elif narrowing && not focused then TestPlan.Skip "not focused"
                    else TestPlan.Run

                Map.add node.Uid outcome plans
            | ResolvedGroup(node, children) ->
                let pending, focused = carry node
                children |> List.fold (fun plans child -> go pending focused child plans) plans

        go false false tree Map.empty
