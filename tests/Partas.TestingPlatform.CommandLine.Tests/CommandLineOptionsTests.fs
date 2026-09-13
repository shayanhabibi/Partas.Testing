module Partas.TestingPlatform.CommandLine.Tests.CommandLineOptionsTests

open System.CommandLine
open Expecto
open FSharp.SystemCommandLine
open Microsoft.Testing.Platform.CommandLine
open Partas.TestingPlatform.CommandLine

let private metadata =
    { Uid = "uid"
      Version = "1.2.3"
      DisplayName = "display"
      Description = "a description" }

let private buildProvider options = CommandLineOptions.toProvider metadata options

[<Tests>]
let tests =
    testList "CommandLineOptions.toProvider" [
        test "an option's name loses the System.CommandLine prefix" {
            let option = Option<string>("--my-custom-filter")
            option.Description <- "a custom filter"
            let provider = buildProvider [ option ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            Expect.equal declared.Name "my-custom-filter" "prefix stripped"
        }

        test "an option's description carries through unchanged" {
            let option = Option<string>("--filter")
            option.Description <- "narrows the run"
            let provider = buildProvider [ option ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            Expect.equal declared.Description "narrows the run" "description preserved"
        }

        test "an option's arity maps onto CommandLineOption's ArgumentArity" {
            // Option<bool> carries an asymmetric arity (0..1); a degenerate 1..1 option
            // cannot distinguish correct mapping from a swapped or hardcoded one.
            let option = Option<bool>("--flag")
            option.Description <- "toggles something"
            let provider = buildProvider [ option ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            Expect.equal declared.Arity.Min 0 "min preserved"
            Expect.equal declared.Arity.Max 1 "max preserved"
        }

        test "a hidden option stays hidden" {
            let option = Option<string>("--secret")
            option.Description <- "a hidden option"
            option.Hidden <- true
            let provider = buildProvider [ option ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            Expect.isTrue declared.IsHidden "hidden preserved"
        }

        test "every declared option is carried through, in order" {
            let a = Option<string>("--a")
            a.Description <- "a"
            let b = Option<string>("--b")
            b.Description <- "b"
            let provider = buildProvider [ a; b ]
            let names = provider.GetCommandLineOptions() |> Seq.map _.Name |> List.ofSeq
            Expect.equal names [ "a"; "b" ] "both options present, in order"
        }

        test "the provider's extension identity is the given uid, version, display name and description" {
            let provider = buildProvider [] :> Microsoft.Testing.Platform.Extensions.IExtension
            Expect.equal provider.Uid "uid" "uid"
            Expect.equal provider.Version "1.2.3" "version"
            Expect.equal provider.DisplayName "display" "display name"
            Expect.equal provider.Description "a description" "description"
        }

        testAsync "the provider is always enabled" {
            let provider = buildProvider [] :> Microsoft.Testing.Platform.Extensions.IExtension
            let! enabled = provider.IsEnabledAsync() |> Async.AwaitTask
            Expect.isTrue enabled "always enabled"
        }

        testAsync "argument validation always succeeds" {
            let option = Option<string>("--a")
            option.Description <- "a"
            let provider = buildProvider [ option ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            let! result = provider.ValidateOptionArgumentsAsync(declared, [| "x" |]) |> Async.AwaitTask
            Expect.isTrue result.IsValid "valid"
        }

        testAsync "command-line validation always succeeds" {
            let provider = buildProvider []
            let! result = provider.ValidateCommandLineOptionsAsync(Unchecked.defaultof<ICommandLineOptions>) |> Async.AwaitTask
            Expect.isTrue result.IsValid "valid"
        }

        test "a blank description raises, naming the offending option" {
            let option = Option<string>("--no-description")

            Expect.throwsC
                (fun () -> buildProvider [ option ] |> ignore)
                (fun ex -> Expect.stringContains ex.Message "--no-description" "names the offending option")
        }

        test "toProviderFromInputs extracts the option backing an ActionInput" {
            let input = Input.option<string> "--from-input" |> Input.description "via FSharp.SystemCommandLine"
            let provider = CommandLineOptions.toProviderFromInputs metadata [ input :> ActionInput ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            Expect.equal declared.Name "from-input" "prefix stripped"
            Expect.equal declared.Description "via FSharp.SystemCommandLine" "description preserved"
        }
    ]
