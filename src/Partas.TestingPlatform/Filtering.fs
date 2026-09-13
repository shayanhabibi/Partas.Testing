// Filters are [Experimental("TPEXP")], which F# surfaces as FS0057. The binding absorbs the
// suppression so consumers never blanket-disable their own experimental warnings.
#nowarn "57"

namespace Partas.TestingPlatform

open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Requests

module Filtering =

    /// <summary>
    /// Reduces a platform filter to a predicate over a node's uid and properties. An unrecognised
    /// filter admits every node.
    /// </summary>
    let rec toPredicate (filter: ITestExecutionFilter) : string -> PropertyBag -> bool =
        match filter with
        | :? TestNodeUidListFilter as filter ->
            let admitted = filter.TestNodeUids |> Seq.map _.Value |> Set.ofSeq
            fun uid _ -> Set.contains uid admitted
        | :? TreeNodeFilter as filter -> fun uid properties -> filter.MatchesFilter(uid, properties)
        | :? CompositeTestExecutionFilter as filter ->
            let members = filter.Filters |> Seq.map toPredicate |> List.ofSeq
            fun uid properties -> members |> List.forall (fun admits -> admits uid properties)
        | _ -> fun _ _ -> true
