module internal Partas.TestingPlatform.AssemblyInfo

open System.Runtime.CompilerServices

// FrameworkDefinition's representation and BuilderExtension's case constructor are internal:
// the latter reaches a builder that can defer a registered action's execution into the running
// session (see Framework.fs). Granted to the shipped TRX companion only — neither this
// binding's own unit tests (Partas.TestingPlatform.Tests) nor the TRX companion's own test
// project need a grant from here directly; Partas.TestingPlatform.Trx.Tests reads and builds
// FrameworkDefinition values through the public CE and the read accessors in the
// FrameworkDefinition module instead. InternalsVisibleTo without a matching strong name is a
// compile-time speed bump against accident, not a boundary against intent: any assembly
// compiled with a matching AssemblyName can forge the grant. Strong-naming this assembly would
// close that gap but changes the shipped package's identity for every consumer.
[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Trx")>]
do ()
