module internal Partas.TestingPlatform.AssemblyInfo

open System.Runtime.CompilerServices

// FrameworkDefinition.BuilderExtensions is internal: it reaches a builder that can defer a
// registered action's execution into the running session (see Framework.fs), so only this
// binding's own TRX package and its tests may set it.
[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Trx")>]
[<assembly: InternalsVisibleTo("Partas.TestingPlatform.Tests")>]
do ()
