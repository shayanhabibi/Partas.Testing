module Partas.TestingPlatform.Tests.FrameworkTests

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.CommandLine
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
