# Session lifecycle

The MTP binding supports asynchronous session setup and cleanup independently of
the test DSL. Both callbacks receive `SessionContext` (MTP's session ID and
cancellation token) and return `Task<SessionOutcome>`.

```fsharp
let definition =
    testFramework<unit> {
        uid "my-framework"
        onCreateSession (fun context -> task {
            do! initializeResources context.CancellationToken
            return SessionOutcome.Succeeded None
        })
        tests buildTree
        onRun runTests
        onCloseSession (fun context -> task {
            do! releaseResources context.CancellationToken
            return SessionOutcome.Succeeded None
        })
    }
```

For a definition already built by `testSuite`, use
`FrameworkDefinition.withCreateSession setup` and
`FrameworkDefinition.withCloseSession cleanup`. Each replaces the existing
callback; callbacks are not accumulated.

Setup is awaited before building the tree. `SessionOutcome.Failed(error, warning)`
is returned to MTP without building the tree or executing tests. A successful
setup warning is retained even if tree construction subsequently fails.

Cleanup is awaited when MTP calls `CloseTestSessionAsync`; the binding does not
invent additional close calls. Make cleanup tolerant of partially initialized
resources. A cleanup failure is reported to MTP, not discarded. Callback faults
and cancellation propagate to MTP rather than being converted into success.
In the in-process `TestApplication.run` path they escape to the caller as
exceptions; use `SessionOutcome.Failed` for an expected setup/cleanup failure
that MTP can report as a session result.

Both callbacks default to successful no-ops. Existing definitions need no changes.
This adds no test attributes, synthetic tests, or IDE-specific metadata.

## Verification notes

The integration tests run real MTP applications, checking asynchronous ordering,
session identity, setup failure preventing execution, cleanup failure affecting
the exit code, warning preservation on tree failure, and propagation of faulted
and cancelled callback tasks. Diagnostic output is
captured from a child process because MTP output bypassed `Console.SetOut` in
the in-process test harness.

Run `dotnet run --project tests/Partas.TestingPlatform.Tests`, followed by the
native bootstrap checks:

```text
dotnet run --project tests/Partas.Testing.IDE.Tests -- --list-tests
node tests/Partas.Testing.IDE.Tests/verify-bootstrap.mjs
```
