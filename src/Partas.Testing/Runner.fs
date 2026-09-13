namespace Partas.Testing

open System.Threading.Tasks
open Partas.TestingPlatform

module Runner =

    /// <summary>
    /// Runs every leaf the platform admitted, reporting a body that raises as failed and a leaf
    /// the plan excludes as skipped. A platform filter overrides a source marking of focus.
    /// </summary>
    let run (context: RunContext<TestBody>) : Task =
        let plan = Focus.plan (not context.FilterApplied) context.Tree

        let execute (leaf: ExecutableLeaf<TestBody>) _ =
            task {
                try
                    do! Async.StartAsTask(leaf.Payload, cancellationToken = context.CancellationToken)
                    return TestResult.create Passed
                with
                | AssertionException(_, expected, actual) as error ->
                    return TestResult.create (Failed(Some error, Some { Expected = expected; Actual = actual }))
                | error ->
                    return TestResult.create (Failed(Some error, None))
            }

        task {
            for leaf in context.Leaves do
                match Map.tryFind leaf.Node.Uid plan with
                | Some(TestPlan.Skip reason) ->
                    let skipped = { TestResult.create Skipped with Explanation = Some reason }
                    let! _ = context.Reporter.Run(leaf, (fun _ -> Task.FromResult skipped), context.CancellationToken)
                    ()
                | _ ->
                    let! _ = context.Reporter.Run(leaf, execute leaf, context.CancellationToken)
                    ()
        }
