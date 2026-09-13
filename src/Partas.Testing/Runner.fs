namespace Partas.Testing

open System
open System.Threading
open System.Threading.Tasks
open Partas.TestingPlatform

module Runner =

    let private fixtureOf (node: ResolvedNode) =
        node.Properties
        |> List.tryPick (function
            | :? FixtureProperty as fixture -> Some fixture
            | _ -> None)

    /// <summary>
    /// Runs every leaf the platform admitted, reporting a body that raises as failed and a leaf
    /// the plan excludes as skipped. A platform filter overrides a source marking of focus. A
    /// group owning a fixture brackets the leaves it runs with setup and teardown, and reports an
    /// outcome of its own when either raises.
    /// </summary>
    let run (context: RunContext<TestBody>) : Task =
        let plan = Focus.plan (not context.FilterApplied) context.Tree

        let outcomeOf (error: exn) =
            match error with
            | AssertionException(_, expected, actual) -> Failed(Some error, Some { Expected = expected; Actual = actual })
            | _ -> Failed(Some error, None)

        /// <summary>
        /// The result for a raised error. Cancellation of the session token reports `Skipped`;
        /// any other exception, including a test's own `OperationCanceledException`, reports
        /// `Failed`.
        /// </summary>
        let resultOf (error: exn) =
            match error with
            | :? OperationCanceledException when context.CancellationToken.IsCancellationRequested ->
                { TestResult.create Skipped with Explanation = Some "the session was cancelled" }
            | _ -> TestResult.create (outcomeOf error)

        let execute (leaf: ExecutableLeaf<TestBody>) _ =
            task {
                try
                    do! Async.StartAsTask(leaf.Payload, cancellationToken = context.CancellationToken)
                    return TestResult.create Passed
                with error ->
                    return resultOf error
            }

        let settled result _ = Task.FromResult result

        let skipped reason =
            { TestResult.create Skipped with Explanation = Some reason }

        let report (leaf: ExecutableLeaf<TestBody>) body =
            task {
                let! _ = context.Reporter.Run(leaf, body, context.CancellationToken)
                return ()
            }

        /// <summary>Whether the plan runs a leaf anywhere under the tree.</summary>
        let rec runsAnything tree =
            match tree with
            | ResolvedLeaf(node, _) ->
                match Map.tryFind node.Uid plan with
                | Some(TestPlan.Skip _) -> false
                | _ -> true
            | ResolvedGroup(_, children) -> List.exists runsAnything children

        let attempt (cancellation: CancellationToken) (work: Async<unit>) =
            task {
                try
                    do! Async.StartAsTask(work, cancellationToken = cancellation)
                    return None
                with error ->
                    return Some error
            }

        let rec walk (parent: string option) (blocked: string option) tree =
            task {
                match tree with
                | ResolvedLeaf(node, payload) ->
                    let leaf = { Node = node; Parent = parent; Payload = payload }

                    // A leaf deactivated in source keeps its own reason, so a stray focus or
                    // pending mark stays legible under a group whose setup failed.
                    match Map.tryFind node.Uid plan, blocked with
                    | Some(TestPlan.Skip reason), _ -> do! report leaf (settled (skipped reason))
                    | _, Some reason -> do! report leaf (settled (skipped reason))
                    | _, None -> do! report leaf (execute leaf)
                | ResolvedGroup(node, children) ->
                    let descend blocking =
                        task {
                            for child in children do
                                do! walk (Some node.Uid) blocking child
                        }

                    match fixtureOf node, blocked with
                    | Some fixture, None when List.exists runsAnything children ->
                        // A group's own outcome travels through the leaf-shaped reporter record;
                        // the reporter reads its node and parent.
                        let group = { Node = node; Parent = parent; Payload = async.Zero() }

                        match! attempt context.CancellationToken (fixture.Setup()) with
                        | Some error ->
                            do! report group (settled (resultOf error))
                            do! descend (Some $"the setup of {node.Uid} failed")
                        | None ->
                            do! descend None

                            // Teardown runs outside the session's cancellation, so a cancelled
                            // run still releases what setup acquired.
                            match! attempt CancellationToken.None (fixture.Teardown()) with
                            | Some error -> do! report group (settled (TestResult.create (outcomeOf error)))
                            | None -> ()
                    | _ -> do! descend blocked
            }

        walk None None context.Tree
