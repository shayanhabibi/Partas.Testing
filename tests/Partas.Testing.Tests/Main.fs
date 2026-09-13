module Partas.Testing.Tests.Main

open System.Threading
open Expecto

[<EntryPoint>]
let main argv =
    // Expecto runs its own tests concurrently and each one blocks its worker on the inner
    // runner, so a suite's leaves need workers beyond the pool's default floor. Raising the
    // floor hands them over immediately rather than at the injection rate.
    let workers, completionPorts = ThreadPool.GetMinThreads()
    ThreadPool.SetMinThreads(max workers 64, completionPorts) |> ignore

    runTestsInAssemblyWithCLIArgs [] argv
