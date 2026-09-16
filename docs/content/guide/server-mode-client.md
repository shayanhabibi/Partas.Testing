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

For a child-process server, `ExitAsync` waits up to `ServerShutdownTimeout` for the process to
exit, so `ServerExitCode` is available as soon as `ExitAsync` returns. For an in-process host,
`ServerExitCode` becomes available only after `ShutdownAsync`.

## Hosting in-process

`LaunchInProcessAsync` runs the application in the current process. The callback receives the
complete server-mode argument array and must forward it unchanged; extra options may be appended
after the server arguments:

```fsharp
let! client =
    MtpClient.LaunchInProcessAsync(fun args _ ->
        Task.Run(fun () -> definition |> TestApplication.run args))
```

Options that register a test-host controller, such as `--report-trx`, cannot be appended here:
the platform relaunches the in-process host to apply them, a relaunch neither launch path exposes
to the client, so TRX attachments are not reachable through the client against upstream 2.4.0.

## Updates

Every `testing/testUpdates/tests` notification arrives as a `TestNodeUpdateBatch`. When a
discover or run call completes, its updates have already been delivered to handlers. Each
`TestNodeUpdate` exposes the common properties as typed fields and keeps the full property
dictionary in `Raw`.

## Errors

Failures surface as `MtpClientException`, `MtpConnectionClosedException` or
`MtpProtocolErrorException` (which carries the JSON-RPC error code). Cancelling the token passed to
a discover or run call sends `$/cancelRequest`. The cancelled call then raises
`OperationCanceledException` (or `TaskCanceledException`) unchanged; cancellation is never
wrapped in `MtpClientException`.
