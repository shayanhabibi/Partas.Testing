# Partas.TestingPlatform — design

An F# binding to Microsoft.Testing.Platform, and `Partas.Testing`, an Expecto-shaped
framework built on it.

## 1. Scope

`Partas.TestingPlatform` is a separately packaged, general-purpose binding. Any F# author can
build a test framework on it. `Partas.Testing` is its first consumer and the only source of
pressure on its API: nothing enters the binding until `Partas.Testing` exercises it.

The binding targets MTP natively. No VSTest bridge — bridging would force the tree through
VSTest's flat, attribute-shaped `TestCase` model.

## 2. Packages

| Package | Depends on | Contents |
|---|---|---|
| `Partas.TestingPlatform` | `Microsoft.Testing.Platform` only | the binding |
| `Partas.TestingPlatform.CommandLine` | binding, `FSharp.SystemCommandLine` | option adapter |
| `Partas.TestingPlatform.Trx` | binding, `Microsoft.Testing.Extensions.TrxReport` | TRX capability and report registration |
| `Partas.Testing` | binding | the framework |

`TargetFrameworks` is `net8.0;net10.0`, set in `Directory.Build.props`. `PackageId` is
per-project. The MTP reference floats within `[2.4.0,3.0.0)`.

## 3. The seam

The binding owns **identity and protocol**. The framework owns **execution semantics**.

| Binding | Framework |
|---|---|
| tree structure, UID and path derivation | test bodies, fixtures, group semantics |
| collision detection | parallelism policy |
| the discovery walk | the execution walk |
| filter interpretation | retry policy, focus/pending |
| message publishing, timing, `InProgress` | assertion formatting, failure messages |
| session lifecycle, `Complete()` | output capture |

The binding contains no `Task.WhenAll` and calls user code only through the reporter's
bracketing thunk.

## 4. Core types

### 4.1 Tree

One generic tree, read by both walks. Discovery ignores `'T`; execution consumes it.

```fsharp
type TestTree<'T> =
    | Leaf  of name: string * location: SourceLocation option * properties: IProperty list * payload: 'T
    | Group of name: string * location: SourceLocation option * properties: IProperty list * children: TestTree<'T> list
```

`SourceLocation` is captured through `[<CallerFilePath>]` / `[<CallerLineNumber>]`. F# honours
caller-info attributes only on optional parameters of members, so the framework's `test` and
`testList` constructors are static members rather than module-level `let` bindings. Groups
capture their own location from the `testList` call site.

### 4.2 Resolution

```fsharp
type ResolvedTestTree<'T>   // TestTree<'T> with uid and path on every node
type Collision = { Path: string; Name: string }

val resolve : TestTree<'T> -> Result<ResolvedTestTree<'T>, Collision list>
```

Paths are `/`-joined — forced by `TreeNodeFilter.PathSeparator`, which is `const '/'`. A `/`
in a user-supplied name is escaped, as is the escape character. A node's UID and its filter
path are the same string.

Duplicate sibling names are a hard error naming the colliding path. Positional
disambiguation would re-point every downstream UID when a sibling is reordered or deleted,
which breaks IDE re-run and rots stored filters.

The tree resolves once per process during `CreateTestSessionAsync` and is cached. Both walks
read the same value, so discovery UIDs and execution UIDs cannot diverge.

### 4.3 Session

```fsharp
[<Struct>]
type SessionContext = { SessionId: SessionUid; CancellationToken: CancellationToken }

type SessionOutcome =
    | Succeeded of warning: string option
    | Failed    of error: string * warning: string option
```

`SessionContext` unifies `CreateTestSessionContext` and `CloseTestSessionContext` through
`SessionContext.ofCreate` and `ofClose`. `SessionOutcome` converts through internal
`toCreateResult` / `toCloseResult`.

These are explicit functions, not `op_Implicit`. Implicit conversion requires `#nowarn 3391`
at every site that uses it, resolves against an expected type and so fails wherever a type is
inferred, and is unresolvable when the expected type is a type variable. The conversions
occur in two places inside the binding's own `ITestFramework` implementation.

### 4.4 Outcome

```fsharp
type TestOutcome =
    | Passed
    | Skipped of explanation: string option
    | Failed  of exn option * assertion: AssertionFailure option
    | Error   of exn option
    | Timeout of exn option * TimeSpan option

and AssertionFailure = { Expected: string option; Actual: string option }

type TestResult =
    { Outcome: TestOutcome
      Explanation: string option
      StandardOutput: string option
      StandardError: string option
      RetryAttempt: RetryAttempt option
      ExtraProperties: IProperty list }

and RetryAttempt = { AttemptNumber: int; IsSuperseded: bool }
```

`CancelledTestNodeStateProperty` is obsolete in MTP and has no case here. A test body
cancelled on the session token reports `Skipped` with an explanation.

Exceptions pass through untouched. Formatting a failure message is the framework's concern —
assertion diffing is what `Partas.Testing` adds above the binding.

`ExtraProperties` keeps consumers unblocked on platform properties the binding has not
modelled.

`TestMethodIdentifierProperty` is never emitted. It requires seven fields including method
arity, parameter type full names, and return type full name, none of which a closure has.
`TrxFullyQualifiedTypeNameProperty` covers TRX grouping with one honest string when the TRX
package is in play.

**`Timeout` is modelled and unreachable.** The binding maps it to
`TimeoutTestNodeStateProperty`, and a framework constructing that case gets a correct platform
report. Neither `Partas.TestingPlatform` nor `Partas.Testing` has a timeout mechanism: nothing
imposes a deadline on a test body, so nothing produces the case. The case exists so a framework
that adds its own deadline has a state to report through; a suite hanging on a wedged body hangs
the run until the platform's own deadline or an operator kills it.

**A hanging teardown outlives cancellation.** `Partas.Testing` runs fixture teardown under
`CancellationToken.None`, so a `Ctrl-C`'d run still releases what setup acquired. A teardown that
never returns therefore keeps the process alive past the interrupt, and the operator's only
recourse is killing it. Releasing the resource is worth the risk; a bounded teardown wait would
need the timeout mechanism above.

## 5. Discovery

The binding walks the resolved tree and publishes every node. Parent links come from
`TestNodeUpdateMessage(sessionUid, node, parentUid)`.

Leaves carry `DiscoveredTestNodeStateProperty`. **Groups carry no state property.** A node
carrying an execution state is an `action` in the server protocol and the platform counts it
as a test; a node carrying none is a `group`. Publishing a group with a state inflates
`--list-tests` by the number of groups while the run summary counts only leaves.

Each node carries `TestFileLocationProperty` when a location was captured.

## 6. Execution and reporting

The framework walks. Per surviving leaf it calls:

```fsharp
member Reporter.Run : ResolvedLeaf<'T> * (CancellationToken -> Task<TestResult>) -> Task<unit>
```

The binding publishes `InProgress`, awaits that publish, starts the clock, invokes the thunk,
stops the clock, and publishes the outcome with `TimingProperty`. Ordering, concurrency, and
retry all stay with the framework.

`IMessageBus.PublishAsync` is safe under concurrent callers, so the reporter publishes
directly.

A reporter takes a per-leaf property contribution, `ExecutableLeaf<unit> -> IProperty list`, held
on `FrameworkDefinition.LeafProperties` and composed through
`FrameworkDefinition.addLeafProperties`. The contribution runs once per reported outcome and its
properties join the published node. It exists for a companion package whose writer demands a
property on every result: `Partas.TestingPlatform.Trx`'s writer raises on a result carrying no
`TrxFullyQualifiedTypeNameProperty`, so `TrxReport.enable` contributes one and every framework
built on the binding gets a working `--report-trx` without its own walk naming TRX.

**Degree of parallelism is unbounded.** `Runner.run` roots the walk at `TestMode.Parallel`, a
parallel group starts every child at once, and `Test.case` wraps a synchronous body in an
`async`, so a blocking body holds a thread pool worker for its whole duration. A suite of
blocking bodies wide enough drains the pool, and the runtime injects replacement workers at a
rate of roughly one or two per second, so the run degrades to that rate until the first bodies
return. The framework has no degree-of-parallelism setting: the default degree, whether the bound
is per group or global, and how a user configures it are a design question this slice does not
answer.

Available today:

- `Test.sequentialList` bounds a group to one body at a time, and descendants inherit the mode,
  so wrapping a suite of blocking bodies in one caps that subtree at a single worker.
- `Test.caseAsync` with a genuinely asynchronous body releases its worker at every `do!`.
- `ThreadPool.SetMinThreads` raises the floor so the pool hands workers over immediately rather
  than at the injection rate. This repository's own Expecto suite does exactly that in
  `tests/Partas.Testing.Tests/Fakes.fs`, where every nested run starts from a worker its Expecto
  test already holds.

Before execution begins the binding publishes every surviving group node — the ancestors of
leaves that passed the filter, and only those. Ancestors precede descendants, so no
`parentTestNodeUid` ever references an unpublished node.

Groups carry a state property only when they have an outcome of their own. A group whose
setup fails reports `Failed`. Its children report `Skipped`, naming their own source-deactivated
reason where one applies (§7) and the group's UID otherwise. That group becomes an `action` and
joins the counts, which is the intent — a setup failure is a failure. Ordinary groups carry no
state, so totals count leaves exactly.

`ExecuteRequestContext.Complete()` runs in a `try/finally` around the whole request. A tree
that fails to construct or resolve reports through `SessionOutcome.Failed` rather than through
a synthetic test node.

## 7. Filtering

`ITestExecutionFilter` is a marker with no members. The binding interprets all four
implementations — `NopFilter`, `TestNodeUidListFilter`, `TreeNodeFilter` (received only; it
has no public constructor), and `CompositeTestExecutionFilter` (recursive) — and exposes the
result as one total function:

```fsharp
val toPredicate : ITestExecutionFilter -> (string -> PropertyBag -> bool)
```

The binding registers `AddTreeNodeFilterService` so `--treenode-filter` exists, and
`AddMaximumFailedTestsService` so `--maximum-failed-tests` works.

Framework-level focus and pending compose as follows: a non-`NopFilter` platform filter
overrides source focus, so clicking one test in an IDE runs that test. Focus applies when the
platform asked for everything.

Filter-excluded leaves are never published — a filter means "show me a subset".
Focus-deactivated and pending leaves are published and report `Skipped` — they exist and were
deliberately deactivated, and hiding them lets a stray `ftest` silently disable a suite in CI.

## 8. Command line

MTP rejects unrecognised options and terminates before framework code runs, so every option
must be declared to MTP.

`Partas.TestingPlatform.CommandLine` derives MTP's `ICommandLineOptionsProvider` from
`FSharp.SystemCommandLine` definitions. `System.CommandLine.Option` carries `Name`,
`Description`, and an `Arity` with `Min`/`Max`, which map onto `CommandLineOption` and
`ArgumentArity`; System.CommandLine names carry a `--` prefix and MTP's do not.

The core binding exposes the raw `ICommandLineOptionsProvider` seam and takes no
System.CommandLine dependency.

## 9. Capabilities

- `IBannerMessageOwnerCapability` — optional CE operation; returning `None` yields the
  platform banner.
- `IGracefulStopTestExecutionResultCapability` — implemented, paired with the
  maximum-failed-tests registration. Every definition declares it, whether or not it declares
  capabilities of its own. Both its own `TryStopTestExecutionAsync` and the inherited
  `StopTestExecutionAsync` set the `GracefulStop` latch on `RunContext`, and the former reports
  `true`, holding the platform to its graceful path so tests already running reach their own end.
  Honouring the latch is the framework's: `Partas.Testing` reads it before each leaf, reports
  every remaining one `Skipped`, and leaves a fixture group it has not yet entered unused, while a
  group already bracketing its leaves still tears down.
- `ITrxReportCapability` — `Partas.TestingPlatform.Trx`, so the core takes no extra
  Microsoft dependency. The capability alone does not write a report: `.Abstractions` declares
  the contract, but the writer that acts on it ships in the sibling `Microsoft.Testing.Extensions.TrxReport`
  package, which `Partas.TestingPlatform.Trx` references directly and registers through
  `AddTrxReportProvider()`. Reaching the builder to call that registration is an
  `internal`-only seam (`FrameworkDefinition`'s representation, gated by an `InternalsVisibleTo`
  grant to `Partas.TestingPlatform.Trx`) rather than a public CE operation — a public one would
  let any consumer register a deferred
  `Func<IServiceProvider, _>` factory that runs inside a session and reaches `IMessageBus`, the
  same escalation the core's public surface otherwise closes off. `InternalsVisibleTo` without a
  matching strong name grants access by unsigned assembly name, which any assembly compiled with
  a matching name can also obtain: the seam raises the bar against an accidental caller rather
  than closing the door on a deliberate one. Strong-naming this assembly would close that gap but
  changes the shipped package's identity for every consumer, so it is left as the project owner's
  decision rather than applied here.

## 10. Hosting and identity

The public surface is a computation expression over an internal handler record. A CE grows by
adding custom operations, which breaks nobody; a public record breaks every construction site
when a field is added, and the capability surface is entirely `[Experimental]` and will move.

`uid` is required. `IExtension.Uid` is SHA-256'd for telemetry, shown in `--info`, embedded in
artifact metadata, and used for feature detection, and MTP's own documentation warns against
deriving it from a name that a rename can change. `version` defaults to the assembly's
informational version; `displayName` and `description` default to `uid`.

Entry points are hand-written:

```fsharp
[<EntryPoint>]
let main argv = runTestsWithArgs argv (fun () -> suite)
```

`Partas.Testing` fixes its own `uid`, `version`, `displayName` and `description`: they identify
the framework to the platform, not the suite, and a per-suite uid would change the identity
`--info`, telemetry and artifact metadata key on. Everything else about the definition is open
through `testSuite`, which returns the `FrameworkDefinition` the one-liner would have run, for a
suite that registers a companion package or declares command-line options of its own:

```fsharp
[<EntryPoint>]
let main argv =
    testSuite (fun () -> suite)
    |> TrxReport.enable
    |> TestApplication.run argv
```

`FrameworkDefinition.addCapability`, `addCommandLineOptionsProvider` and `addLeafProperties` are
the definition-to-definition counterparts of the CE operations, each appending to what the
definition already declares. `samples/Partas.Testing.Sample` runs the TRX form above.

The binding's package sets `GenerateTestingPlatformEntryPoint=false` — MTP's generator emits
C#.

## 11. Public surface and stability

The binding passes through inert MTP data types — `SessionUid`, `TestNodeUid`, `IProperty`,
`LinePositionSpan` — and wraps everything behavioural: `ITestFramework`, the filters, the
capabilities, `IMessageBus`, `ExecuteRequestContext`.

Passing types through keeps the `[2.4.0,3.0.0)` float honest: one assembly identity across the
major, so a consumer resolving a different minor binds cleanly.

F# surfaces C# `[Experimental("TPEXP")]` as `warning FS0057`, which is a single blanket
warning number rather than a per-diagnostic suppression — `#nowarn "57"` also silences F#'s
own experimental warnings.

The binding absorbs that suppression internally for its stable core, and marks its own
TPEXP-mirroring surface with F#'s `[<Experimental>]`. A consumer's suppression then scopes to
code that genuinely sits on shifting ground. Version 1.0 covers the structural core.

## 12. Testing

`tests/Partas.TestingPlatform.ProtocolTests` drives the framework over the real server
protocol using `Microsoft.Testing.Platform.ServerMode.Client.Sources`.

Property-based, over generated trees:

- every leaf appears exactly once in discovery
- UIDs are unique
- discovery UIDs and execution UIDs match
- a path round-trips through the filter predicate
- parent links reproduce the constructed tree
- every `parentTestNodeUid` was published earlier in the same session
- concurrent reporters produce well-formed, non-interleaved node updates

Golden-file message-stream snapshots cover two or three representative suites.

The binding's own tests run on Expecto with its FsCheck integration, pinned to the standalone
runner. A bug in UID derivation or message publishing would otherwise sit inside the machinery
reporting the results. `Partas.Testing` dogfoods once the binding underneath is independently
trusted.

## 13. Sequence

**Housekeeping commit.** `src/Partas.Testing.MTP` → `src/Partas.TestingPlatform`. Delete
`src/Partas.Testing.MTP.ServerMode` (an empty namespace with a commented-out reference) and
create `tests/Partas.TestingPlatform.ProtocolTests`. `src/Partas.Testing/Library.fs` declares
`namespace Partas.Test` and `obj/` holds stale `Partas.Test.AssemblyInfo.fs`. Add
`TargetFrameworks` to `Directory.Build.props`.

**Vertical slice.** The thinnest binding and framework that resolve a two-level tree, discover
it, run it, report pass and fail with locations, and render the hierarchy in an IDE. Both
deepen together, which is what makes the consumer-driven rule in §1 enforceable.

## Decision log

| # | Decision |
|---|---|
| 1 | shipped, consumer-driven binding |
| 2 | explicit conversions, no `op_Implicit` |
| 3 | `SessionOutcome` DU |
| 4 | tree-first; binding owns UIDs |
| 5 | caller-info locations; member-based DSL |
| 6 | `Task` at the boundary, `Async` in the framework |
| 7 | `net8.0;net10.0`; MTP `[2.4.0,3.0.0)` |
| 8 | ServerMode becomes the protocol rig |
| 9 | MTP-native only |
| 10 | CE over an internal handler record |
| 11 | `/`-joined paths; loud collisions |
| 12 | materialized, resolved once, cached |
| 13 | groups are nodes carrying no state; state only with an outcome; UID equals filter path |
| 14 | no `TestMethodIdentifierProperty` |
| 15 | typed outcome DU, typed assertion failure, `ExtraProperties` escape hatch |
| 16 | binding interprets filters, exposes a predicate, auto-registers services |
| 17 | hand-written entry point; generator disabled |
| 18 | group-scoped fixtures, then inherited metadata (framework) |
| 19 | parallelism inherited per group; fixture groups sequential (framework) |
| 20 | `Complete()` in `finally`; construction failure is a session failure; cancellation is `Skipped` |
| 21 | `uid` required; version from the assembly |
| 22/26 | options derived from FSharp.SystemCommandLine in a separate package |
| 23 | banner optional; graceful stop implemented; TRX separate |
| 24 | timing and `InProgress` always |
| 25 | binding walks discovery, framework walks execution |
| 27 | property-based rig plus goldens; core not dogfooded |
| 28 | one generic `TestTree<'T>` |
| 29 | run publishes surviving ancestors and leaves |
| 30 | `Partas.TestingPlatform` + `.CommandLine` + `.Trx` |
| 31 | 1.0 core; own `[<Experimental>]` on the mirroring surface |
| 32 | pass through inert types, wrap behavioural ones |
| 33 | `ResolvedTestTree<'T>`; resolution is a total function |
| 34 | bracketing reporter |
| 35 | platform filter overrides focus; focus is `Skipped`, filtered is unpublished |
| 36 | retry modelled, orchestrated by the framework |
| 37 | vertical slice |
| 38 | ancestors published up front |
| 39 | Expecto and FsCheck, hosted by the VSTest adapter so one `dotnet test` runs every suite |
| 40 | housekeeping commit first |
| 41 | a leaf-property contribution seam, so a companion package supplies the properties its writer requires |
