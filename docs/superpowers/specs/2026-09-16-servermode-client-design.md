# Partas.TestingPlatform.Client — design

An F# client for the Microsoft.Testing.Platform (MTP) server-mode JSON-RPC protocol, built on
the source-only `Microsoft.Testing.Platform.ServerMode.Client.Sources` package.

## 1. Scope

`Partas.TestingPlatform.Client` is a shipped F# library. Any F# author can launch an MTP test
application in server mode and drive discovery and execution from it. The protocol test rig
described in `DESIGN.md` §12 is its first consumer and follows in a later spec.

The library does not reimplement the protocol. All wire, transport and serialization code is the
upstream source, compiled unchanged. The F# code is a typed surface over that source.

Protocol reference: <https://github.com/microsoft/testfx/blob/main/docs/mstest-runner-protocol/001-protocol-intro.md>.

## 2. Constraints from the upstream package

- The package ships C# source only, injected as `contentFiles`. F# cannot compile it, so a C#
  assembly is required.
- Every injected type is `internal`. A pack-time transform flips `public` to `internal`, moves
  the linked platform types (`TestNode`, `PropertyBag`, `SessionUid`, ...) under a package-private
  `Microsoft.Testing.Platform.ServerMode.Client.Protocol` namespace, and renames the vendored
  `Jsonite` namespace. The injected source therefore adds no public API and does not collide with
  `Microsoft.Testing.Platform.dll`.
- The consuming project needs C# 12 or newer. `net8.0` and later default to it.
- The package's own `.targets` appends `IS_CORE_MTP;IS_MTP_SERVER_MODE_CLIENT` and, on `net8.0`
  and later, `MTP_CLIENT_USE_MODERN_DOTNET` to `DefineConstants`. On `net8.0+` the
  System.Text.Json path is compiled; the Jsonite path is `netstandard2.0`-only.
- The client namespace is `Microsoft.Testing.Platform.ServerMode.Client`.

## 3. Packages

| Project | Language | TFMs | Package | Depends on |
|---|---|---|---|---|
| `src/Partas.TestingPlatform.Client.Protocol` | C# | `$(PartasTargetFrameworks)` | `Partas.TestingPlatform.Client.Protocol` | `Microsoft.Testing.Platform.ServerMode.Client.Sources` (exact) |
| `src/Partas.TestingPlatform.Client` | F# | `$(PartasTargetFrameworks)` | `Partas.TestingPlatform.Client` | `Partas.TestingPlatform.Client.Protocol` |

The shim project contains a single source file declaring
`[assembly: InternalsVisibleTo("Partas.TestingPlatform.Client")]`. It has no other code. Its
public API is empty, which is the contract: consumers use it only through the F# package.

The F# client does not reference `Microsoft.Testing.Platform`. The in-process launch path accepts
a plain callback, so the caller supplies whichever MTP version its test application uses.

The source package version is pinned exactly. It moves with the MTP floor in
`Partas.TestingPlatform` (`[2.4.0,3.0.0)`), both driven by one `MtpVersion` property in
`Directory.Build.props`. Floating a source-only package would silently change compiled code.

The existing `src/Partas.TestingPlatform.Client` stub (an `Exe` with an empty `Program.fs`) is
replaced by the library.

## 4. F# surface

Namespace `Partas.TestingPlatform.Client`.

### 4.1 `MtpClient`

```fsharp
type MtpClient =
    interface IDisposable

    static member Launch : source: string * ?options: MtpClientOptions -> MtpClient
    static member LaunchAsync :
        source: string * ?options: MtpClientOptions * ?cancellationToken: CancellationToken
            -> Task<MtpClient>
    static member LaunchInProcessAsync :
        run: (string[] -> CancellationToken -> Task<int>)
        * ?options: MtpClientOptions
        * ?cancellationToken: CancellationToken
            -> Task<MtpClient>

    member ProcessId : int
    member ServerExitCode : int option
    member Capabilities : ServerCapabilities option

    member InitializeAsync : ?cancellationToken: CancellationToken -> Task<ServerCapabilities>

    member DiscoverTestsAsync : ?cancellationToken: CancellationToken -> Task
    member DiscoverTestsAsync : uids: string seq * ?cancellationToken: CancellationToken -> Task
    member DiscoverTestsWithFilterAsync : filter: string * ?cancellationToken: CancellationToken -> Task

    member RunTestsAsync : ?cancellationToken: CancellationToken -> Task<RunResult>
    member RunTestsAsync : uids: string seq * ?cancellationToken: CancellationToken -> Task<RunResult>
    member RunTestsWithFilterAsync : filter: string * ?cancellationToken: CancellationToken -> Task<RunResult>

    member ExitAsync : ?cancellationToken: CancellationToken -> Task
    member ShutdownAsync : unit -> Task

    [<CLIEvent>] member TestNodesUpdated : IEvent<TestNodeUpdateBatch>
    [<CLIEvent>] member LogReceived : IEvent<LogMessage>
    [<CLIEvent>] member TelemetryReceived : IEvent<TelemetryEvent>
    [<CLIEvent>] member AttachmentsReceived : IEvent<Attachment list>

    member TestNodeUpdates : IObservable<TestNodeUpdateBatch>
    member Logs : IObservable<LogMessage>
    member Telemetry : IObservable<TelemetryEvent>
    member Attachments : IObservable<Attachment list>

    member ServerRequestHandler : ServerRequestHandler option with get, set
```

`ServerRequestHandler` is `string -> IReadOnlyDictionary<string, obj> option -> CancellationToken
-> Task<IReadOnlyDictionary<string, obj> option>` and receives server-to-client requests
(`telemetry/update`, `client/launchDebugger`, `client/attachDebugger`).

`F#` `IEvent<_>` implements `IObservable<_>`, so the `IObservable` members are the same event
values exposed under a second name for `Observable`-module callers. `Task` and `Task<_>` are used
at the boundary, matching `DESIGN.md` decision 6.

### 4.2 Options

```fsharp
type MtpClientOptions =
    { ClientName: string
      ClientVersion: string
      SupportedProtocolVersions: string list
      DebuggerProvider: bool
      IsStateful: bool option
      ConnectionTimeout: TimeSpan
      ServerShutdownTimeout: TimeSpan
      EnvironmentVariables: Map<string, string option>
      Logger: (ClientLogLevel -> string -> unit) option }

    static member Default : MtpClientOptions
```

`Default` carries the upstream defaults: client name
`Partas.TestingPlatform.Client`, version from the assembly, upstream supported protocol versions,
90 second connection timeout, 30 second shutdown timeout, and a silent logger.

`ClientLogLevel` is `Trace | Debug | Information | Warning | Error`.

### 4.3 Payloads

```fsharp
[<RequireQualifiedAccess>]
type NodeType = Action | Group | Other of string

[<RequireQualifiedAccess>]
type ExecutionState =
    | Discovered | InProgress | Passed | Skipped | Failed | TimedOut | Error | Canceled
    | Other of string

type SourceLocation = { File: string; LineStart: int option; LineEnd: int option }

type NodeError = { Message: string option; StackTrace: string option }

type TestNodeUpdate =
    { Uid: string
      DisplayName: string option
      NodeType: NodeType option
      ExecutionState: ExecutionState option
      ParentUid: string option
      Location: SourceLocation option
      Duration: TimeSpan option
      Error: NodeError option
      StandardOutput: string option
      StandardError: string option
      Raw: IReadOnlyDictionary<string, obj> }

type TestNodeUpdateBatch = { RunId: Guid; Updates: TestNodeUpdate list }

type ServerCapabilities =
    { ServerProcessId: int option
      ServerName: string option
      ServerVersion: string option
      ProtocolVersion: string option
      SupportsDiscovery: bool
      MultiRequestSupport: bool
      VSTestProviderSupport: bool
      SupportsAttachments: bool
      MultiConnectionProvider: bool }

type Attachment =
    { Uri: string option
      Producer: string option
      Type: string option
      DisplayName: string option
      Description: string option }

type RunResult = { Attachments: Attachment list }

type LogMessage = { Level: ClientLogLevel; Message: string }

type TelemetryEvent = { EventName: string; Metrics: IReadOnlyDictionary<string, obj> }
```

`Uid` is required. An update arriving with a missing `uid` is dropped and reported through the
options logger at `Warning`, since a node without identity cannot be correlated. `ExecutionState`
is `None` for a node published without an `execution-state` property. `Raw` holds the full
property dictionary so extension properties from the IDE-integration protocol document stay
reachable without a library change.

### 4.4 Exceptions

```fsharp
type MtpClientException(message: string, inner: exn) =
    inherit Exception(message, inner)

type MtpConnectionClosedException(message: string, inner: exn) =
    inherit MtpClientException(message, inner)

type MtpProtocolErrorException(code: int, message: string, inner: exn) =
    inherit MtpClientException(message, inner)
    member ErrorCode : int
```

Every public member catches the three upstream exception types at the boundary and raises the
corresponding F# exception with the original as `inner`. Other exceptions pass through unchanged.
The names differ from the upstream `MtpServerClientException`, `MtpServerConnectionClosedException`
and `MtpServerErrorException` so both sets can be referenced in the same file.

### 4.5 Interop module

`internal module Interop` holds one function per upstream type:

- `ofOptions : MtpClientOptions -> MtpServerClientOptions`
- `toCapabilities`, `toRunResult`, `toAttachment`, `toUpdateBatch`, `toUpdate`, `toLogMessage`,
  `toTelemetry`
- `translateException : exn -> exn`

The module is the whole glue layer. An upstream shape change surfaces as a compile error here and
nowhere else.

## 5. Lifecycle

Ownership follows the upstream contract:

- The client owns the launched or hosted server. Closing the transport is how a server-mode
  application is asked to stop.
- `ExitAsync` sends the protocol `exit` notification. Callers send it before shutdown for a
  graceful stop.
- `ShutdownAsync` tears down without blocking. `Dispose` performs the same work synchronously.
  Both are idempotent, and a `Dispose` after `ShutdownAsync` returns immediately.
- Teardown waits at most `ServerShutdownTimeout`, then cancels the in-process callback's token and
  waits a further fixed five seconds before abandoning it.
- Cancelling the token passed to a discover or run call sends `$/cancelRequest`.
- `ServerExitCode` is `Some` once teardown completes.
- `LaunchInProcessAsync` throws `PlatformNotSupportedException` on browser and WASM targets.

## 6. Maintenance model

Upgrading upstream is one change to `MtpVersion`. The shim recompiles the new source. If the C#
client's shape changed, the F# build fails inside `Interop`, which is the only place edits land.
The public F# surface changes only when a new upstream capability is worth exposing.

## 7. Testing

`tests/Partas.TestingPlatform.Client.Tests` on Expecto, pinned to the standalone runner like the
other binding tests. It drives `samples/Partas.TestingPlatform.Sample` over the wire two ways:

- as a child process through `LaunchAsync`, using the sample's built executable
- in-process through `LaunchInProcessAsync`, forwarding the argument array to the sample's
  `TestApplication` entry

Both paths run the same test list:

1. `InitializeAsync` returns capabilities with `SupportsDiscovery = true` and a protocol version.
2. Discover-all publishes every sample leaf exactly once, `Discovered`, with each `ParentUid`
   published earlier in the same session and a final batch with empty `Updates`.
3. Run-all publishes a terminal `ExecutionState` for every leaf and returns a `RunResult`.
4. Run by UID list executes only the named leaves.
5. Run with a graph filter executes only matching leaves.
6. Cancelling a run mid-flight, against a test-project suite with one leaf that waits on the
   run's cancellation token, completes the call and leaves the client usable for `ExitAsync`.
7. `ExitAsync` then `ShutdownAsync` yields `ServerExitCode = Some _`, and a second `Dispose` is a
   no-op.
8. `AttachmentsReceived` observes the TRX attachment when the in-process callback appends
   `--report-trx` to the server arguments. `LogReceived` is covered by the mapping test below,
   since the launch paths carry no verbosity flag.
9. An update lacking `uid` is dropped and logged, tested by feeding a property dictionary
   directly to the `Interop` mapping.

The property-based rig from `DESIGN.md` §12 is a separate spec and consumes this library.

## Decision log

| # | Decision |
|---|---|
| C1 | shipped F# library; protocol rig is its first consumer |
| C2 | typed-but-thin surface: `Task` at the boundary, records and DUs for payloads, `Raw` escape hatch |
| C3 | C# shim as its own package; `InternalsVisibleTo` is its only content |
| C4 | source package pinned exactly, moving with the MTP floor |
| C5 | client carries no `Microsoft.Testing.Platform` reference; in-process callback is a plain function |
| C6 | upstream exceptions translated to F# exceptions at the boundary |
| C7 | one internal `Interop` module is the entire glue layer |
