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
    /// A <c>TrxFullyQualifiedTypeNameProperty</c> carrying the group holding the leaf, as a
    /// human-facing path, as the type name TRX groups results under. A root-level leaf carries
    /// an empty type name.
    /// </summary>
    let fullyQualifiedTypeName (leaf: ExecutableLeaf<'T>) : IProperty =
        let group = leaf.Parent |> Option.defaultValue ""
        TrxFullyQualifiedTypeNameProperty(group.TrimStart TestTree.Separator |> TestTree.unescape) :> IProperty

    /// <summary>
    /// Adds the TRX capability, puts <c>fullyQualifiedTypeName</c> on every reported outcome, and
    /// registers <c>Microsoft.Testing.Extensions.TrxReport</c>'s writer with the builder, so
    /// <c>--report-trx</c> parses, and produces a report, for any framework built on the binding.
    /// The writer raises on a result carrying no grouping name, so <c>enable</c> supplies one for
    /// every framework rather than leaving it to each execution walk.
    /// </summary>
    let enable (definition: FrameworkDefinition<'T>) : FrameworkDefinition<'T> =
        { definition with
            Capabilities = definition.Capabilities @ [ capability ]
            BuilderExtensions =
                definition.BuilderExtensions
                @ [ BuilderExtension.create _.AddTrxReportProvider()] }
        |> FrameworkDefinition.addLeafProperties (fun leaf -> [ fullyQualifiedTypeName leaf ])
