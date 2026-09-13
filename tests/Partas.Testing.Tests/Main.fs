module Partas.Testing.Tests.Main

open Expecto

// The thread pool floor this suite needs is raised in Fakes.fs, on the path every nested run
// takes: the test SDK's adapter hosts this assembly without calling this entry point.
[<EntryPoint>]
let main argv = runTestsInAssemblyWithCLIArgs [] argv
