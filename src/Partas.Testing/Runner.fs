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

    let private declaredMode (node: ResolvedNode) =
        node.Properties
        |> List.tryPick (function
            | :? ModeProperty as mode -> Some mode.Mode
            | _ -> None)

    /// <summary>
    /// The mode a group runs its children under. A declared mode applies. A group owning a
    /// fixture otherwise runs its children in order; every other group inherits its parent's
    /// mode.
    /// </summary>
    let private modeOf (node: ResolvedNode) (inherited: TestMode) =
        match declaredMode node with
        | Some declared -> declared
        | None when (fixtureOf node).IsSome -> TestMode.Sequential
        | None -> inherited

    /// <summary>
    /// Runs every leaf the platform admitted, reporting a body that raises as failed and a leaf
    /// the plan excludes as skipped. A platform filter overrides a source marking of focus. A
    /// group owning a fixture brackets the leaves it runs with setup and teardown, and reports an
    /// outcome of its own when either raises. The root runs its children concurrently, and every
    /// group runs its own children under the mode <c>modeOf</c> gives it. Once the platform
    /// requests a graceful stop, every remaining leaf reports skipped and a fixture group not yet
    /// entered stays unused, while a group already bracketing its leaves still tears down.
    /// </summary>
    let run (context: RunContext<TestBody>) : Task =
        let plan = Focus.plan (not context.FilterApplied) context.Tree

        let outcomeOf (error: exn) =
            match error with
            | AssertionException(_, expected, actual) -> Failed(Some error, Some { Expected = expected; Actual = actual })
            | _ -> Failed(Some error, None)

        let cancelled = "the session was cancelled"

        let stopped = "the platform stopped the run early"

        let skipped reason =
            { TestResult.create Skipped with Explanation = Some reason }

        /// <summary>
        /// The result for a raised error, given whether the task the runtime built for it ended
        /// in the `Canceled` state. That state reports the raise as caused by the token passed to
        /// the task — the session's own cancellation — and reports `Skipped`. A task that instead
        /// ends `Faulted` reports `Failed`, whatever exception it carries, including an
        /// `OperationCanceledException` a body raised through its own logic while racing the
        /// session's cancellation.
        /// </summary>
        let resultOf (wasCanceled: bool) (error: exn) =
            if wasCanceled then skipped cancelled else TestResult.create (outcomeOf error)

        let execute (leaf: ExecutableLeaf<TestBody>) _ =
            task {
                let running = Async.StartAsTask(leaf.Payload, cancellationToken = context.CancellationToken)

                try
                    do! running
                    return TestResult.create Passed
                with error ->
                    return resultOf running.IsCanceled error
            }

        let settled result _ = Task.FromResult result

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
                let running = Async.StartAsTask(work, cancellationToken = cancellation)

                try
                    do! running
                    return None
                with error ->
                    return Some(running.IsCanceled, error)
            }

        let rec walk (parent: string option) (inherited: TestMode) (blocked: string option) tree =
            task {
                match tree with
                | ResolvedLeaf(node, payload) ->
                    let leaf = { Node = node; Parent = parent; Payload = payload }

                    // A leaf deactivated in source keeps its own reason, so a stray focus or
                    // pending mark stays legible under a group whose setup failed. A session
                    // already cancelled before this leaf starts skips it outright, rather than
                    // starting a body only to have the runtime refuse to run it.
                    match Map.tryFind node.Uid plan, blocked with
                    | Some(TestPlan.Skip reason), _ -> do! report leaf (settled (skipped reason))
                    | _, Some reason -> do! report leaf (settled (skipped reason))
                    | _, None when context.CancellationToken.IsCancellationRequested ->
                        do! report leaf (settled (skipped cancelled))
                    | _, None when context.GracefulStop.IsRequested -> do! report leaf (settled (skipped stopped))
                    | _, None -> do! report leaf (execute leaf)
                | ResolvedGroup(node, children) ->
                    let mode = modeOf node inherited

                    let descend blocking =
                        match mode with
                        | TestMode.Sequential ->
                            task {
                                for child in children do
                                    do! walk (Some node.Uid) mode blocking child
                            }
                        | TestMode.Parallel ->
                            // Mapping the walk over the children starts each one; `WhenAll` joins them.
                            task {
                                let running: Task[] =
                                    children
                                    |> List.map (fun child -> walk (Some node.Uid) mode blocking child :> Task)
                                    |> Array.ofList

                                do! Task.WhenAll running
                            }

                    match fixtureOf node, blocked with
                    | Some fixture, None when
                        List.exists runsAnything children && not context.GracefulStop.IsRequested
                        ->
                        // A group's own outcome travels through the leaf-shaped reporter record;
                        // the reporter reads its node and parent.
                        let group = { Node = node; Parent = parent; Payload = async.Zero() }

                        if context.CancellationToken.IsCancellationRequested then
                            do! report group (settled (skipped cancelled))
                            do! descend (Some cancelled)
                        else
                            match! attempt context.CancellationToken (fixture.Setup()) with
                            | Some(wasCanceled, error) ->
                                do! report group (settled (resultOf wasCanceled error))
                                do! descend (Some $"the setup of {node.Uid} failed")
                            | None ->
                                do! descend None

                                // Teardown runs outside the session's cancellation, so a
                                // cancelled run still releases what setup acquired.
                                match! attempt CancellationToken.None (fixture.Teardown()) with
                                | Some(_, error) -> do! report group (settled (TestResult.create (outcomeOf error)))
                                | None -> ()
                    | _ -> do! descend blocked
            }

        walk None TestMode.Parallel None context.Tree
