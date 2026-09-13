module Partas.TestingPlatform.CommandLine.Tests.CommandLineOptionsTests

open System.CommandLine
open Expecto
open Microsoft.Testing.Platform.CommandLine
open Partas.TestingPlatform.CommandLine

let private buildProvider options =
    CommandLineOptions.toProvider "uid" "1.2.3" "display" "a description" options

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
            let option = Option<string>("--filter")
            option.Description <- "narrows the run"
            let provider = buildProvider [ option ]
            let declared = provider.GetCommandLineOptions() |> Seq.exactlyOne
            Expect.equal declared.Arity.Min option.Arity.MinimumNumberOfValues "min preserved"
            Expect.equal declared.Arity.Max option.Arity.MaximumNumberOfValues "max preserved"
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
    ]
