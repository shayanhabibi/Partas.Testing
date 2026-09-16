module Partas.TestingPlatform.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    match argv with
    | [| "--session-tree-failure" |] -> SessionTests.runTreeFailure ()
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
