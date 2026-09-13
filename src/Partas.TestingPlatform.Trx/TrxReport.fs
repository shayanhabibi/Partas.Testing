namespace Partas.TestingPlatform.Trx

open Microsoft.Testing.Extensions.TrxReport.Abstractions
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.Extensions.Messages
open Partas.TestingPlatform

/// <summary>Enables TRX reporting for the current session.</summary>
type TrxReportCapability() =
    interface ITrxReportCapability with
        member _.IsSupported = true
        member _.Enable() = ()

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TrxReport =

    /// <summary>A capability factory for <c>FrameworkDefinition.Capabilities</c>.</summary>
    let capability () : ITestFrameworkCapability = TrxReportCapability() :> ITestFrameworkCapability

    /// <summary>
    /// A <c>TrxFullyQualifiedTypeNameProperty</c> carrying the node's own uid, without its
    /// leading separator, as the type name TRX groups results under.
    /// </summary>
    let fullyQualifiedTypeName (node: ResolvedNode) : IProperty =
        TrxFullyQualifiedTypeNameProperty(node.Uid.TrimStart TestTree.Separator) :> IProperty
