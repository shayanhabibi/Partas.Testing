# Plan: complete Partas.Testing

Spec: `DESIGN.md` at the repository root. Where this plan and the spec disagree, the spec wins.

## Global Constraints

- **TDD is mandatory.** `.claude/rules/` and the repo's practice: write the failing test, run it,
  confirm it fails for the intended reason, then implement. A test that passes the moment it is
  written is not evidence — either it guards behaviour that already exists (say so) or it is wrong.
- **Comment style** is governed by `.claude/rules/comments.md`. No `<paramref>` in doc comments:
  naming one parameter makes the analyser demand documentation for all of them. State contracts,
  not justifications.
- **Zero warnings.** `dotnet build Partas.Testing.slnx` must report 0 errors and 0 warnings.
- Both suites must pass: `tests/Partas.TestingPlatform.Tests` (56) and `tests/Partas.Testing.Tests` (26).
  Run them with `dotnet run --project <proj> -f net10.0`.
- **The seam holds** (spec §3). The binding owns identity and protocol; the framework owns
  execution semantics. No `Task.WhenAll` in `Partas.TestingPlatform`.
- Framework code targets `$(PartasTargetFrameworks)` = `net8.0;net10.0`. Do not use APIs missing
  on net8.0.
- Union cases must not shadow common names. `Error` shadows `Result.Error`; `Focused`/`Pending`
  collide with Expecto. Use `[<RequireQualifiedAccess>]` where a collision is plausible.
- Commit each task separately. End commit messages with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

## Existing interfaces you will build on

```fsharp
// Partas.TestingPlatform
type TestTree<'T> = Leaf of name * SourceLocation option * IProperty list * 'T
                  | Group of name * SourceLocation option * IProperty list * TestTree<'T> list
type TestOutcome = Passed | Skipped | Failed of exn option * AssertionFailure option
                 | Errored of exn option | TimedOut of exn option * TimeSpan option
type AssertionFailure = { Expected: string option; Actual: string option }
type TestResult = { Outcome; Explanation; StandardOutput; StandardError; RetryAttempt; ExtraProperties }
type ExecutableLeaf<'T> = { Node: ResolvedNode; Parent: string option; Payload: 'T }
type RunContext<'T> = { Tree; Leaves; Reporter; CancellationToken; FilterApplied }
Reporter.Run : ExecutableLeaf<'T> * (CancellationToken -> Task<TestResult>) * CancellationToken -> Task<TestResult>

// Partas.Testing
type TestBody = Async<unit>
type TestFocus = Normal | Focused | Pending          // RequireQualifiedAccess
type FocusProperty(state: TestFocus)                  // rides in the IProperty escape hatch
type TestPlan = Run | Skip of reason: string          // RequireQualifiedAccess
Focus.plan : honourFocus: bool -> ResolvedTestTree<'T> -> Map<string, TestPlan>
Runner.run : RunContext<TestBody> -> Task
Test.case / caseAsync / focused / pending / list / focusedList / pendingList
```

---

## Task 1: Assertion library

Create `src/Partas.Testing/Expect.fs` (compiled after `Dsl.fs`, before `Focus.fs`).

Define an exception carrying structured expectation data, and make the runner translate it into
`Failed(Some ex, Some assertion)` so the binding emits `AssertionFailureProperty`.

```fsharp
exception AssertionException of message: string * expected: string option * actual: string option
```

Required members of `module Expect`, each taking a trailing `message: string` describing intent:

- `equal actual expected message` — structural equality, `expected`/`actual` rendered with `%A`
- `notEqual actual expected message`
- `isTrue value message` / `isFalse value message`
- `isSome value message` / `isNone value message`
- `isOk value message` / `isError value message`
- `throws (fn: unit -> unit) message` — passes when `fn` raises
- `failure message` — always fails, no expected/actual

`Expect.equal 2 1 "parses an int"` must produce an `AssertionException` whose message names the
intent, the expected value, and the actual value.

Then change `Runner.run` so a body raising `AssertionException` is reported
`Failed(Some ex, Some { Expected = ...; Actual = ... })`, while any other exception stays
`Failed(Some ex, None)`. Existing runner tests must keep passing.

**Verify end to end:** add an assertion to `samples/Partas.Testing.Sample`, run it, and confirm the
platform reports the failure. Record the observed output in the report.

---

## Task 2: Group-scoped fixtures

Spec §Q18(C) — the differentiator over Expecto. A group may own setup producing a value and
teardown consuming it; descendants receive the value.

Add to `Dsl.fs`:

```fsharp
Test.listWith : name * setup: (unit -> Async<'a>) * teardown: ('a -> Async<unit>) *
                children: (Fixture<'a> -> TestTree<TestBody> list) * ?file * ?line -> TestTree<TestBody>
```

`children` receives a **handle**, not the fixture value. Discovery must enumerate the whole tree
without executing setup, so the children are built eagerly at construction and their bodies close
over the handle, reading the value when they run:

```fsharp
type Fixture<'a> =
    /// The value produced by setup. Raises before setup completes and after teardown runs.
    member Value: 'a
```

Back the handle with a mutable slot the runner fills after setup and clears after teardown. Keep
**one tree** (spec §Q28): do not introduce a second tree type that the framework projects from.

Runner behaviour:

- setup runs once per group, before any descendant leaf
- teardown runs once, after the last descendant leaf, **even when a leaf fails**
- setup raising: the group node reports `Failed` carrying the exception, and every descendant leaf
  reports `Skipped` whose explanation names the group's uid (spec §6). The group node therefore
  becomes an `action` and joins the counts — that is intended for a setup failure.
- teardown raising must not overwrite a leaf result that already reported

Publishing a group result needs a reporter call against a group node. `Reporter.Run` takes an
`ExecutableLeaf`, which is a leaf-shaped record; constructing one for a group node is acceptable,
but note it in the report as a binding API smell for later.

---

## Task 3: Parallelism

Spec §Q19(C): parallelism is a group property inherited by descendants; the root defaults to
parallel; **a group declaring setup/teardown defaults to sequential within itself** unless marked
parallel explicitly.

Add to `Dsl.fs`: `Test.sequentialList` and `Test.parallelList` (same shape as `Test.list`), carrying
the mode through an `IProperty` like `FocusProperty` does.

Change `Runner.run` to honour the mode. Leaves in a parallel group run concurrently; leaves in a
sequential group run in order. Fixture setup and teardown ordering from Task 2 must hold under
both modes.

`Reporter` publishes through `IMessageBus`, which is safe under concurrent callers — no
serialisation needed.

Tests must prove concurrency actually happens (e.g. two leaves that each wait on the other's
signal complete only if run in parallel; use a timeout so a sequential implementation fails rather
than hangs forever).

**Verify end to end:** a parallel sample suite reports the same counts as a sequential one.

---

## Task 4: Partas.TestingPlatform.CommandLine

Spec §8. New project `src/Partas.TestingPlatform.CommandLine`, referencing the binding and
`FSharp.SystemCommandLine`. Add it to `Partas.Testing.slnx` under `/src/`.

MTP rejects unrecognised options and terminates before framework code runs, so every option must be
declared to MTP. Derive MTP's `ICommandLineOptionsProvider` from `System.CommandLine.Option`
values: `Name`, `Description`, and `Arity` (`Min`/`Max`) map onto `CommandLineOption` and
`ArgumentArity`. System.CommandLine names carry a `--` prefix; MTP's do not — strip it.

Public surface: a function turning a list of System.CommandLine options into an
`ICommandLineOptionsProvider`, plus whatever registration helper the binding needs to accept it.
The core binding must gain **no** System.CommandLine dependency.

**Verify end to end:** a sample declaring a custom flag starts without the "Unknown option" failure
and reads the flag's value. Record the observed output.

---

## Task 5: Partas.TestingPlatform.Trx

Spec §9. New project `src/Partas.TestingPlatform.Trx`, referencing the binding and
`Microsoft.Testing.Extensions.TrxReport.Abstractions`. Add it to `Partas.Testing.slnx` under `/src/`.

Implement `ITrxReportCapability` (`IsSupported`, `Enable`) and expose a way to add it to the
capability set the binding builds. Emit `TrxFullyQualifiedTypeNameProperty` from a node's path so
TRX grouping has an honest value — the spec forbids fabricating `TestMethodIdentifierProperty`.

**Verify end to end:** run a sample with `--report-trx` and confirm a TRX file is written with the
expected test names. Record the observed output.

---

## Task 2b: partial-application DSL

Change every constructor in `src/Partas.Testing/Dsl.fs` from tupled to partial-application shape, so
call sites read like Expecto.

Before: `Test.case ("parses an int", fun () -> ())`
After:  `Test.case "parses an int" (fun () -> ())`

The member declares **only** the name plus the caller-info optionals, and returns a function:

```fsharp
static member case (name: string, [<CallerFilePath>] ?file: string, [<CallerLineNumber>] ?line: int) =
    fun (body: unit -> unit) -> Leaf(name, locate file line, [], async { return body () })
```

Verified by probe: this captures the correct call-site line under juxtaposition
(`Test.case "n" (fun () -> ())`) and under pipelining (`body |> Test.case "n"`).

**The trap, also verified:** any `let`-bound wrapper bakes in the *library's* line, not the caller's.
`let testCase name body = Test.case name body` captured the line of that binding for every call.
So there must be no module-level alias, and the `Test.` prefix stays.

Apply to `case`, `caseAsync`, `focused`, `pending`, `list`, `focusedList`, `pendingList`, and
`listWith`. `listWith` has four leading arguments (name, setup, teardown, children) — keep
name+setup+teardown+caller-info in the member and return a function taking `children`, or keep it
tupled if the mixed form reads worse; state which you chose and why.

Update every call site: `tests/Partas.Testing.Tests/` (Tests.fs, FocusTests.fs, RunnerTests.fs,
FixtureTests.fs) and `samples/Partas.Testing.Sample/Program.fs`.

Line numbers are asserted in `Tests.fs` ("a case records the line it was written on"). Those
assertions must still hold and must still be capturing the *call site*, not a fixed number — do not
simply update a constant to make a test pass.

## Task 2c: cancellation reports Skipped

`DESIGN.md` decision 20 and §4.4 say a body cancelled on the session token reports `Skipped` with an
explanation. The framework reports `Failed` carrying a `TaskCanceledException`, in two places:
`Runner.fs`'s `execute` (a cancelled leaf body) and the setup call (a session cancelled before or
during setup). Teardown was already detached from the session token in task 2 and is not affected.

Make cancellation on the session token report `Skipped` with an explanation naming cancellation, for
leaf bodies and for fixture setup alike. A cancellation that is *not* the session token's — an
`OperationCanceledException` the test itself threw for its own reasons — must still report `Failed`;
distinguish them by checking the token.

Also correct `DESIGN.md` §6: it says children of a failed-setup group "report `Skipped` naming the
group's UID", but source-deactivated children now correctly report their own reason instead. Qualify
the sentence to defer to §7.
