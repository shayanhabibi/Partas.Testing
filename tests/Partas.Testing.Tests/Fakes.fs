module Partas.Testing.Tests.Fakes

open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Messages
open Microsoft.Testing.Platform.TestHost
open Partas.Testing
open Partas.TestingPlatform

/// <summary>Reports no option as set, for fixtures that never declare command-line options.</summary>
type NoCommandLineOptions() =
    interface ICommandLineOptions with
        member _.IsOptionSet(_) = false

        member _.TryGetOptionArgumentList(_, arguments: byref<string[]>) =
            arguments <- [||]
            false

type RecordingMessageBus() =
    let gate = obj ()
    let published = ResizeArray<IData>()

    member _.Published = lock gate (fun () -> List.ofSeq published)

    member this.Updates =
        this.Published
        |> List.choose (function
            | :? TestNodeUpdateMessage as update -> Some update
            | _ -> None)

    interface IMessageBus with
        member _.PublishAsync(_, data) =
            lock gate (fun () -> published.Add data)
            Task.CompletedTask

type StubProducer() =
    interface IDataProducer with
        member _.DataTypesProduced = [| typeof<TestNodeUpdateMessage> |]

    interface IExtension with
        member _.Uid = "Partas.Testing.Tests"
        member _.Version = "1.0.0"
        member _.DisplayName = "stub"
        member _.Description = "stub"
        member _.IsEnabledAsync() = Task.FromResult true

/// <summary>
/// Runs a suite through the runner under the given session cancellation, and returns every
/// message the run published.
/// </summary>
let runSuiteUpdatesUnder (cancellation: CancellationToken) filterApplied tree =
    let resolved =
        match TestTree.resolve tree with
        | Ok resolved -> resolved
        | Error collisions -> failwithf "the fixture does not resolve: %A" collisions

    let bus = RecordingMessageBus()

    let context =
        { Tree = resolved
          Leaves = Execution.leaves resolved
          Reporter = Reporter(bus, StubProducer(), SessionUid "session")
          CancellationToken = cancellation
          FilterApplied = filterApplied
          CommandLineOptions = NoCommandLineOptions() }

    (Runner.run context).GetAwaiter().GetResult()

    bus.Updates

/// <summary>Runs a suite through the runner and returns every message the run published.</summary>
let runSuiteUpdates filterApplied tree =
    runSuiteUpdatesUnder CancellationToken.None filterApplied tree

/// <summary>
/// Every terminal, non-<c>InProgress</c> update published, paired with its uid, in publication
/// order. A uid reported twice appears twice.
/// </summary>
let terminalList updates =
    updates
    |> List.choose (fun (update: TestNodeUpdateMessage) ->
        let states: TestNodeStateProperty[] = update.TestNode.Properties.OfType()

        match Array.tryHead states with
        | Some state when not (state :? InProgressTestNodeStateProperty) -> Some(update.TestNode.Uid.Value, update)
        | _ -> None)

/// <summary>The terminal, non-<c>InProgress</c> update published for a leaf, by uid.</summary>
let terminalUpdates updates = terminalList updates |> Map.ofList

/// <summary>The state property an update carries.</summary>
let stateOf (update: TestNodeUpdateMessage) =
    let states: TestNodeStateProperty[] = update.TestNode.Properties.OfType()
    states.[0]

/// <summary>Runs a suite through the runner and returns the terminal state of every leaf.</summary>
let runSuiteUnder cancellation filterApplied tree =
    runSuiteUpdatesUnder cancellation filterApplied tree
    |> terminalUpdates
    |> Map.map (fun _ update -> stateOf update)

/// <summary>Runs a suite through the runner and returns the terminal state of every leaf.</summary>
let runSuite filterApplied tree =
    runSuiteUnder CancellationToken.None filterApplied tree
