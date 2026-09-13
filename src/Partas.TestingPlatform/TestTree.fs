namespace Partas.TestingPlatform

open Microsoft.Testing.Platform.Extensions.Messages

type SourceLocation = { File: string; Line: int }

type TestTree<'T> =
    | Leaf of name: string * location: SourceLocation option * properties: IProperty list * payload: 'T
    | Group of name: string * location: SourceLocation option * properties: IProperty list * children: TestTree<'T> list

/// <summary>A node identity that a duplicate sibling name would have produced twice.</summary>
type Collision = { Uid: string; Name: string }

type ResolvedNode =
    { Uid: string
      Name: string
      Location: SourceLocation option
      Properties: IProperty list }

/// <summary>A <c>TestTree</c> carrying the UID of every node. A resolved tree has unique UIDs.</summary>
type ResolvedTestTree<'T> =
    | ResolvedLeaf of node: ResolvedNode * payload: 'T
    | ResolvedGroup of node: ResolvedNode * children: ResolvedTestTree<'T> list

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TestTree =

    /// <summary>The uid separator, fixed by <c>TreeNodeFilter.PathSeparator</c>.</summary>
    [<Literal>]
    let Separator = '/'

    /// <summary>Replaces <c>\</c> with <c>\\</c> and <c>/</c> with <c>\/</c>.</summary>
    let private escape (name: string) = name.Replace(@"\", @"\\").Replace("/", @"\/")

    /// <summary>The inverse of <c>escape</c>, for rendering a uid as a human-facing path.</summary>
    let unescape (uid: string) = uid.Replace(@"\/", "/").Replace(@"\\", @"\")

    /// <summary>Assigns a UID to every node, or reports every duplicate sibling name.</summary>
    let resolve (tree: TestTree<'T>) : Result<ResolvedTestTree<'T>, Collision list> =
        let collisions = ResizeArray()

        let nameOf =
            function
            | Leaf(name, _, _, _) -> name
            | Group(name, _, _, _) -> name

        let uid prefix name = prefix + string Separator + escape name

        let node prefix name location properties =
            { Uid = uid prefix name; Name = name; Location = location; Properties = properties }

        let rec go prefix tree =
            match tree with
            | Leaf(name, location, properties, payload) -> ResolvedLeaf(node prefix name location properties, payload)
            | Group(name, location, properties, children) ->
                let group = node prefix name location properties

                children
                |> List.countBy nameOf
                |> List.filter (snd >> (<) 1)
                |> List.iter (fun (duplicate, _) ->
                    collisions.Add { Uid = uid group.Uid duplicate; Name = duplicate })

                ResolvedGroup(group, children |> List.map (go group.Uid))

        let resolved = go "" tree

        if collisions.Count = 0 then Ok resolved else Error(List.ofSeq collisions)
