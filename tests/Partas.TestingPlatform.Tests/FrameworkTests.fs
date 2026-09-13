module Partas.TestingPlatform.Tests.FrameworkTests

#nowarn "57"

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Microsoft.Testing.Platform.Builder
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.CommandLine
open Microsoft.Testing.Platform.Extensions.TestFramework
open Partas.TestingPlatform

type private StubProvider() =
    interface IExtension with
        member _.Uid = "stub"
        member _.Version = "1.0.0"
        member _.DisplayName = "stub"
        member _.Description = "stub"
        member _.IsEnabledAsync() = Task.FromResult true

    interface ICommandLineOptionsProvider with
        member _.GetCommandLineOptions() =
            List.empty<CommandLineOption> :> IReadOnlyCollection<CommandLineOption>

        member _.ValidateOptionArgumentsAsync(_, _) = ValidationResult.ValidTask
        member _.ValidateCommandLineOptionsAsync(_) = ValidationResult.ValidTask

type private StubCapability() =
    interface ITestFrameworkCapability

[<Tests>]
let tests =
    testList "FrameworkDefinition.commandLineOptions" [
        test "an empty definition carries no command line providers" {
            Expect.isEmpty FrameworkDefinition.empty<int>.CommandLineOptionsProviders "no providers by default"
        }

        test "the CE operation records the given providers" {
            let factory () = StubProvider() :> ICommandLineOptionsProvider

            let definition =
                testFramework<int> {
                    uid "x"
                    commandLineOptions [ factory ]
                }

            Expect.equal definition.CommandLineOptionsProviders.Length 1 "one provider recorded"

            Expect.isTrue
                (obj.ReferenceEquals(definition.CommandLineOptionsProviders.[0], factory))
                "same factory retained"
        }
    ]

[<Tests>]
let capabilitiesTests =
    testList "FrameworkDefinition.capabilities" [
        test "an empty definition carries no extra capabilities" {
            Expect.isEmpty FrameworkDefinition.empty<int>.Capabilities "no capabilities by default"
        }

        test "the CE operation records the given capability factories" {
            let factory () = StubCapability() :> ITestFrameworkCapability

            let definition =
                testFramework<int> {
                    uid "x"
                    capabilities [ factory ]
                }

            Expect.equal definition.Capabilities.Length 1 "one factory recorded"

            Expect.isTrue
                (obj.ReferenceEquals(definition.Capabilities.[0], factory))
                "same factory retained"
        }

        test "FrameworkCapabilities exposes declared capabilities alongside the banner" {
            let stub = StubCapability()

            let definition =
                { FrameworkDefinition.empty<int> with
                    Banner = Some "hi"
                    Capabilities = [ fun () -> stub :> ITestFrameworkCapability ] }

            let capabilities = (FrameworkCapabilities definition :> ITestFrameworkCapabilities).Capabilities

            Expect.equal capabilities.Count 2 "banner plus declared capability"
            Expect.isTrue (capabilities |> Seq.exists (fun c -> c :? IBannerMessageOwnerCapability)) "banner present"
            Expect.isTrue (capabilities |> Seq.exists (fun c -> obj.ReferenceEquals(c, stub))) "declared capability present"
        }

        test "FrameworkCapabilities omits the banner capability when none is declared" {
            let stub = StubCapability()

            let definition =
                { FrameworkDefinition.empty<int> with
                    Capabilities = [ fun () -> stub :> ITestFrameworkCapability ] }

            let capabilities = (FrameworkCapabilities definition :> ITestFrameworkCapabilities).Capabilities

            Expect.equal capabilities.Count 1 "only the declared capability"
            Expect.isTrue (obj.ReferenceEquals(Seq.head capabilities, stub)) "declared capability present"
        }

        test "a capability factory is invoked once, not on every read of Capabilities" {
            let mutable invocations = 0

            let factory () =
                invocations <- invocations + 1
                StubCapability() :> ITestFrameworkCapability

            let definition = { FrameworkDefinition.empty<int> with Capabilities = [ factory ] }
            let capabilities = FrameworkCapabilities definition :> ITestFrameworkCapabilities

            capabilities.Capabilities |> ignore
            capabilities.Capabilities |> ignore

            Expect.equal invocations 1 "factory invoked once despite two reads"
        }
    ]

[<Tests>]
let builderExtensionsTests =
    testList "FrameworkDefinition.BuilderExtensions" [
        test "an empty definition carries no builder extensions" {
            Expect.isEmpty FrameworkDefinition.empty<int>.BuilderExtensions "no builder extensions by default"
        }

        test "BuilderExtensions.set records the given actions" {
            let extension = BuilderExtension.create (fun _ -> ())
            let definition = FrameworkDefinition.empty<int> |> BuilderExtensions.set [ extension ]

            Expect.equal definition.BuilderExtensions.Length 1 "one action recorded"
        }

        test "BuilderExtensions.run invokes every declared action" {
            let mutable calls = 0
            let extension = BuilderExtension.create (fun _ -> calls <- calls + 1)

            let definition =
                FrameworkDefinition.empty<int> |> BuilderExtensions.set [ extension; extension ]

            definition |> BuilderExtensions.run Unchecked.defaultof<ITestApplicationBuilder>

            Expect.equal calls 2 "both registered actions ran"
        }

        test "BuilderExtensions.run passes the given builder through to each action" {
            let builder = Unchecked.defaultof<ITestApplicationBuilder>
            let mutable seen = ValueNone
            let extension = BuilderExtension.create (fun b -> seen <- ValueSome b)
            let definition = FrameworkDefinition.empty<int> |> BuilderExtensions.set [ extension ]

            definition |> BuilderExtensions.run builder

            match seen with
            | ValueSome received -> Expect.isTrue (obj.ReferenceEquals(received, builder)) "same builder reference reaches the action"
            | ValueNone -> failtest "the action never ran"
        }
    ]
