module Partas.TestingPlatform.Tests.FrameworkTests

#nowarn "57"

open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.CommandLine
open Microsoft.Testing.Platform.Extensions.Messages
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

        test "adding a provider keeps the ones already declared, in declaration order" {
            let first () = StubProvider() :> ICommandLineOptionsProvider
            let second () = StubProvider() :> ICommandLineOptionsProvider

            let providers =
                testFramework<int> {
                    uid "x"
                    commandLineOptions [ first ]
                }
                |> FrameworkDefinition.addCommandLineOptionsProvider second
                |> FrameworkDefinition.commandLineOptionsProviders

            Expect.equal providers.Length 2 "both factories declared"
            Expect.isTrue (obj.ReferenceEquals(providers.[0], first)) "the CE-declared factory comes first"
            Expect.isTrue (obj.ReferenceEquals(providers.[1], second)) "the added factory comes second"
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

            Expect.equal capabilities.Count 3 "graceful stop, banner, declared capability"
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

            Expect.equal capabilities.Count 2 "graceful stop and the declared capability"
            Expect.isFalse
                (capabilities |> Seq.exists (fun c -> c :? IBannerMessageOwnerCapability))
                "no banner capability"

            Expect.isTrue (capabilities |> Seq.exists (fun c -> obj.ReferenceEquals(c, stub))) "declared capability present"
        }

        test "adding a capability keeps the ones already declared, in declaration order" {
            let first () = StubCapability() :> ITestFrameworkCapability
            let second () = StubCapability() :> ITestFrameworkCapability

            let declared =
                testFramework<int> {
                    uid "x"
                    capabilities [ first ]
                }
                |> FrameworkDefinition.addCapability second
                |> FrameworkDefinition.capabilities

            Expect.equal declared.Length 2 "both factories declared"
            Expect.isTrue (obj.ReferenceEquals(declared.[0], first)) "the CE-declared factory comes first"
            Expect.isTrue (obj.ReferenceEquals(declared.[1], second)) "the added factory comes second"
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

type private Marker(text: string) =
    member _.Text = text
    interface IProperty

[<Tests>]
let leafPropertyTests =
    let leaf: ExecutableLeaf<unit> =
        { Node = { Uid = "/g/a"; Name = "a"; Location = None; Properties = [] }
          Parent = Some "/g"
          Payload = () }

    let textsOf (definition: FrameworkDefinition<int>) =
        definition |> FrameworkDefinition.leafProperties |> (fun contribute -> contribute leaf)
        |> List.map (fun property -> (property :?> Marker).Text)

    testList "FrameworkDefinition.LeafProperties" [
        test "an empty definition contributes nothing" {
            Expect.isEmpty (textsOf FrameworkDefinition.empty<int>) "no properties by default"
        }

        test "a contribution reads the leaf it is given" {
            let definition =
                FrameworkDefinition.empty<int>
                |> FrameworkDefinition.addLeafProperties (fun leaf -> [ Marker leaf.Node.Uid ])

            Expect.sequenceEqual (textsOf definition) [ "/g/a" ] "the uid the contribution saw"
        }

        test "a second contribution follows the first, rather than replacing it" {
            let definition =
                FrameworkDefinition.empty<int>
                |> FrameworkDefinition.addLeafProperties (fun _ -> [ Marker "first" ])
                |> FrameworkDefinition.addLeafProperties (fun _ -> [ Marker "second" ])

            Expect.sequenceEqual (textsOf definition) [ "first"; "second" ] "both, in declaration order"
        }
    ]

[<Tests>]
let gracefulStopTests =
    let capabilityOf (stop: GracefulStop) =
        (FrameworkCapabilities(FrameworkDefinition.empty<int>, stop) :> ITestFrameworkCapabilities).Capabilities
        |> Seq.pick (function
            | :? IGracefulStopTestExecutionResultCapability as capability -> Some capability
            | _ -> None)

    testList "GracefulStop" [
        test "a fresh latch is unset" {
            Expect.isFalse (GracefulStop()).IsRequested "nothing has asked for a stop"
        }

        test "requesting twice leaves the latch set" {
            let stop = GracefulStop()
            stop.Request()
            stop.Request()
            Expect.isTrue stop.IsRequested "still set"
        }

        test "every definition declares a graceful stop capability, declared or not" {
            let capabilities =
                (FrameworkCapabilities FrameworkDefinition.empty<int> :> ITestFrameworkCapabilities).Capabilities

            Expect.isTrue
                (capabilities |> Seq.exists (fun c -> c :? IGracefulStopTestExecutionResultCapability))
                "the result-reporting graceful stop capability is present"

            Expect.isTrue
                (capabilities |> Seq.exists (fun c -> c :? IGracefulStopTestExecutionCapability))
                "so the platform's graceful stop path finds it"
        }

        test "TryStopTestExecutionAsync sets the latch and reports the request honoured" {
            let stop = GracefulStop()
            let honoured = (capabilityOf stop).TryStopTestExecutionAsync(CancellationToken.None).Result

            Expect.isTrue honoured "the framework reports it can stop gracefully"
            Expect.isTrue stop.IsRequested "the latch the run reads is set"
        }

        test "StopTestExecutionAsync sets the latch" {
            let stop = GracefulStop()
            let capability = capabilityOf stop :> IGracefulStopTestExecutionCapability
            capability.StopTestExecutionAsync(CancellationToken.None).Wait()

            Expect.isTrue stop.IsRequested "the latch the run reads is set"
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
