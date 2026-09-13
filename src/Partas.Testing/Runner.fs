namespace Partas.Testing

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

        let execute (leaf: ExecutableLeaf<TestBody>) _ =
            task {
                try
                    do! Async.StartAsTask(leaf.Payload, cancellationToken = context.CancellationToken)
                    return TestResult.create Passed
                with error ->
                    return TestResult.create (outcomeOf error)
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

        let attempt (work: Async<unit>) =
            task {
                try
                    do! Async.StartAsTask(work, cancellationToken = context.CancellationToken)
                    return None
                with error ->
                    return Some error
            }

        let rec walk (parent: string option) (blocked: string option) tree =
            task {
                match tree with
                | ResolvedLeaf(node, payload) ->
                    let leaf = { Node = node; Parent = parent; Payload = payload }

                    match blocked, Map.tryFind node.Uid plan with
                    | Some reason, _ -> do! report leaf (settled (skipped reason))
                    | None, Some(TestPlan.Skip reason) -> do! report leaf (settled (skipped reason))
                    | None, _ -> do! report leaf (execute leaf)
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

                        match! attempt (fixture.Setup()) with
                        | Some error ->
                            do! report group (settled (TestResult.create (outcomeOf error)))
                            do! descend (Some $"the setup of {node.Uid} failed")
                        | None ->
                            do! descend None

                            match! attempt (fixture.Teardown()) with
                            | Some error -> do! report group (settled (TestResult.create (outcomeOf error)))
                            | None -> ()
                    | _ -> do! descend blocked
            }

        walk None None context.Tree
