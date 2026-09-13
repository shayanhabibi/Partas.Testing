namespace Partas.TestingPlatform.CommandLine

open System.Collections.Generic
open System.CommandLine
open System.Threading.Tasks
open FSharp.SystemCommandLine
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.CommandLine

/// <summary>An <see cref="T:Microsoft.Testing.Platform.Extensions.IExtension"/> identity.</summary>
type ProviderMetadata =
    { Uid: string
      Version: string
      DisplayName: string
      Description: string }

/// <summary>
/// Declares a fixed set of <see cref="T:System.CommandLine.Option"/> values to MTP as an
/// <see cref="T:Microsoft.Testing.Platform.Extensions.CommandLine.ICommandLineOptionsProvider"/>,
/// so MTP accepts them instead of rejecting the run for an unrecognised option.
/// </summary>
type private OptionsProvider(metadata: ProviderMetadata, options: Option list) =

    let toCommandLineOption (option: Option) : CommandLineOption =
        let arity = ArgumentArity(option.Arity.MinimumNumberOfValues, option.Arity.MaximumNumberOfValues)
        CommandLineOption(option.Name.TrimStart('-'), option.Description, arity, option.Hidden)

    interface IExtension with
        member _.Uid = metadata.Uid
        member _.Version = metadata.Version
        member _.DisplayName = metadata.DisplayName
        member _.Description = metadata.Description
        member _.IsEnabledAsync() = Task.FromResult true

    interface ICommandLineOptionsProvider with
        member _.GetCommandLineOptions() =
            options |> List.map toCommandLineOption |> ResizeArray :> IReadOnlyCollection<CommandLineOption>

        member _.ValidateOptionArgumentsAsync(_, _) = ValidationResult.ValidTask

        member _.ValidateCommandLineOptionsAsync(_) = ValidationResult.ValidTask

module CommandLineOptions =

    /// <summary>Raised by <c>toProvider</c> for an option with a null or blank description.</summary>
    let private requireDescription (option: Option) =
        if System.String.IsNullOrWhiteSpace option.Description then
            invalidArg "options" $"Option '{option.Name}' requires a non-blank Description."

    /// <summary>
    /// Builds an <see cref="T:Microsoft.Testing.Platform.Extensions.CommandLine.ICommandLineOptionsProvider"/>
    /// declaring the given System.CommandLine options to MTP. Option names lose their
    /// System.CommandLine <c>--</c> prefix; MTP's own option names carry none.
    /// </summary>
    let toProvider (metadata: ProviderMetadata) (options: Option list) : ICommandLineOptionsProvider =
        options |> List.iter requireDescription
        OptionsProvider(metadata, options)

    /// <summary>
    /// Extracts the underlying <see cref="T:System.CommandLine.Option"/> from an
    /// <see cref="T:FSharp.SystemCommandLine.ActionInput"/> backed by a parsed option.
    /// </summary>
    let private toOption (input: ActionInput) : Option =
        match input.Source with
        | ActionInputSource.ParsedOption option -> option
        | other -> invalidArg (nameof input) $"'{other}' is not an option; toProviderFromInputs accepts only Input.option values."

    /// <summary>
    /// Builds an <see cref="T:Microsoft.Testing.Platform.Extensions.CommandLine.ICommandLineOptionsProvider"/>
    /// declaring the <see cref="T:System.CommandLine.Option"/> backing each given
    /// <see cref="T:FSharp.SystemCommandLine.ActionInput"/> to MTP.
    /// </summary>
    let toProviderFromInputs (metadata: ProviderMetadata) (inputs: ActionInput list) : ICommandLineOptionsProvider =
        inputs |> List.map toOption |> toProvider metadata
