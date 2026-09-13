namespace Partas.TestingPlatform.CommandLine

open System.Collections.Generic
open System.CommandLine
open System.Threading.Tasks
open Microsoft.Testing.Platform.CommandLine
open Microsoft.Testing.Platform.Extensions
open Microsoft.Testing.Platform.Extensions.CommandLine

/// <summary>
/// Declares a fixed set of <see cref="T:System.CommandLine.Option"/> values to MTP as an
/// <see cref="T:Microsoft.Testing.Platform.Extensions.CommandLine.ICommandLineOptionsProvider"/>,
/// so MTP accepts them instead of rejecting the run for an unrecognised option.
/// </summary>
type private OptionsProvider
    (uid: string, version: string, displayName: string, description: string, options: Option list) =

    let toCommandLineOption (option: Option) : CommandLineOption =
        let arity = ArgumentArity(option.Arity.MinimumNumberOfValues, option.Arity.MaximumNumberOfValues)
        CommandLineOption(option.Name.TrimStart('-'), option.Description, arity, option.Hidden)

    interface IExtension with
        member _.Uid = uid
        member _.Version = version
        member _.DisplayName = displayName
        member _.Description = description
        member _.IsEnabledAsync() = Task.FromResult true

    interface ICommandLineOptionsProvider with
        member _.GetCommandLineOptions() =
            options |> List.map toCommandLineOption |> ResizeArray :> IReadOnlyCollection<CommandLineOption>

        member _.ValidateOptionArgumentsAsync(_, _) = ValidationResult.ValidTask

        member _.ValidateCommandLineOptionsAsync(_) = ValidationResult.ValidTask

module CommandLineOptions =

    /// <summary>
    /// Builds an <see cref="T:Microsoft.Testing.Platform.Extensions.CommandLine.ICommandLineOptionsProvider"/>
    /// declaring the given System.CommandLine options to MTP. Option names lose their
    /// System.CommandLine <c>--</c> prefix; MTP's own option names carry none.
    /// </summary>
    let toProvider
        (uid: string)
        (version: string)
        (displayName: string)
        (description: string)
        (options: Option list)
        : ICommandLineOptionsProvider =
        OptionsProvider(uid, version, displayName, description, options)
