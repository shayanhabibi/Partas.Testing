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

/// <summary>
/// A handle on a group's fixture value, given to the group's children when they are built and
/// filled while the group runs.
/// </summary>
type Fixture<'a> internal (name: string) =
    let mutable held: 'a voption = ValueNone

    member internal _.Held = held
    member internal _.Fill(value: 'a) = held <- ValueSome value
    member internal _.Release() = held <- ValueNone

    /// <summary>
    /// The value setup produced. Reading before setup completes, or after teardown runs, raises
    /// <see cref="T:System.InvalidOperationException"/>.
    /// </summary>
    member _.Value =
        match held with
        | ValueSome value -> value
        | ValueNone -> invalidOp $"The fixture of '{name}' is readable only while the group runs."

/// <summary>
/// A group's setup and teardown, carried through the binding's property escape hatch. Both are
/// closed over the group's handle, so the fixture value stays invisible to the runner.
/// </summary>
type internal FixtureProperty(setup: unit -> Async<unit>, teardown: unit -> Async<unit>) =
    /// <summary>Produces the fixture value and fills the group's handle.</summary>
    member _.Setup = setup

    /// <summary>Consumes the fixture value and releases the group's handle.</summary>
    member _.Teardown = teardown

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

    /// <summary>
    /// A group owning a fixture. Setup runs once before the group's first leaf and teardown once
    /// after its last, and the children read the value through the handle they were built with.
    /// </summary>
    static member listWith
        (
            name: string,
            setup: unit -> Async<'a>,
            teardown: 'a -> Async<unit>,
            children: Fixture<'a> -> TestTree<TestBody> list,
            [<CallerFilePath>] ?file: string,
            [<CallerLineNumber>] ?line: int
        ) : TestTree<TestBody> =
        let fixture = Fixture<'a> name

        let bracket =
            FixtureProperty(
                (fun () ->
                    async {
                        let! value = setup ()
                        fixture.Fill value
                    }),
                fun () ->
                    async {
                        match fixture.Held with
                        | ValueSome value ->
                            try
                                do! teardown value
                            finally
                                fixture.Release()
                        | ValueNone -> ()
                    }
            )

        Group(name, locate file line, [ bracket ], children fixture)

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
