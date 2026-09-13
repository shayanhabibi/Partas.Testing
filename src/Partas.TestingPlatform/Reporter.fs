namespace Partas.TestingPlatform

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Testing.Platform.Extensions.Messages
open Microsoft.Testing.Platform.Messages
open Microsoft.Testing.Platform.TestHost

/// <summary>
/// Publishes the progress and the outcome of individual leaves. Ordering and concurrency belong
/// to the caller; a reporter brackets one leaf it is handed.
/// </summary>
type Reporter(bus: IMessageBus, producer: IDataProducer, session: SessionUid) =

    /// <summary>
    /// Publishes the leaf as in progress, runs the body, then publishes its result with the
    /// elapsed timing. A body that raises is published as errored and returned as such.
    /// </summary>
    member _.Run(leaf: ExecutableLeaf<'T>, body: CancellationToken -> Task<TestResult>, cancellation: CancellationToken) : Task<TestResult> =
        let parent = leaf.Parent |> Option.map TestNodeUid |> Option.toObj

        let send (properties: IProperty list) =
            let testNode = Nodes.toTestNode (Some(List.head properties)) leaf.Node
            properties |> List.tail |> List.iter testNode.Properties.Add
            bus.PublishAsync(producer, TestNodeUpdateMessage(session, testNode, parent))

        task {
            do! send [ InProgressTestNodeStateProperty.CachedInstance ]

            let start = DateTimeOffset.UtcNow
            let clock = Diagnostics.Stopwatch.StartNew()

            let! result =
                task {
                    try
                        return! body cancellation
                    with error ->
                        return TestResult.create (Errored(Some error))
                }

            clock.Stop()
            let timing = TimingInfo(start, start + clock.Elapsed, clock.Elapsed)

            do! send (TestResult.properties result @ [ TimingProperty timing ])
            return result
        }
