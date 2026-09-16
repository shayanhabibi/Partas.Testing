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
            Expect.isTrue upstream.IsStateful "stateful"
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

        testCase "toUpdateBatch maps RunId, drops nodes without uid, and logs a warning" <| fun () ->
            let logged = ResizeArray<ClientLogLevel * string>()
            let log level message = logged.Add((level, message))
            let runId = Guid.NewGuid()
            let withUid =
                Dictionary<string, obj>(dict [ "uid", box "/a" ]) :> IDictionary<string, obj>
            let withoutUid =
                Dictionary<string, obj>(dict [ "display-name", box "orphan" ]) :> IDictionary<string, obj>
            let updates =
                [| Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdate(withUid, "/parent")
                   Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdate(withoutUid, null) |]
                :> IReadOnlyList<Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdate>
            let args = Microsoft.Testing.Platform.ServerMode.Client.MtpTestNodeUpdateEventArgs(runId, updates)
            let batch = Interop.toUpdateBatch log args
            Expect.equal batch.RunId runId "run id"
            Expect.equal batch.Updates.Length 1 "one update kept"
            Expect.equal batch.Updates.[0].ParentUid (Some "/parent") "parent uid preserved"
            Expect.equal logged.Count 1 "one warning logged"
            Expect.equal (fst logged[0]) ClientLogLevel.Warning "level"

        testCase "toCapabilities maps every field when populated" <| fun () ->
            let upstream =
                Microsoft.Testing.Platform.ServerMode.Client.MtpServerCapabilities(
                    Nullable 42,
                    "srv",
                    "1.2.3",
                    true,
                    true,
                    true,
                    true,
                    true,
                    "2.0")
            let capabilities = Interop.toCapabilities upstream
            Expect.equal capabilities.ServerProcessId (Some 42) "server process id"
            Expect.equal capabilities.ServerName (Some "srv") "server name"
            Expect.equal capabilities.ServerVersion (Some "1.2.3") "server version"
            Expect.equal capabilities.ProtocolVersion (Some "2.0") "protocol version"
            Expect.isTrue capabilities.SupportsDiscovery "supports discovery"
            Expect.isTrue capabilities.MultiRequestSupport "multi request support"
            Expect.isTrue capabilities.VSTestProviderSupport "vstest provider support"
            Expect.isTrue capabilities.SupportsAttachments "supports attachments"
            Expect.isTrue capabilities.MultiConnectionProvider "multi connection provider"

        testCase "toCapabilities maps absent optional fields to None" <| fun () ->
            let upstream =
                Microsoft.Testing.Platform.ServerMode.Client.MtpServerCapabilities(
                    Nullable(),
                    null,
                    null,
                    false,
                    false,
                    false,
                    false,
                    false)
            let capabilities = Interop.toCapabilities upstream
            Expect.equal capabilities.ServerProcessId None "server process id"
            Expect.equal capabilities.ServerName None "server name"
            Expect.equal capabilities.ServerVersion None "server version"
            Expect.equal capabilities.ProtocolVersion None "protocol version"

        testCase "toRunResult and toAttachment map a fully populated attachment" <| fun () ->
            let attachment =
                Microsoft.Testing.Platform.ServerMode.Client.MtpAttachment("file:///x.trx", "trx", "type", "name", "desc")
            let upstream =
                Microsoft.Testing.Platform.ServerMode.Client.MtpRunResult([| attachment |])
            let result = Interop.toRunResult upstream
            Expect.equal result.Attachments.Length 1 "one attachment"
            let mapped = result.Attachments.[0]
            Expect.equal mapped.Uri (Some "file:///x.trx") "uri"
            Expect.equal mapped.Producer (Some "trx") "producer"
            Expect.equal mapped.Type (Some "type") "type"
            Expect.equal mapped.DisplayName (Some "name") "display name"
            Expect.equal mapped.Description (Some "desc") "description"

        testCase "toAttachment maps a fully absent attachment to None fields" <| fun () ->
            let attachment =
                Microsoft.Testing.Platform.ServerMode.Client.MtpAttachment(null, null, null, null, null)
            let mapped = Interop.toAttachment attachment
            Expect.equal mapped.Uri None "uri"
            Expect.equal mapped.Producer None "producer"
            Expect.equal mapped.Type None "type"
            Expect.equal mapped.DisplayName None "display name"
            Expect.equal mapped.Description None "description"

        testCase "toLogLevel maps every upstream level" <| fun () ->
            let cases =
                [ Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Trace, ClientLogLevel.Trace
                  Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Debug, ClientLogLevel.Debug
                  Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Information, ClientLogLevel.Information
                  Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Warning, ClientLogLevel.Warning
                  Microsoft.Testing.Platform.ServerMode.Client.MtpClientLogLevel.Error, ClientLogLevel.Error ]
            for wire, expected in cases do
                Expect.equal (Interop.toLogLevel wire) expected (string wire)

        testCase "toLogMessage maps a known level and defaults an unknown one to Error" <| fun () ->
            let known = Microsoft.Testing.Platform.ServerMode.Client.MtpLogEventArgs("Warning", "m")
            let message = Interop.toLogMessage known
            Expect.equal message { Level = ClientLogLevel.Warning; Message = "m" } "known level"
            let unknown = Microsoft.Testing.Platform.ServerMode.Client.MtpLogEventArgs("Bogus", "m2")
            let mapped = Interop.toLogMessage unknown
            Expect.equal mapped { Level = ClientLogLevel.Error; Message = "m2" } "unknown level defaults to error"

        testCase "toTelemetry maps EventName and passes Metrics through" <| fun () ->
            let metrics =
                Dictionary<string, obj>(dict [ "k", box 1 ]) :> IReadOnlyDictionary<string, obj>
            let upstream = Microsoft.Testing.Platform.ServerMode.Client.MtpTelemetryEventArgs("evt", metrics)
            let telemetry = Interop.toTelemetry upstream
            Expect.equal telemetry.EventName "evt" "event name"
            Expect.isTrue (obj.ReferenceEquals(telemetry.Metrics, metrics)) "metrics reference passed through"

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
