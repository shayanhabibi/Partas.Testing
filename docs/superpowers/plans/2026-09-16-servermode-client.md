# Partas.TestingPlatform.Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an F# library that launches a Microsoft.Testing.Platform (MTP) test application in server mode and drives discovery and execution over JSON-RPC, using the upstream source-only C# client unchanged.

**Architecture:** A C# shim project compiles `Microsoft.Testing.Platform.ServerMode.Client.Sources` and grants `InternalsVisibleTo` the F# project. The F# project exposes one `MtpClient` type plus records and DUs; all mapping between upstream C# types and F# types lives in one internal `Interop` module. An Expecto test project drives the existing sample application over the wire, both as a child process and in-process.

**Tech Stack:** F# (`net8.0;net10.0` via `$(PartasTargetFrameworks)`), C# 12 shim, `Microsoft.Testing.Platform.ServerMode.Client.Sources` 2.4.0, Expecto 11 alpha with YoloDev.Expecto.TestSdk.

**Spec:** `docs/superpowers/specs/2026-09-16-servermode-client-design.md`

## Global Constraints

- Every shell command is prefixed with `rtk` (repo-wide rule). `rtk dotnet build ...`, `rtk git add ...`.
- Shipped projects target `$(PartasTargetFrameworks)` (`net8.0;net10.0`). Test and sample projects target `net10.0`.
- The source package version is pinned exactly to `$(MtpVersion)` = `2.4.0`, defined once in `Directory.Build.props`. Never float it.
- The F# client project never references `Microsoft.Testing.Platform`.
- Every injected upstream type is `internal` and lives in namespace `Microsoft.Testing.Platform.ServerMode.Client`. In F# files, refer to them fully qualified. Do not `open` that namespace: the F# exception names and upstream names would then shadow each other.
- Doc comments follow `.claude/rules/comments.md`: describe the contract, no justification, no exclusion framing. A member whose meaning is exhausted by its name and type gets no comment.
- Commit after each task. Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Do not stage or commit files outside the task's listed files. The working tree carries unrelated user changes.
- After restore, the upstream sources are readable at
  `%USERPROFILE%\.nuget\packages\microsoft.testing.platform.servermode.client.sources\2.4.0\contentFiles\cs\net8.0\Client\`. When a member name in this plan does not compile, read `IMtpServerClient.cs` and `MtpServerClientOptions.cs` there and use the real name. Record any such correction in the task's commit message.

---

## File structure

| Path | Responsibility |
|---|---|
| `Directory.Build.props` | adds `MtpVersion` |
| `src/Partas.TestingPlatform/Partas.TestingPlatform.fsproj` | MTP floor reads `MtpVersion` |
| `src/Partas.TestingPlatform.Client.Protocol/Partas.TestingPlatform.Client.Protocol.csproj` | C# shim: compiles the source package |
| `src/Partas.TestingPlatform.Client.Protocol/InternalsVisibleTo.cs` | the shim's only source |
| `src/Partas.TestingPlatform.Client/Partas.TestingPlatform.Client.fsproj` | F# library (replaces the `Exe` stub) |
| `src/Partas.TestingPlatform.Client/AssemblyInfo.fs` | `InternalsVisibleTo` for the test project |
| `src/Partas.TestingPlatform.Client/Types.fs` | public records, DUs, options, exceptions |
| `src/Partas.TestingPlatform.Client/Interop.fs` | internal mapping to and from upstream types |
| `src/Partas.TestingPlatform.Client/MtpClient.fs` | the public client type |
| `samples/Partas.TestingPlatform.Sample/Program.fs` | exposes `definition` for in-process hosting |
| `tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj` | test project |
| `tests/Partas.TestingPlatform.Client.Tests/InteropTests.fs` | pure mapping tests |
| `tests/Partas.TestingPlatform.Client.Tests/Fixture.fs` | launch helpers, update collection, slow suite |
| `tests/Partas.TestingPlatform.Client.Tests/ChildProcessTests.fs` | wire tests through `LaunchAsync` |
| `tests/Partas.TestingPlatform.Client.Tests/InProcessTests.fs` | wire tests through `LaunchInProcessAsync` |
| `tests/Partas.TestingPlatform.Client.Tests/Main.fs` | Expecto entry point |
| `Partas.Testing.slnx` | registers the two new projects |
| `docs/content/guide/server-mode-client.md` | consumer guide |
| `DESIGN.md` | package table rows |

---

### Task 1: C# shim project and `MtpVersion`

**Files:**
- Modify: `Directory.Build.props`
- Modify: `src/Partas.TestingPlatform/Partas.TestingPlatform.fsproj`
- Create: `src/Partas.TestingPlatform.Client.Protocol/Partas.TestingPlatform.Client.Protocol.csproj`
- Create: `src/Partas.TestingPlatform.Client.Protocol/InternalsVisibleTo.cs`
- Modify: `Partas.Testing.slnx`

**Interfaces:**
- Produces: assembly `Partas.TestingPlatform.Client.Protocol` containing the upstream internal types, visible to assembly `Partas.TestingPlatform.Client`.

- [ ] **Step 1: Add `MtpVersion` to `Directory.Build.props`**

Inside the first `<PropertyGroup>` (the one holding `PartasTargetFrameworks`), add:

```xml
<!-- Floor of the Microsoft.Testing.Platform reference and the exact server-mode client source version. -->
<MtpVersion>2.4.0</MtpVersion>
```

- [ ] **Step 2: Make the binding's MTP floor read it**

In `src/Partas.TestingPlatform/Partas.TestingPlatform.fsproj`, change

```xml
<PackageReference Include="Microsoft.Testing.Platform" Version="[2.4.0,3.0.0)" />
```

to

```xml
<PackageReference Include="Microsoft.Testing.Platform" Version="[$(MtpVersion),3.0.0)" />
```

- [ ] **Step 3: Create the shim csproj**

`src/Partas.TestingPlatform.Client.Protocol/Partas.TestingPlatform.Client.Protocol.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFrameworks>$(PartasTargetFrameworks)</TargetFrameworks>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <PackageId>Partas.TestingPlatform.Client.Protocol</PackageId>
        <Title>Partas.TestingPlatform.Client.Protocol</Title>
        <Description>The Microsoft.Testing.Platform server-mode client protocol, compiled for Partas.TestingPlatform.Client. Contains no public API.</Description>
        <!-- The assembly has no public types; there is nothing to document. -->
        <GenerateDocumentationFile>false</GenerateDocumentationFile>
    </PropertyGroup>

    <ItemGroup>
        <!-- Source-only package: its C# files compile into this assembly as internal types. -->
        <PackageReference Include="Microsoft.Testing.Platform.ServerMode.Client.Sources" Version="[$(MtpVersion)]" PrivateAssets="all" />
    </ItemGroup>

</Project>
```

- [ ] **Step 4: Create the only source file**

`src/Partas.TestingPlatform.Client.Protocol/InternalsVisibleTo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Partas.TestingPlatform.Client")]
```

- [ ] **Step 5: Register in the solution**

In `Partas.Testing.slnx`, under `<Folder Name="/src/">`, add after the existing Client line:

```xml
<Project Path="src/Partas.TestingPlatform.Client.Protocol/Partas.TestingPlatform.Client.Protocol.csproj" />
```

- [ ] **Step 6: Build the shim**

Run: `rtk dotnet build src/Partas.TestingPlatform.Client.Protocol -c Debug`
Expected: succeeds for both TFMs with zero errors. Warnings from the injected source are acceptable; do not add suppressions.

- [ ] **Step 7: Verify the injected types are present and internal**

Run:

```bash
rtk dotnet build src/Partas.TestingPlatform.Client.Protocol -c Debug -f net10.0 -v q && ls ~/.nuget/packages/microsoft.testing.platform.servermode.client.sources/2.4.0/contentFiles/cs/net8.0/Client/
```

Expected: the listing shows `IMtpServerClient.cs`, `MtpServerClient.cs`, `MtpServerClientOptions.cs` among others. Open `IMtpServerClient.cs` and confirm the names of the properties on `MtpAttachment`, `MtpLogEventArgs`, `MtpTelemetryEventArgs` and `MtpRunResult`. Note them for Task 3.

- [ ] **Step 8: Commit**

```bash
rtk git add Directory.Build.props src/Partas.TestingPlatform/Partas.TestingPlatform.fsproj src/Partas.TestingPlatform.Client.Protocol Partas.Testing.slnx
rtk git commit -m "Add C# shim compiling the MTP server-mode client sources

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: F# client project scaffold and public types

**Files:**
- Modify: `src/Partas.TestingPlatform.Client/Partas.TestingPlatform.Client.fsproj` (rewrite)
- Delete: `src/Partas.TestingPlatform.Client/Program.fs`
- Create: `src/Partas.TestingPlatform.Client/AssemblyInfo.fs`
- Create: `src/Partas.TestingPlatform.Client/Types.fs`

**Interfaces:**
- Produces: every public type in namespace `Partas.TestingPlatform.Client` listed in spec §4.2, §4.3, §4.4, with `NodeType` and `ExecutionState` `[<RequireQualifiedAccess>]`.

- [ ] **Step 1: Rewrite the fsproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFrameworks>$(PartasTargetFrameworks)</TargetFrameworks>
        <LangVersion>latest</LangVersion>
        <PackageId>Partas.TestingPlatform.Client</PackageId>
        <Title>Partas.TestingPlatform.Client</Title>
        <Description>An F# client for Microsoft.Testing.Platform server mode.</Description>
    </PropertyGroup>

    <ItemGroup>
        <Compile Include="AssemblyInfo.fs"/>
        <Compile Include="Types.fs"/>
        <Compile Include="Interop.fs"/>
        <Compile Include="MtpClient.fs"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../Partas.TestingPlatform.Client.Protocol/Partas.TestingPlatform.Client.Protocol.csproj"/>
    </ItemGroup>

</Project>
```

Delete `Program.fs`. `Interop.fs` and `MtpClient.fs` are created in Tasks 3 and 4; until then create them as empty modules so the project compiles:

`Interop.fs`:
```fsharp
module internal Partas.TestingPlatform.Client.Interop
```

`MtpClient.fs`:
```fsharp
namespace Partas.TestingPlatform.Client
```

- [ ] **Step 2: AssemblyInfo.fs**

```fsharp
module internal Partas.TestingPlatform.Client.AssemblyInfo

open System.Runtime.CompilerServices

[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Client.Tests")>]
do ()
```

- [ ] **Step 3: Types.fs**

```fsharp
namespace Partas.TestingPlatform.Client

open System
open System.Collections.Generic

type ClientLogLevel =
    | Trace
    | Debug
    | Information
    | Warning
    | Error

/// <summary>Client-side settings for a server-mode session.</summary>
type MtpClientOptions =
    { ClientName: string
      ClientVersion: string
      SupportedProtocolVersions: string list
      DebuggerProvider: bool
      IsStateful: bool option
      ConnectionTimeout: TimeSpan
      ServerShutdownTimeout: TimeSpan
      /// <summary>Variables applied to a child-process server. Ignored by in-process hosting.</summary>
      EnvironmentVariables: Map<string, string option>
      Logger: (ClientLogLevel -> string -> unit) option }

    static member Default: MtpClientOptions =
        { ClientName = "Partas.TestingPlatform.Client"
          ClientVersion =
            typeof<MtpClientOptions>.Assembly.GetName().Version
            |> Option.ofObj
            |> Option.map string
            |> Option.defaultValue "1.0.0"
          SupportedProtocolVersions = [ "1.0.0" ]
          DebuggerProvider = false
          IsStateful = None
          ConnectionTimeout = TimeSpan.FromSeconds 90.0
          ServerShutdownTimeout = TimeSpan.FromSeconds 30.0
          EnvironmentVariables = Map.empty
          Logger = None }

[<RequireQualifiedAccess>]
type NodeType =
    | Action
    | Group
    | Other of string

[<RequireQualifiedAccess>]
type ExecutionState =
    | Discovered
    | InProgress
    | Passed
    | Skipped
    | Failed
    | TimedOut
    | Error
    | Canceled
    | Other of string

type SourceLocation =
    { File: string
      LineStart: int option
      LineEnd: int option }

type NodeError =
    { Message: string option
      StackTrace: string option }

/// <summary>One node in a <c>testing/testUpdates/tests</c> notification.</summary>
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
      /// <summary>Every property on the node as published, including extension properties.</summary>
      Raw: IReadOnlyDictionary<string, obj> }

type TestNodeUpdateBatch =
    { RunId: Guid
      Updates: TestNodeUpdate list }

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

type LogMessage =
    { Level: ClientLogLevel
      Message: string }

type TelemetryEvent =
    { EventName: string
      Metrics: IReadOnlyDictionary<string, obj> }

/// <summary>Raised for any failure reported by the server-mode client.</summary>
type MtpClientException(message: string, inner: exn) =
    inherit Exception(message, inner)

/// <summary>Raised when the server closed the connection or never connected.</summary>
type MtpConnectionClosedException(message: string, inner: exn) =
    inherit MtpClientException(message, inner)

/// <summary>Raised when the server answered a request with a JSON-RPC error.</summary>
type MtpProtocolErrorException(code: int, message: string, inner: exn) =
    inherit MtpClientException(message, inner)
    member _.ErrorCode = code
```

- [ ] **Step 4: Build**

Run: `rtk dotnet build src/Partas.TestingPlatform.Client -c Debug`
Expected: success for both TFMs.

- [ ] **Step 5: Commit**

```bash
rtk git add src/Partas.TestingPlatform.Client
rtk git commit -m "Scaffold Partas.TestingPlatform.Client with its public types

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Interop mapping with pure tests

**Files:**
- Modify: `src/Partas.TestingPlatform.Client/Interop.fs`
- Create: `tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj`
- Create: `tests/Partas.TestingPlatform.Client.Tests/InteropTests.fs`
- Create: `tests/Partas.TestingPlatform.Client.Tests/Main.fs`
- Modify: `Partas.Testing.slnx`

**Interfaces:**
- Consumes: types from Task 2.
- Produces (all in `module internal Partas.TestingPlatform.Client.Interop`):
  - `toNodeType : string -> NodeType`
  - `toExecutionState : string -> ExecutionState`
  - `toUpdate : log:(ClientLogLevel -> string -> unit) -> parentUid:string option -> node:IReadOnlyDictionary<string, obj> -> TestNodeUpdate option`
  - `toUpdateBatch : log:(ClientLogLevel -> string -> unit) -> Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdateEventArgs -> TestNodeUpdateBatch`
  - `toCapabilities : Microsoft.Testing.Platform.ServerMode.Client.MtpServerCapabilities -> ServerCapabilities`
  - `toAttachment`, `toRunResult`, `toLogMessage`, `toTelemetry`
  - `toLogLevel : Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel -> ClientLogLevel`
  - `ofOptions : MtpClientOptions -> Microsoft.Testing.Platform.ServerMode.Client.MtpServerClientOptions`
  - `translateException : exn -> exn`

- [ ] **Step 1: Create the test project**

`tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <LangVersion>latest</LangVersion>
        <IsPackable>false</IsPackable>
        <GenerateProgramFile>false</GenerateProgramFile>
        <RootNamespace>Partas.TestingPlatform.Client.Tests</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <Compile Include="InteropTests.fs"/>
        <Compile Include="Main.fs"/>
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Expecto" Version="11.0.0-alpha8"/>
        <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.15.5"/>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../../src/Partas.TestingPlatform.Client/Partas.TestingPlatform.Client.fsproj"/>
    </ItemGroup>

</Project>
```

`Main.fs`:

```fsharp
module Partas.TestingPlatform.Client.Tests.Main

open Expecto

[<EntryPoint>]
let main argv = runTestsInAssemblyWithCLIArgs [] argv
```

Register in `Partas.Testing.slnx` under `<Folder Name="/tests/">`:

```xml
<Project Path="tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj" />
```

- [ ] **Step 2: Write the failing mapping tests**

`InteropTests.fs`:

```fsharp
module Partas.TestingPlatform.Client.Tests.InteropTests

open System
open System.Collections.Generic
open Expecto
open Partas.TestingPlatform.Client

let private node (pairs: (string * obj) list) : IReadOnlyDictionary<string, obj> =
    Dictionary<string, obj>(dict pairs) :> IReadOnlyDictionary<string, obj>

let private silent (_: ClientLogLevel) (_: string) = ()

[<Tests>]
let tests =
    testList "Interop" [
        testCase "maps every known node-type and execution-state string" <| fun () ->
            Expect.equal (Interop.toNodeType "action") NodeType.Action "action"
            Expect.equal (Interop.toNodeType "group") NodeType.Group "group"
            Expect.equal (Interop.toNodeType "widget") (NodeType.Other "widget") "unknown node type"
            let cases =
                [ "discovered", ExecutionState.Discovered
                  "in-progress", ExecutionState.InProgress
                  "passed", ExecutionState.Passed
                  "skipped", ExecutionState.Skipped
                  "failed", ExecutionState.Failed
                  "timed-out", ExecutionState.TimedOut
                  "error", ExecutionState.Error
                  "canceled", ExecutionState.Canceled
                  "weird", ExecutionState.Other "weird" ]
            for wire, expected in cases do
                Expect.equal (Interop.toExecutionState wire) expected wire

        testCase "maps a fully populated node" <| fun () ->
            let raw =
                node [
                    "uid", box "/a/b"
                    "display-name", box "b"
                    "node-type", box "action"
                    "execution-state", box "failed"
                    "location.file", box "C:/src/Tests.fs"
                    "location.line-start", box 12L
                    "location.line-end", box 14L
                    "time.duration-ms", box 1500.0
                    "error.message", box "boom"
                    "error.stacktrace", box "at X"
                    "standardOutput", box "out"
                    "standardError", box "err"
                    "custom.ext", box "kept" ]
            let update = Interop.toUpdate silent (Some "/a") raw |> Option.get
            Expect.equal update.Uid "/a/b" "uid"
            Expect.equal update.DisplayName (Some "b") "display name"
            Expect.equal update.NodeType (Some NodeType.Action) "node type"
            Expect.equal update.ExecutionState (Some ExecutionState.Failed) "state"
            Expect.equal update.ParentUid (Some "/a") "parent"
            Expect.equal update.Location (Some { File = "C:/src/Tests.fs"; LineStart = Some 12; LineEnd = Some 14 }) "location"
            Expect.equal update.Duration (Some(TimeSpan.FromMilliseconds 1500.0)) "duration"
            Expect.equal update.Error (Some { Message = Some "boom"; StackTrace = Some "at X" }) "error"
            Expect.equal update.StandardOutput (Some "out") "stdout"
            Expect.equal update.StandardError (Some "err") "stderr"
            Expect.equal (update.Raw["custom.ext"]) (box "kept") "raw retains extension properties"

        testCase "maps a minimal node to None-valued fields" <| fun () ->
            let update = Interop.toUpdate silent None (node [ "uid", box "/x" ]) |> Option.get
            Expect.equal update.NodeType None "node type"
            Expect.equal update.ExecutionState None "state"
            Expect.equal update.Location None "location"
            Expect.equal update.Duration None "duration"
            Expect.equal update.Error None "error"
            Expect.equal update.ParentUid None "parent"

        testCase "omits the error record when both error fields are absent" <| fun () ->
            let update = Interop.toUpdate silent None (node [ "uid", box "/x"; "execution-state", box "passed" ]) |> Option.get
            Expect.equal update.Error None "error"

        testCase "drops a node without uid and logs a warning" <| fun () ->
            let logged = ResizeArray<ClientLogLevel * string>()
            let log level message = logged.Add((level, message))
            let update = Interop.toUpdate log None (node [ "display-name", box "orphan" ])
            Expect.isNone update "dropped"
            Expect.equal logged.Count 1 "one log entry"
            Expect.equal (fst logged[0]) ClientLogLevel.Warning "level"

        testCase "accepts integer duration and line numbers boxed as int" <| fun () ->
            let update =
                Interop.toUpdate silent None (node [ "uid", box "/x"; "time.duration-ms", box 7; "location.file", box "f"; "location.line-start", box 3 ])
                |> Option.get
            Expect.equal update.Duration (Some(TimeSpan.FromMilliseconds 7.0)) "duration"
            Expect.equal update.Location (Some { File = "f"; LineStart = Some 3; LineEnd = None }) "location"

        testCase "ofOptions carries every setting across" <| fun () ->
            let options =
                { MtpClientOptions.Default with
                    ClientName = "n"
                    ClientVersion = "9.9.9"
                    SupportedProtocolVersions = [ "1.0.0" ]
                    DebuggerProvider = true
                    IsStateful = Some true
                    ConnectionTimeout = TimeSpan.FromSeconds 5.0
                    ServerShutdownTimeout = TimeSpan.FromSeconds 6.0
                    EnvironmentVariables = Map.ofList [ "A", Some "1"; "B", None ] }
            let upstream = Interop.ofOptions options
            Expect.equal upstream.ClientName "n" "name"
            Expect.equal upstream.ClientVersion "9.9.9" "version"
            Expect.sequenceEqual upstream.SupportedProtocolVersions [ "1.0.0" ] "protocol versions"
            Expect.isTrue upstream.DebuggerProvider "debugger"
            Expect.equal upstream.IsStateful (Nullable true) "stateful"
            Expect.equal upstream.ConnectionTimeout (TimeSpan.FromSeconds 5.0) "connection timeout"
            Expect.equal upstream.ServerShutdownTimeout (TimeSpan.FromSeconds 6.0) "shutdown timeout"
            Expect.equal upstream.EnvironmentVariables["A"] "1" "env A"
            Expect.isNull upstream.EnvironmentVariables["B"] "env B cleared"

        testCase "ofOptions forwards the logger" <| fun () ->
            let logged = ResizeArray<ClientLogLevel * string>()
            let options = { MtpClientOptions.Default with Logger = Some(fun l m -> logged.Add((l, m))) }
            let upstream = Interop.ofOptions options
            upstream.Logger.Log(Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Error, "x")
            Expect.equal (List.ofSeq logged) [ ClientLogLevel.Error, "x" ] "forwarded"

        testCase "translateException wraps the three upstream exceptions" <| fun () ->
            let closed = Microsoft.Testing.Platform.ServerMode.Client.MtpServerConnectionClosedException("closed")
            let error = Microsoft.Testing.Platform.ServerMode.Client.MtpServerErrorException(-32600, "bad")
            let generic = Microsoft.Testing.Platform.ServerMode.Client.MtpServerClientException("generic")
            match Interop.translateException closed with
            | :? MtpConnectionClosedException as e -> Expect.equal e.InnerException (closed :> exn) "inner"
            | other -> failtestf "expected MtpConnectionClosedException, got %A" other
            match Interop.translateException error with
            | :? MtpProtocolErrorException as e -> Expect.equal e.ErrorCode -32600 "code"
            | other -> failtestf "expected MtpProtocolErrorException, got %A" other
            match Interop.translateException generic with
            | :? MtpClientException as e -> Expect.equal e.Message "generic" "message"
            | other -> failtestf "expected MtpClientException, got %A" other
            let unrelated = InvalidOperationException "x"
            Expect.equal (Interop.translateException unrelated) (unrelated :> exn) "others pass through"
    ]
```

The test project references upstream internals, so it needs the shim's `InternalsVisibleTo` too. Add to `src/Partas.TestingPlatform.Client.Protocol/InternalsVisibleTo.cs`:

```csharp
[assembly: InternalsVisibleTo("Partas.TestingPlatform.Client.Tests")]
```

If the upstream exception constructors take different arguments than shown, read `MtpServerClientExceptions.cs` in the NuGet cache and adjust the test to the real constructors.

- [ ] **Step 3: Run the tests and confirm they fail to compile**

Run: `rtk dotnet build tests/Partas.TestingPlatform.Client.Tests -c Debug`
Expected: errors that `toNodeType`, `toUpdate`, `ofOptions`, `translateException` are not defined.

- [ ] **Step 4: Implement Interop.fs**

```fsharp
module internal Partas.TestingPlatform.Client.Interop

open System
open System.Collections.Generic

let private tryString (key: string) (node: IReadOnlyDictionary<string, obj>) : string option =
    match node.TryGetValue key with
    | true, (:? string as s) -> Some s
    | _ -> None

let private tryNumber (key: string) (node: IReadOnlyDictionary<string, obj>) : float option =
    match node.TryGetValue key with
    | true, (:? float as d) -> Some d
    | true, (:? single as f) -> Some(float f)
    | true, (:? int as i) -> Some(float i)
    | true, (:? int64 as l) -> Some(float l)
    | true, (:? int16 as s) -> Some(float s)
    | true, (:? decimal as m) -> Some(float m)
    | _ -> None

let toNodeType (wire: string) : NodeType =
    match wire with
    | "action" -> NodeType.Action
    | "group" -> NodeType.Group
    | other -> NodeType.Other other

let toExecutionState (wire: string) : ExecutionState =
    match wire with
    | "discovered" -> ExecutionState.Discovered
    | "in-progress" -> ExecutionState.InProgress
    | "passed" -> ExecutionState.Passed
    | "skipped" -> ExecutionState.Skipped
    | "failed" -> ExecutionState.Failed
    | "timed-out" -> ExecutionState.TimedOut
    | "error" -> ExecutionState.Error
    | "canceled" -> ExecutionState.Canceled
    | other -> ExecutionState.Other other

let toUpdate
    (log: ClientLogLevel -> string -> unit)
    (parentUid: string option)
    (node: IReadOnlyDictionary<string, obj>)
    : TestNodeUpdate option =
    match tryString "uid" node with
    | None ->
        log ClientLogLevel.Warning "Dropped a test node update that carries no uid."
        None
    | Some uid ->
        let location =
            tryString "location.file" node
            |> Option.map (fun file ->
                { File = file
                  LineStart = tryNumber "location.line-start" node |> Option.map int
                  LineEnd = tryNumber "location.line-end" node |> Option.map int })
        let error =
            match tryString "error.message" node, tryString "error.stacktrace" node with
            | None, None -> None
            | message, stackTrace -> Some { Message = message; StackTrace = stackTrace }
        Some
            { Uid = uid
              DisplayName = tryString "display-name" node
              NodeType = tryString "node-type" node |> Option.map toNodeType
              ExecutionState = tryString "execution-state" node |> Option.map toExecutionState
              ParentUid = parentUid
              Location = location
              Duration = tryNumber "time.duration-ms" node |> Option.map TimeSpan.FromMilliseconds
              Error = error
              StandardOutput = tryString "standardOutput" node
              StandardError = tryString "standardError" node
              Raw = node }

let toUpdateBatch
    (log: ClientLogLevel -> string -> unit)
    (e: Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdateEventArgs)
    : TestNodeUpdateBatch =
    { RunId = e.RunId
      Updates =
        e.Updates
        |> Seq.choose (fun u -> toUpdate log (Option.ofObj u.ParentUid) u.Node)
        |> List.ofSeq }

let toCapabilities (c: Microsoft.Testing.Platform.ServerMode.Client.MtpServerCapabilities) : ServerCapabilities =
    { ServerProcessId = Option.ofNullable c.ServerProcessId
      ServerName = Option.ofObj c.ServerName
      ServerVersion = Option.ofObj c.ServerVersion
      ProtocolVersion = Option.ofObj c.ProtocolVersion
      SupportsDiscovery = c.SupportsDiscovery
      MultiRequestSupport = c.MultiRequestSupport
      VSTestProviderSupport = c.VSTestProviderSupport
      SupportsAttachments = c.SupportsAttachments
      MultiConnectionProvider = c.MultiConnectionProvider }

let toAttachment (a: Microsoft.Testing.Platform.ServerMode.Client.MtpAttachment) : Attachment =
    { Uri = Option.ofObj a.Uri
      Producer = Option.ofObj a.Producer
      Type = Option.ofObj a.Type
      DisplayName = Option.ofObj a.DisplayName
      Description = Option.ofObj a.Description }

let toRunResult (r: Microsoft.Testing.Platform.ServerMode.Client.MtpRunResult) : RunResult =
    { Attachments = r.Attachments |> Seq.map toAttachment |> List.ofSeq }

let toLogLevel (level: Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel) : ClientLogLevel =
    match level with
    | Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Trace -> ClientLogLevel.Trace
    | Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Debug -> ClientLogLevel.Debug
    | Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Information -> ClientLogLevel.Information
    | Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Warning -> ClientLogLevel.Warning
    | _ -> ClientLogLevel.Error

let toLogMessage (e: Microsoft.Testing.Platform.ServerMode.Client.MtpLogEventArgs) : LogMessage =
    // MtpLogEventArgs carries the server's level as a string; read its real property names from
    // IMtpServerClient.cs in the NuGet cache. Map the string with the same spelling the wire uses.
    let level =
        match e.Level with
        | "Trace" -> ClientLogLevel.Trace
        | "Debug" -> ClientLogLevel.Debug
        | "Information" -> ClientLogLevel.Information
        | "Warning" -> ClientLogLevel.Warning
        | _ -> ClientLogLevel.Error
    { Level = level; Message = e.Message }

let toTelemetry (e: Microsoft.Testing.Platform.ServerMode.Client.MtpTelemetryEventArgs) : TelemetryEvent =
    { EventName = e.EventName; Metrics = e.Metrics }

let ofOptions (o: MtpClientOptions) : Microsoft.Testing.Platform.ServerMode.Client.MtpServerClientOptions =
    let upstream = Microsoft.Testing.Platform.ServerMode.Client.MtpServerClientOptions()
    upstream.ClientName <- o.ClientName
    upstream.ClientVersion <- o.ClientVersion
    upstream.SupportedProtocolVersions <- (List.toArray o.SupportedProtocolVersions :> IReadOnlyCollection<string>)
    upstream.DebuggerProvider <- o.DebuggerProvider
    upstream.IsStateful <- Option.toNullable o.IsStateful
    upstream.ConnectionTimeout <- o.ConnectionTimeout
    upstream.ServerShutdownTimeout <- o.ServerShutdownTimeout
    for KeyValue(key, value) in o.EnvironmentVariables do
        upstream.EnvironmentVariables[key] <- Option.toObj value
    upstream.Logger <-
        match o.Logger with
        | Some log ->
            Microsoft.Testing.Platform.ServerMode.Client.DelegateMtpClientLogger(fun level message -> log (toLogLevel level) message)
            :> Microsoft.Testing.Platform.ServerMode.Client.IMtpClientLogger
        | None -> null
    upstream

let translateException (e: exn) : exn =
    match e with
    | :? Microsoft.Testing.Platform.ServerMode.Client.MtpServerConnectionClosedException as x ->
        MtpConnectionClosedException(x.Message, x) :> exn
    | :? Microsoft.Testing.Platform.ServerMode.Client.MtpServerErrorException as x ->
        MtpProtocolErrorException(x.ErrorCode, x.Message, x) :> exn
    | :? Microsoft.Testing.Platform.ServerMode.Client.MtpServerClientException as x ->
        MtpClientException(x.Message, x) :> exn
    | other -> other
```

If `MtpLogEventArgs.Level` is an enum or has a different property name, adapt `toLogMessage`; the event's `Message` property name may also differ. Verify against the cached source before building.

- [ ] **Step 5: Run the tests**

Run: `rtk dotnet run --project tests/Partas.TestingPlatform.Client.Tests -c Debug`
Expected: all `Interop` tests pass.

- [ ] **Step 6: Commit**

```bash
rtk git add src/Partas.TestingPlatform.Client/Interop.fs src/Partas.TestingPlatform.Client.Protocol/InternalsVisibleTo.cs tests/Partas.TestingPlatform.Client.Tests Partas.Testing.slnx
rtk git commit -m "Map upstream server-mode client types to F# records

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: `MtpClient`

**Files:**
- Modify: `src/Partas.TestingPlatform.Client/MtpClient.fs`

**Interfaces:**
- Consumes: `Interop` from Task 3, types from Task 2.
- Produces: `Partas.TestingPlatform.Client.MtpClient` and `Partas.TestingPlatform.Client.ServerRequestHandler` exactly as in spec §4.1.

- [ ] **Step 1: Implement MtpClient.fs**

```fsharp
namespace Partas.TestingPlatform.Client

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

/// <summary>
/// Handles a server-to-client request such as <c>telemetry/update</c> or
/// <c>client/attachDebugger</c>. Receives the method name and its parameters, and returns the
/// result object.
/// </summary>
type ServerRequestHandler =
    string -> IReadOnlyDictionary<string, obj> option -> CancellationToken -> Task<IReadOnlyDictionary<string, obj> option>

/// <summary>
/// A client for a Microsoft.Testing.Platform application running in server mode. The client owns
/// the launched or hosted application for its lifetime.
/// </summary>
type MtpClient internal (inner: Microsoft.Testing.Platform.ServerMode.Client.IMtpServerClient, options: MtpClientOptions) =
    let log = options.Logger |> Option.defaultValue (fun _ _ -> ())
    let updates = Event<TestNodeUpdateBatch>()
    let logs = Event<LogMessage>()
    let telemetry = Event<TelemetryEvent>()
    let attachments = Event<Attachment list>()
    let mutable requestHandler: ServerRequestHandler option = None
    let mutable disposed = false

    let onUpdates =
        EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdateEventArgs>(fun _ e ->
            updates.Trigger(Interop.toUpdateBatch log e))
    let onLog =
        EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpLogEventArgs>(fun _ e ->
            logs.Trigger(Interop.toLogMessage e))
    let onTelemetry =
        EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpTelemetryEventArgs>(fun _ e ->
            telemetry.Trigger(Interop.toTelemetry e))
    let onAttachments =
        EventHandler<Microsoft.Testing.Platform.ServerMode.Client.MtpAttachmentsEventArgs>(fun _ e ->
            attachments.Trigger(e.Attachments |> Seq.map Interop.toAttachment |> List.ofSeq))

    do
        inner.TestNodesUpdated.AddHandler onUpdates
        inner.LogReceived.AddHandler onLog
        inner.TelemetryReceived.AddHandler onTelemetry
        inner.AttachmentsReceived.AddHandler onAttachments

    let guard (work: unit -> Task<'a>) : Task<'a> =
        task {
            try
                return! work ()
            with e ->
                return raise (Interop.translateException e)
        }

    let guardUnit (work: unit -> Task) : Task =
        task {
            try
                do! work ()
            with e ->
                return raise (Interop.translateException e)
        }
        :> Task

    static let launchCore (launch: unit -> Task<Microsoft.Testing.Platform.ServerMode.Client.MtpServerClient>) (options: MtpClientOptions) =
        task {
            try
                let! inner = launch ()
                return new MtpClient(inner, options)
            with e ->
                return raise (Interop.translateException e)
        }

    /// <summary>Launches <paramref name="source"/> as a child process and blocks until it connects.</summary>
    static member Launch(source: string, ?options: MtpClientOptions) : MtpClient =
        MtpClient.LaunchAsync(source, ?options = options).GetAwaiter().GetResult()

    /// <summary>
    /// Launches the test application at <paramref name="source"/> as a child process in server
    /// mode. <paramref name="source"/> is a managed <c>.dll</c> or a native executable.
    /// </summary>
    static member LaunchAsync(source: string, ?options: MtpClientOptions, ?cancellationToken: CancellationToken) : Task<MtpClient> =
        let options = defaultArg options MtpClientOptions.Default
        let token = defaultArg cancellationToken CancellationToken.None
        launchCore
            (fun () -> Microsoft.Testing.Platform.ServerMode.Client.MtpServerClient.LaunchAsync(source, Interop.ofOptions options, token))
            options

    /// <summary>
    /// Hosts the test application in the current process. <paramref name="run"/> receives the
    /// complete server-mode argument array and returns the application's exit code.
    /// </summary>
    static member LaunchInProcessAsync
        (run: string[] -> CancellationToken -> Task<int>, ?options: MtpClientOptions, ?cancellationToken: CancellationToken)
        : Task<MtpClient> =
        let options = defaultArg options MtpClientOptions.Default
        let token = defaultArg cancellationToken CancellationToken.None
        let entry = Func<string[], CancellationToken, Task<int>>(fun args ct -> run args ct)
        launchCore
            (fun () -> Microsoft.Testing.Platform.ServerMode.Client.MtpServerClient.LaunchInProcessAsync(entry, Interop.ofOptions options, token))
            options

    member _.ProcessId: int = inner.ProcessId
    member _.ServerExitCode: int option = Option.ofNullable inner.ServerExitCode
    member _.Capabilities: ServerCapabilities option = inner.Capabilities |> Option.ofObj |> Option.map Interop.toCapabilities

    /// <summary>Performs the <c>initialize</c> handshake. Must complete before any other request.</summary>
    member _.InitializeAsync(?cancellationToken: CancellationToken) : Task<ServerCapabilities> =
        let token = defaultArg cancellationToken CancellationToken.None
        guard (fun () ->
            task {
                let! c = inner.InitializeAsync token
                return Interop.toCapabilities c
            })

    member _.DiscoverTestsAsync(?cancellationToken: CancellationToken) : Task =
        let token = defaultArg cancellationToken CancellationToken.None
        guardUnit (fun () -> inner.DiscoverTestsAsync token)

    member _.DiscoverTestsAsync(uids: string seq, ?cancellationToken: CancellationToken) : Task =
        let token = defaultArg cancellationToken CancellationToken.None
        let list = List<string>(uids) :> IReadOnlyCollection<string>
        guardUnit (fun () -> inner.DiscoverTestsAsync(list, token))

    /// <summary>Discovers the nodes matching an MTP graph filter such as <c>/parser/**</c>.</summary>
    member _.DiscoverTestsWithFilterAsync(filter: string, ?cancellationToken: CancellationToken) : Task =
        let token = defaultArg cancellationToken CancellationToken.None
        guardUnit (fun () -> inner.DiscoverTestsWithFilterAsync(filter, token))

    member _.RunTestsAsync(?cancellationToken: CancellationToken) : Task<RunResult> =
        let token = defaultArg cancellationToken CancellationToken.None
        guard (fun () ->
            task {
                let! r = inner.RunTestsAsync token
                return Interop.toRunResult r
            })

    member _.RunTestsAsync(uids: string seq, ?cancellationToken: CancellationToken) : Task<RunResult> =
        let token = defaultArg cancellationToken CancellationToken.None
        let list = List<string>(uids) :> IReadOnlyCollection<string>
        guard (fun () ->
            task {
                let! r = inner.RunTestsAsync(list, token)
                return Interop.toRunResult r
            })

    /// <summary>Runs the nodes matching an MTP graph filter such as <c>/parser/**</c>.</summary>
    member _.RunTestsWithFilterAsync(filter: string, ?cancellationToken: CancellationToken) : Task<RunResult> =
        let token = defaultArg cancellationToken CancellationToken.None
        guard (fun () ->
            task {
                let! r = inner.RunTestsWithFilterAsync(filter, token)
                return Interop.toRunResult r
            })

    /// <summary>Sends the protocol <c>exit</c> notification.</summary>
    member _.ExitAsync(?cancellationToken: CancellationToken) : Task =
        let token = defaultArg cancellationToken CancellationToken.None
        guardUnit (fun () -> inner.ExitAsync token)

    /// <summary>Tears down the connection and the server without blocking. Idempotent.</summary>
    member _.ShutdownAsync() : Task = guardUnit (fun () -> inner.ShutdownAsync())

    [<CLIEvent>]
    member _.TestNodesUpdated: IEvent<TestNodeUpdateBatch> = updates.Publish
    [<CLIEvent>]
    member _.LogReceived: IEvent<LogMessage> = logs.Publish
    [<CLIEvent>]
    member _.TelemetryReceived: IEvent<TelemetryEvent> = telemetry.Publish
    [<CLIEvent>]
    member _.AttachmentsReceived: IEvent<Attachment list> = attachments.Publish

    member _.TestNodeUpdates: IObservable<TestNodeUpdateBatch> = updates.Publish :> _
    member _.Logs: IObservable<LogMessage> = logs.Publish :> _
    member _.Telemetry: IObservable<TelemetryEvent> = telemetry.Publish :> _
    member _.Attachments: IObservable<Attachment list> = attachments.Publish :> _

    member _.ServerRequestHandler
        with get () = requestHandler
        and set (value: ServerRequestHandler option) =
            requestHandler <- value
            inner.ServerRequestHandler <-
                match value with
                | None -> null
                | Some handler ->
                    Func<string, IDictionary<string, obj>, CancellationToken, Task<IDictionary<string, obj>>>(fun name parameters token ->
                        task {
                            let input =
                                parameters
                                |> Option.ofObj
                                |> Option.map (fun p -> Dictionary<string, obj>(p) :> IReadOnlyDictionary<string, obj>)
                            let! output = handler name input token
                            return
                                match output with
                                | Some o -> Dictionary<string, obj>(o) :> IDictionary<string, obj>
                                | None -> null
                        })

    interface IDisposable with
        /// <summary>Tears down synchronously. Prefer <c>ShutdownAsync</c> on UI or watchdog threads.</summary>
        member _.Dispose() =
            if not disposed then
                disposed <- true
                inner.TestNodesUpdated.RemoveHandler onUpdates
                inner.LogReceived.RemoveHandler onLog
                inner.TelemetryReceived.RemoveHandler onTelemetry
                inner.AttachmentsReceived.RemoveHandler onAttachments
                inner.Dispose()
```

Notes for the implementer:

- Upstream's `ServerRequestHandler` is typed over `IDictionary<string, object?>`. If the F# compiler rejects the `Func` conversion, construct the delegate with explicit type arguments matching the upstream property type as read from `IMtpServerClient.cs`.
- If `static let` inside a class with a primary constructor is rejected, move `launchCore` to a private module above the type: `module private MtpClientLaunch = let launchCore ...` and call it from the static members.
- `new MtpClient(...)` inside `launchCore` is allowed because the constructor is `internal`.

- [ ] **Step 2: Build**

Run: `rtk dotnet build src/Partas.TestingPlatform.Client -c Debug`
Expected: success, both TFMs. Fix compile errors against the real upstream names, keeping the public shape unchanged.

- [ ] **Step 3: Check the public surface has no upstream leaks**

Run:

```bash
rtk dotnet build src/Partas.TestingPlatform.Client -c Debug -f net10.0 -v q && rtk dotnet fsi --quiet --exec /dev/stdin <<'EOF'
let asm = System.Reflection.Assembly.LoadFrom "src/Partas.TestingPlatform.Client/bin/Debug/net10.0/Partas.TestingPlatform.Client.dll"
for t in asm.GetExportedTypes() do
    for m in t.GetMembers(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.DeclaredOnly) do
        let s = string m
        if s.Contains "Microsoft.Testing.Platform.ServerMode" then printfn "LEAK %s :: %s" t.FullName s
printfn "scan complete"
EOF
```

Expected: `scan complete` with no `LEAK` lines. If running fsi from stdin fails on this shell, save the script to the scratchpad directory and pass its path.

- [ ] **Step 4: Commit**

```bash
rtk git add src/Partas.TestingPlatform.Client/MtpClient.fs
rtk git commit -m "Add MtpClient over the upstream server-mode client

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Sample exposes its definition; test fixture

**Files:**
- Modify: `samples/Partas.TestingPlatform.Sample/Program.fs`
- Modify: `tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj`
- Create: `tests/Partas.TestingPlatform.Client.Tests/Fixture.fs`

**Interfaces:**
- Produces: `Partas.TestingPlatform.Sample.Program.definition : FrameworkDefinition<Body>`.
- Produces (module `Partas.TestingPlatform.Client.Tests.Fixture`):
  - `sampleLeafUids : string list` (four UIDs)
  - `options : MtpClientOptions`
  - `launchChild : unit -> Task<MtpClient>`
  - `launchInProcess : extraArgs:string list -> Task<MtpClient>`
  - `launchSlowInProcess : unit -> Task<MtpClient>`
  - `Collector` type with `Updates : TestNodeUpdate list` (in arrival order), `Attachments : Attachment list list`, and `Completed : Task` that resolves on the first batch with empty `Updates`.
  - `collect : MtpClient -> Collector`
  - `terminal : TestNodeUpdate -> bool`

- [ ] **Step 1: Expose the sample definition**

In `samples/Partas.TestingPlatform.Sample/Program.fs`, replace the `main` function with:

```fsharp
/// <summary>The sample framework, hostable in-process by a server-mode client.</summary>
let definition =
    testFramework<Body> {
        uid "Partas.TestingPlatform.Sample"
        version "0.1.0"
        displayName "Partas sample"
        tests (fun () -> suite)
        onRun runTests
        commandLineOptions [ myCustomFilterProvider ]
    }
    |> TrxReport.enable

[<EntryPoint>]
let main argv = definition |> TestApplication.run argv
```

Run: `rtk dotnet build samples/Partas.TestingPlatform.Sample -c Debug` and `rtk dotnet run --project tests/Partas.TestingPlatform.Trx.Tests -c Debug`
Expected: both succeed; the TRX end-to-end test still passes.

- [ ] **Step 2: Reference the sample and the binding from the test project**

Add to the test fsproj's `<ItemGroup>` of project references:

```xml
<ProjectReference Include="../../src/Partas.TestingPlatform/Partas.TestingPlatform.fsproj"/>
<!-- Referenced for two reasons: its definition is hosted in-process, and the reference copies
     its dll beside this project's output for the child-process launch path. -->
<ProjectReference Include="../../samples/Partas.TestingPlatform.Sample/Partas.TestingPlatform.Sample.fsproj"/>
```

Add `Fixture.fs` to `<Compile>` after `InteropTests.fs`.

- [ ] **Step 3: Fixture.fs**

```fsharp
module Partas.TestingPlatform.Client.Tests.Fixture

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Partas.TestingPlatform
open Partas.TestingPlatform.Client

let sampleDll = Path.Combine(AppContext.BaseDirectory, "Partas.TestingPlatform.Sample.dll")

let sampleLeafUids =
    [ "/parser/literals/parses an int"
      "/parser/literals/rejects a malformed int"
      "/parser/literals/parses a float"
      "/parser/reports the source span" ]

let options =
    { MtpClientOptions.Default with
        ConnectionTimeout = TimeSpan.FromSeconds 60.0
        ServerShutdownTimeout = TimeSpan.FromSeconds 20.0 }

let launchChild () : Task<MtpClient> =
    if not (File.Exists sampleDll) then
        failwithf "expected the sample dll beside the test output at %s" sampleDll
    MtpClient.LaunchAsync(sampleDll, options)

let private hostInProcess (definition: FrameworkDefinition<'T>) (extraArgs: string list) : Task<MtpClient> =
    let run (args: string[]) (_: CancellationToken) : Task<int> =
        Task.Run(fun () -> definition |> TestApplication.run (Array.append args (List.toArray extraArgs)))
    MtpClient.LaunchInProcessAsync(run, options)

let launchInProcess (extraArgs: string list) : Task<MtpClient> =
    hostInProcess Partas.TestingPlatform.Sample.Program.definition extraArgs

/// <summary>A suite with one leaf that waits for the run's cancellation token.</summary>
let slowDefinition =
    let suite: TestTree<unit -> Task<TestOutcome>> =
        Group("slow", None, [], [
            Leaf("waits for cancellation", None, [], fun () -> Task.FromResult Passed)
        ])
    let runTests (context: RunContext<unit -> Task<TestOutcome>>) : Task =
        task {
            for leaf in context.Leaves do
                let body _ =
                    task {
                        do! Task.Delay(TimeSpan.FromMinutes 5.0, context.CancellationToken)
                        return TestResult.create Passed
                    }
                let! _ = context.Reporter.Run(leaf, body, context.CancellationToken)
                ()
        }
    Builders.testFramework<unit -> Task<TestOutcome>> {
        uid "Partas.TestingPlatform.Client.Tests.Slow"
        version "0.1.0"
        displayName "Slow suite"
        tests (fun () -> suite)
        onRun runTests
    }

let launchSlowInProcess () : Task<MtpClient> = hostInProcess slowDefinition []

type Collector(client: MtpClient) =
    let updates = ResizeArray<TestNodeUpdate>()
    let attachments = ResizeArray<Attachment list>()
    let completed = TaskCompletionSource()
    do
        client.TestNodesUpdated.Add(fun batch ->
            lock updates (fun () -> updates.AddRange batch.Updates)
            if List.isEmpty batch.Updates then completed.TrySetResult() |> ignore)
        client.AttachmentsReceived.Add(fun a -> lock attachments (fun () -> attachments.Add a))

    member _.Updates: TestNodeUpdate list = lock updates (fun () -> List.ofSeq updates)
    member _.Attachments: Attachment list list = lock attachments (fun () -> List.ofSeq attachments)
    member _.Completed: Task = completed.Task

let collect (client: MtpClient) = Collector client

let terminal (u: TestNodeUpdate) =
    match u.ExecutionState with
    | Some ExecutionState.Passed
    | Some ExecutionState.Skipped
    | Some ExecutionState.Failed
    | Some ExecutionState.TimedOut
    | Some ExecutionState.Error
    | Some ExecutionState.Canceled -> true
    | _ -> false
```

If `Reporter.Run`'s body signature or `TestResult.create` differ from the sample's usage, copy the exact pattern from `samples/Partas.TestingPlatform.Sample/Program.fs`, which compiles against the same binding.

- [ ] **Step 4: Build**

Run: `rtk dotnet build tests/Partas.TestingPlatform.Client.Tests -c Debug`
Expected: success.

- [ ] **Step 5: Commit**

```bash
rtk git add samples/Partas.TestingPlatform.Sample/Program.fs tests/Partas.TestingPlatform.Client.Tests
rtk git commit -m "Expose the sample definition and add client test fixtures

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Child-process wire tests

**Files:**
- Create: `tests/Partas.TestingPlatform.Client.Tests/ChildProcessTests.fs`
- Modify: `tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj`

**Interfaces:**
- Consumes: `Fixture` from Task 5, `MtpClient` from Task 4.

Each test launches its own client and issues at most one discover or one run request, since the server may not advertise multi-request support. Tests are sequenced: they bind loopback ports and spawn processes.

- [ ] **Step 1: Write the tests**

```fsharp
module Partas.TestingPlatform.Client.Tests.ChildProcessTests

open System
open System.Threading.Tasks
open Expecto
open Partas.TestingPlatform.Client
open Partas.TestingPlatform.Client.Tests.Fixture

let private withClient (body: MtpClient -> Task<unit>) : Task<unit> =
    task {
        let! client = launchChild ()
        try
            do! body client
        finally
            client.ShutdownAsync().GetAwaiter().GetResult()
    }

let private awaitCompletion (collector: Collector) : Task<unit> =
    task {
        let! finished = Task.WhenAny(collector.Completed, Task.Delay(TimeSpan.FromSeconds 60.0))
        Expect.equal finished collector.Completed "the terminal empty batch arrived"
    }

[<Tests>]
let tests =
    testSequenced <| testList "child process" [
        testTask "initialize returns capabilities" {
            do! withClient (fun client -> task {
                let! caps = client.InitializeAsync()
                Expect.isTrue caps.SupportsDiscovery "supports discovery"
                Expect.isSome caps.ProtocolVersion "protocol version negotiated"
                Expect.equal client.Capabilities (Some caps) "cached on the client"
            })
        }

        testTask "discover-all publishes every leaf once, parents first" {
            do! withClient (fun client -> task {
                let collector = collect client
                let! _ = client.InitializeAsync()
                do! client.DiscoverTestsAsync()
                do! awaitCompletion collector
                let updates = collector.Updates
                let leaves = updates |> List.filter (fun u -> u.NodeType = Some NodeType.Action)
                Expect.equal (leaves |> List.map _.Uid |> List.sort) (List.sort sampleLeafUids) "every leaf exactly once"
                for leaf in leaves do
                    Expect.equal leaf.ExecutionState (Some ExecutionState.Discovered) leaf.Uid
                for i, u in List.indexed updates do
                    match u.ParentUid with
                    | Some parent ->
                        let earlier = updates |> List.take i |> List.exists (fun p -> p.Uid = parent)
                        Expect.isTrue earlier $"parent {parent} of {u.Uid} published earlier"
                    | None -> ()
            })
        }

        testTask "run-all publishes a terminal state for every leaf" {
            do! withClient (fun client -> task {
                let collector = collect client
                let! _ = client.InitializeAsync()
                let! result = client.RunTestsAsync()
                do! awaitCompletion collector
                let byUid =
                    collector.Updates
                    |> List.filter terminal
                    |> List.map (fun u -> u.Uid, u)
                    |> Map.ofList
                Expect.equal (byUid |> Map.keys |> List.ofSeq |> List.sort) (List.sort sampleLeafUids) "terminal for every leaf"
                Expect.equal byUid["/parser/literals/parses an int"].ExecutionState (Some ExecutionState.Passed) "passed"
                Expect.equal byUid["/parser/literals/parses a float"].ExecutionState (Some ExecutionState.Skipped) "skipped"
                let failed = byUid["/parser/literals/rejects a malformed int"]
                Expect.equal failed.ExecutionState (Some ExecutionState.Failed) "failed"
                Expect.stringContains (failed.Error |> Option.bind _.Message |> Option.defaultValue "") "unexpected 'x'" "error message"
                Expect.isSome byUid["/parser/literals/parses an int"].Duration "duration reported"
                Expect.isNotNull (box result) "run result returned"
            })
        }

        testTask "run by uid list executes only the named leaves" {
            do! withClient (fun client -> task {
                let collector = collect client
                let! _ = client.InitializeAsync()
                let! _ = client.RunTestsAsync([ "/parser/reports the source span" ])
                do! awaitCompletion collector
                let ran = collector.Updates |> List.filter terminal |> List.map _.Uid |> List.distinct
                Expect.equal ran [ "/parser/reports the source span" ] "only the named leaf"
            })
        }

        testTask "run with a graph filter executes only matching leaves" {
            do! withClient (fun client -> task {
                let collector = collect client
                let! _ = client.InitializeAsync()
                let! _ = client.RunTestsWithFilterAsync "/parser/literals/*"
                do! awaitCompletion collector
                let ran = collector.Updates |> List.filter terminal |> List.map _.Uid |> List.sort
                let expected = sampleLeafUids |> List.filter (fun u -> u.StartsWith "/parser/literals/") |> List.sort
                Expect.equal ran expected "literals only"
            })
        }

        testTask "exit then shutdown yields an exit code and dispose is idempotent" {
            let! client = launchChild ()
            let! _ = client.InitializeAsync()
            do! client.ExitAsync()
            do! client.ShutdownAsync()
            Expect.isSome client.ServerExitCode "exit code observed"
            (client :> IDisposable).Dispose()
            (client :> IDisposable).Dispose()
        }
    ]
```

Add `ChildProcessTests.fs` to `<Compile>` after `Fixture.fs`.

- [ ] **Step 2: Run**

Run: `rtk dotnet run --project tests/Partas.TestingPlatform.Client.Tests -c Debug -- --filter "child process"`
Expected: all six pass. If the filter syntax is rejected, run without the filter.

Failure guidance:
- Connection timeout: confirm `sampleDll` exists beside the test output. The project reference copies it.
- Discovery includes group nodes with `NodeType = Some NodeType.Group`; the leaf filter handles that.
- If the graph filter `/parser/literals/*` matches nothing, try `/parser/literals/**` and record the working syntax in the test name.
- If `ServerExitCode` is `None` after `ShutdownAsync`, wait up to five seconds polling it before asserting; upstream sets it when the process exits.

- [ ] **Step 3: Commit**

```bash
rtk git add tests/Partas.TestingPlatform.Client.Tests
rtk git commit -m "Cover the client over a child-process server

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: In-process wire tests, cancellation and attachments

**Files:**
- Create: `tests/Partas.TestingPlatform.Client.Tests/InProcessTests.fs`
- Modify: `tests/Partas.TestingPlatform.Client.Tests/Partas.TestingPlatform.Client.Tests.fsproj`

**Interfaces:**
- Consumes: `Fixture` from Task 5.

- [ ] **Step 1: Write the tests**

```fsharp
module Partas.TestingPlatform.Client.Tests.InProcessTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open Partas.TestingPlatform.Client
open Partas.TestingPlatform.Client.Tests.Fixture

let private awaitCompletion (collector: Collector) : Task<unit> =
    task {
        let! finished = Task.WhenAny(collector.Completed, Task.Delay(TimeSpan.FromSeconds 60.0))
        Expect.equal finished collector.Completed "the terminal empty batch arrived"
    }

[<Tests>]
let tests =
    testSequenced <| testList "in process" [
        testTask "initialize, discover and exit against the hosted sample" {
            let! client = launchInProcess []
            try
                let collector = collect client
                let! caps = client.InitializeAsync()
                Expect.isTrue caps.SupportsDiscovery "supports discovery"
                do! client.DiscoverTestsAsync()
                do! awaitCompletion collector
                let leaves = collector.Updates |> List.filter (fun u -> u.NodeType = Some NodeType.Action) |> List.map _.Uid |> List.sort
                Expect.equal leaves (List.sort sampleLeafUids) "every leaf"
                do! client.ExitAsync()
            finally
                client.ShutdownAsync().GetAwaiter().GetResult()
            Expect.isSome client.ServerExitCode "callback exit code observed"
        }

        testTask "run-all reports the same outcomes as the child process" {
            let! client = launchInProcess []
            try
                let collector = collect client
                let! _ = client.InitializeAsync()
                let! _ = client.RunTestsAsync()
                do! awaitCompletion collector
                let states =
                    collector.Updates |> List.filter terminal |> List.map (fun u -> u.Uid, u.ExecutionState) |> Map.ofList
                Expect.equal states["/parser/literals/parses an int"] (Some ExecutionState.Passed) "passed"
                Expect.equal states["/parser/literals/rejects a malformed int"] (Some ExecutionState.Failed) "failed"
                Expect.equal states["/parser/literals/parses a float"] (Some ExecutionState.Skipped) "skipped"
                do! client.ExitAsync()
            finally
                client.ShutdownAsync().GetAwaiter().GetResult()
        }

        testTask "TRX attachment arrives when the callback appends --report-trx" {
            let resultsDir = Path.Combine(Path.GetTempPath(), "partas-client-trx-" + Guid.NewGuid().ToString "N")
            Directory.CreateDirectory resultsDir |> ignore
            let! client = launchInProcess [ "--report-trx"; "--results-directory"; resultsDir ]
            try
                let collector = collect client
                let! _ = client.InitializeAsync()
                let! result = client.RunTestsAsync()
                do! awaitCompletion collector
                let fromEvent = collector.Attachments |> List.concat
                let all = fromEvent @ result.Attachments
                let isTrx (a: Attachment) =
                    a.Uri |> Option.exists (fun u -> u.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))
                Expect.isTrue (all |> List.exists isTrx) $"a .trx attachment among {all}"
                do! client.ExitAsync()
            finally
                client.ShutdownAsync().GetAwaiter().GetResult()
                try Directory.Delete(resultsDir, true) with _ -> ()
        }

        testTask "cancelling a run mid-flight completes the call and leaves exit usable" {
            let! client = launchSlowInProcess ()
            try
                let inProgress = TaskCompletionSource()
                client.TestNodesUpdated.Add(fun batch ->
                    if batch.Updates |> List.exists (fun u -> u.ExecutionState = Some ExecutionState.InProgress) then
                        inProgress.TrySetResult() |> ignore)
                let! _ = client.InitializeAsync()
                use cts = new CancellationTokenSource()
                let run = client.RunTestsAsync(cts.Token)
                let! started = Task.WhenAny(inProgress.Task, Task.Delay(TimeSpan.FromSeconds 30.0))
                Expect.equal started inProgress.Task "the slow leaf reported in-progress"
                cts.Cancel()
                let! finished = Task.WhenAny(run :> Task, Task.Delay(TimeSpan.FromSeconds 30.0))
                Expect.equal finished (run :> Task) "run call completed after cancel"
                // Either outcome is acceptable: a cancelled task, or a completed run whose leaf is Canceled/Skipped.
                Expect.isTrue (run.IsCanceled || run.IsFaulted || run.IsCompletedSuccessfully) "run call settled"
                do! client.ExitAsync()
            finally
                client.ShutdownAsync().GetAwaiter().GetResult()
        }
    ]
```

Add `InProcessTests.fs` to `<Compile>` after `ChildProcessTests.fs`.

- [ ] **Step 2: Run**

Run: `rtk dotnet run --project tests/Partas.TestingPlatform.Client.Tests -c Debug`
Expected: every test passes, including `Interop` and `child process`.

Failure guidance:
- If the attachment never arrives on the event and `result.Attachments` is empty, check `--results-directory` is the MTP flag name in this MTP version (`dotnet run --project samples/Partas.TestingPlatform.Sample -- --help`). Adjust the flag.
- If the cancellation test times out waiting for `InProgress`, confirm the binding publishes `InProgress` (DESIGN.md decision 24 says it always does) and that the slow leaf body is reached.
- If in-process hosting of two MTP applications sequentially in one process fails, the fault is in MTP static state. Record it and split the in-process tests into a separately launched process the same way `SessionTests.runTreeFailure` does in `tests/Partas.TestingPlatform.Tests/Main.fs`. Do not silently delete tests.

- [ ] **Step 3: Run the whole solution's tests**

Run: `rtk dotnet test Partas.Testing.slnx -c Debug -v q`
Expected: all projects pass, including the pre-existing suites.

- [ ] **Step 4: Commit**

```bash
rtk git add tests/Partas.TestingPlatform.Client.Tests
rtk git commit -m "Cover the client over an in-process server, cancellation and TRX attachments

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Documentation

**Files:**
- Create: `docs/content/guide/server-mode-client.md`
- Modify: `DESIGN.md` (§2 package table, §12)

- [ ] **Step 1: Write the guide**

Match the front matter and heading style of `docs/content/guide/session-lifecycle.md` (read its first 15 lines first and copy the front matter shape). Content:

```markdown
# Server-mode client

`Partas.TestingPlatform.Client` drives a Microsoft.Testing.Platform test application over the
server-mode JSON-RPC protocol. It launches the application, negotiates capabilities, and streams
discovery and execution updates as typed F# values.

## Packages

| Package | Contents |
|---|---|
| `Partas.TestingPlatform.Client` | `MtpClient` and the typed protocol records |
| `Partas.TestingPlatform.Client.Protocol` | the upstream client source, compiled; no public API |

The client carries no reference to `Microsoft.Testing.Platform`, so it drives applications built
on any MTP version whose server protocol is `1.0.0`.

## Driving a built test application

```fsharp
open Partas.TestingPlatform.Client

task {
    use client = MtpClient.Launch "path/to/MyTests.dll"
    client.TestNodesUpdated.Add(fun batch ->
        for update in batch.Updates do
            printfn "%s %A" update.Uid update.ExecutionState)

    let! capabilities = client.InitializeAsync()
    let! result = client.RunTestsAsync()
    do! client.ExitAsync()
    do! client.ShutdownAsync()
    printfn "exit code %A, %d attachments" client.ServerExitCode result.Attachments.Length
}
```

`InitializeAsync` must be the first request. Send `ExitAsync` for a graceful stop, then
`ShutdownAsync` to release the server. `Dispose` performs the same teardown synchronously.

## Hosting in-process

`LaunchInProcessAsync` runs the application in the current process. The callback receives the
complete server-mode argument array and must forward it unchanged, appending any extra options
after it:

```fsharp
let! client =
    MtpClient.LaunchInProcessAsync(fun args _ ->
        Task.Run(fun () -> definition |> TestApplication.run (Array.append args [| "--report-trx" |])))
```

## Updates

Every `testing/testUpdates/tests` notification arrives as a `TestNodeUpdateBatch`. A batch with an
empty `Updates` list marks the end of the request's stream. Each `TestNodeUpdate` exposes the
common properties as typed fields and keeps the full property dictionary in `Raw`.

## Errors

Failures surface as `MtpClientException`, `MtpConnectionClosedException` or
`MtpProtocolErrorException` (which carries the JSON-RPC error code). Cancelling the token passed to
a discover or run call sends `$/cancelRequest`.
```

- [ ] **Step 2: Update DESIGN.md**

In the §2 package table add two rows after `Partas.TestingPlatform.Trx`:

```markdown
| `Partas.TestingPlatform.Client.Protocol` | `Microsoft.Testing.Platform.ServerMode.Client.Sources` (exact) | the upstream server-mode client, compiled; no public API |
| `Partas.TestingPlatform.Client` | `Partas.TestingPlatform.Client.Protocol` | F# server-mode client |
```

In §12, change the sentence

```
`tests/Partas.TestingPlatform.ProtocolTests` drives the framework over the real server
protocol using `Microsoft.Testing.Platform.ServerMode.Client.Sources`.
```

to

```
`tests/Partas.TestingPlatform.ProtocolTests` drives the framework over the real server
protocol using `Partas.TestingPlatform.Client` (design: `docs/superpowers/specs/2026-09-16-servermode-client-design.md`).
```

Add to the decision log:

```markdown
| 28 | server-mode client shipped as an F# package over a C# shim; one `Interop` module is the glue |
```

- [ ] **Step 3: Build docs if the docs project builds locally**

Run: `rtk dotnet build docs/docs.fsproj -c Debug`
Expected: success. If the docs project requires tooling not present, note that in the commit message and move on.

- [ ] **Step 4: Commit**

```bash
rtk git add docs/content/guide/server-mode-client.md DESIGN.md
rtk git commit -m "Document the server-mode client

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review

- Spec §3 packages: Tasks 1, 2. `MtpVersion`: Task 1.
- Spec §4.1 surface: Task 4. §4.2 options and §4.3 payloads and §4.4 exceptions: Task 2. §4.5 Interop: Task 3.
- Spec §5 lifecycle: exercised by Task 6 exit test and Task 7 cancellation test.
- Spec §7 tests 1–7: Tasks 6 and 7. Test 8 attachments: Task 7. Test 9 dropped uid: Task 3.
- Names used across tasks: `Interop.toUpdate`, `toUpdateBatch`, `toCapabilities`, `toRunResult`, `toAttachment`, `toLogMessage`, `toTelemetry`, `toLogLevel`, `ofOptions`, `translateException` match between Tasks 3 and 4. `Fixture.launchChild`, `launchInProcess`, `launchSlowInProcess`, `collect`, `terminal`, `sampleLeafUids`, `Collector.Completed` match between Tasks 5, 6, 7.
