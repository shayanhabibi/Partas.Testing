namespace Partas.TestingPlatform.Trx

open Microsoft.Testing.Extensions
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
    /// A <c>TrxFullyQualifiedTypeNameProperty</c> carrying the group holding the leaf, without
    /// its leading separator, as the type name TRX groups results under. A root-level leaf
    /// carries an empty type name.
    /// </summary>
    let fullyQualifiedTypeName (leaf: ExecutableLeaf<'T>) : IProperty =
        let group = leaf.Parent |> Option.defaultValue ""
        TrxFullyQualifiedTypeNameProperty(group.TrimStart TestTree.Separator) :> IProperty

    /// <summary>
    /// Adds the TRX capability and registers <c>Microsoft.Testing.Extensions.TrxReport</c>'s
    /// writer with the builder, so <c>--report-trx</c> both parses and produces a report.
    /// </summary>
    let enable (definition: FrameworkDefinition<'T>) : FrameworkDefinition<'T> =
        { definition with
            Capabilities = definition.Capabilities @ [ capability ]
            BuilderExtensions =
                definition.BuilderExtensions
                @ [ BuilderExtension.create (fun builder -> builder.AddTrxReportProvider() |> ignore) ] }
