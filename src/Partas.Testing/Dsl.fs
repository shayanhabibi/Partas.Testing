namespace Partas.Testing

open System.Runtime.CompilerServices
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.TestingPlatform

/// <summary>What a test does when it runs.</summary>
type TestBody = Async<unit>

/// <summary>
/// Whether a node was marked in source. Focused narrows a run to the focused nodes; pending
/// never runs.
/// </summary>
[<RequireQualifiedAccess>]
type TestFocus =
    | Normal
    | Focused
    | Pending

/// <summary>A node's source marking, carried through the binding's property escape hatch.</summary>
type FocusProperty(state: TestFocus) =
    member _.State = state
    interface IProperty

[<AutoOpen>]
module internal Construction =

    let locate file line =
        match file, line with
        | Some file, Some line -> Some { File = file; Line = line }
        | _ -> None

    let focusProperties focus : IProperty list =
        match focus with
        | TestFocus.Normal -> []
        | state -> [ FocusProperty state ]

/// <summary>
/// Constructors for tests and groups. These are static members because F# honours caller-info
/// attributes only on optional parameters of members, and a node records where it was written.
/// </summary>
type Test =

    static member caseAsync
        (
            name: string,
            body: TestBody,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Leaf(name, locate file line, [], body)

    static member case
        (
            name: string,
            body: unit -> unit,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Leaf(name, locate file line, [], async { return body () })

    static member focused
        (
            name: string,
            body: unit -> unit,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Leaf(name, locate file line, focusProperties TestFocus.Focused, async { return body () })

    static member pending
        (
            name: string,
            body: unit -> unit,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Leaf(name, locate file line, focusProperties TestFocus.Pending, async { return body () })

    static member list
        (
            name: string,
            children: TestTree<TestBody> list,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Group(name, locate file line, [], children)

    static member focusedList
        (
            name: string,
            children: TestTree<TestBody> list,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Group(name, locate file line, focusProperties TestFocus.Focused, children)

    static member pendingList
        (
            name: string,
            children: TestTree<TestBody> list,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        Group(name, locate file line, focusProperties TestFocus.Pending, children)
