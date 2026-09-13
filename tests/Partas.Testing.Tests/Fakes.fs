module Partas.Testing.Tests.Fakes

open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Messages
open Microsoft.Testing.Platform.TestHost
open Partas.Testing
open Partas.TestingPlatform

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

/// <summary>Runs a suite through the runner and returns every message the run published.</summary>
let runSuiteUpdates filterApplied tree =
    let resolved =
        match TestTree.resolve tree with
        | Ok resolved -> resolved
        | Error collisions -> failwithf "the fixture does not resolve: %A" collisions

    let bus = RecordingMessageBus()

    let context =
        { Tree = resolved
          Leaves = Execution.leaves resolved
          Reporter = Reporter(bus, StubProducer(), SessionUid "session")
          CancellationToken = CancellationToken.None
          FilterApplied = filterApplied }

    (Runner.run context).GetAwaiter().GetResult()

    bus.Updates

/// <summary>The terminal, non-<c>InProgress</c> update published for a leaf, by uid.</summary>
let terminalUpdates updates =
    updates
    |> List.choose (fun (update: TestNodeUpdateMessage) ->
        let states: TestNodeStateProperty[] = update.TestNode.Properties.OfType()

        match Array.tryHead states with
        | Some state when not (state :? InProgressTestNodeStateProperty) -> Some(update.TestNode.Uid.Value, update)
        | _ -> None)
    |> Map.ofList

/// <summary>Runs a suite through the runner and returns the terminal state of every leaf.</summary>
let runSuite filterApplied tree =
    runSuiteUpdates filterApplied tree
    |> terminalUpdates
    |> Map.map (fun _ update ->
        let states: TestNodeStateProperty[] = update.TestNode.Properties.OfType()
        states.[0])
