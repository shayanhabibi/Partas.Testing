module internal Partas.TestingPlatform.Trx.AssemblyInfo

open System.Runtime.CompilerServices

// Grants this package's own test project access to any internal member declared in this
// assembly. Partas.TestingPlatform's internals are not reachable through this grant —
// InternalsVisibleTo names an assembly, not a transitive project reference — so
// Partas.TestingPlatform.Trx.Tests builds and reads FrameworkDefinition values through the
// public CE and the read accessors in the FrameworkDefinition module, the same as any other
// consumer.
[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Trx.Tests")>]
do ()
