module Partas.TestingPlatform.Tests.FrameworkTests

#nowarn "57"

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
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
            Expect.isEmpty
                (FrameworkDefinition.empty<int> |> FrameworkDefinition.commandLineOptionsProviders)
                "no providers by default"
        }

        test "the CE operation records the given providers" {
            let factory () = StubProvider() :> ICommandLineOptionsProvider

            let definition =
                testFramework<int> {
                    uid "x"
                    commandLineOptions [ factory ]
                }

            let providers = definition |> FrameworkDefinition.commandLineOptionsProviders

            Expect.equal providers.Length 1 "one provider recorded"

            Expect.isTrue
                (obj.ReferenceEquals(providers.[0], factory))
                "same factory retained"
        }
    ]

[<Tests>]
let capabilitiesTests =
    testList "FrameworkDefinition.capabilities" [
        test "an empty definition carries no extra capabilities" {
            Expect.isEmpty (FrameworkDefinition.empty<int> |> FrameworkDefinition.capabilities) "no capabilities by default"
        }

        test "the CE operation records the given capability factories" {
            let factory () = StubCapability() :> ITestFrameworkCapability

            let definition =
                testFramework<int> {
                    uid "x"
                    capabilities [ factory ]
                }

            let capabilities = definition |> FrameworkDefinition.capabilities

            Expect.equal capabilities.Length 1 "one factory recorded"

            Expect.isTrue
                (obj.ReferenceEquals(capabilities.[0], factory))
                "same factory retained"
        }

        test "FrameworkCapabilities exposes declared capabilities alongside the banner" {
            let stub = StubCapability()

            let definition =
                testFramework<int> {
                    uid "x"
                    banner "hi"
                    capabilities [ fun () -> stub :> ITestFrameworkCapability ]
                }

            let capabilities = (FrameworkCapabilities definition :> ITestFrameworkCapabilities).Capabilities

            Expect.equal capabilities.Count 2 "banner plus declared capability"
            Expect.isTrue (capabilities |> Seq.exists (fun c -> c :? IBannerMessageOwnerCapability)) "banner present"
            Expect.isTrue (capabilities |> Seq.exists (fun c -> obj.ReferenceEquals(c, stub))) "declared capability present"
        }

        test "FrameworkCapabilities omits the banner capability when none is declared" {
            let stub = StubCapability()

            let definition =
                testFramework<int> {
                    uid "x"
                    capabilities [ fun () -> stub :> ITestFrameworkCapability ]
                }

            let capabilities = (FrameworkCapabilities definition :> ITestFrameworkCapabilities).Capabilities

            Expect.equal capabilities.Count 1 "only the declared capability"
            Expect.isTrue (obj.ReferenceEquals(Seq.head capabilities, stub)) "declared capability present"
        }

        test "a capability factory is invoked once, not on every read of Capabilities" {
            let mutable invocations = 0

            let factory () =
                invocations <- invocations + 1
                StubCapability() :> ITestFrameworkCapability

            let definition =
                testFramework<int> {
                    uid "x"
                    capabilities [ factory ]
                }

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
            Expect.isEmpty
                (FrameworkDefinition.empty<int> |> FrameworkDefinition.builderExtensions)
                "no builder extensions by default"
        }
    ]
// The internal BuilderExtensions.set/.run mechanism is exercised from
// Partas.TestingPlatform.Trx.Tests, the only assembly (besides Partas.TestingPlatform.Trx
// itself) this binding grants InternalsVisibleTo — see AssemblyInfo.fs. This project carries
// no such grant, by design, so it cannot construct a BuilderExtension to test with.
