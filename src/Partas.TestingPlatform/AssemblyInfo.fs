module internal Partas.TestingPlatform.AssemblyInfo

open System.Runtime.CompilerServices

// BuilderExtension's case constructor is internal: it reaches a builder that can defer a
// registered action's execution into the running session (see Framework.fs). Granted to the
// shipped TRX companion and its own test project only — this binding's own unit tests
// (Partas.TestingPlatform.Tests) get no grant, so this list stays as short as the set of
// assemblies that legitimately need it. InternalsVisibleTo without a matching strong name is
// a compile-time speed bump against accident, not a boundary against intent: any assembly
// compiled with a matching AssemblyName can forge the grant. Strong-naming this assembly would
// close that gap but changes the shipped package's identity for every consumer, so it is left
// as the project owner's decision rather than applied here.
[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Trx")>]
[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Trx.Tests")>]
do ()
