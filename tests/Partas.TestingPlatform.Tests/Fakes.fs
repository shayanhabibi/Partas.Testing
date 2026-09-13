module Partas.TestingPlatform.Tests.Fakes

open System.Threading.Tasks
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Messages

/// <summary>An <c>IMessageBus</c> retaining every published message in publication order.</summary>
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
        member _.Uid = "Partas.TestingPlatform.Tests"
        member _.Version = "1.0.0"
        member _.DisplayName = "stub"
        member _.Description = "stub"
        member _.IsEnabledAsync() = Task.FromResult true
