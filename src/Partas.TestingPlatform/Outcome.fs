namespace Partas.TestingPlatform

open System
open Microsoft.Testing.Platform.Extensions.Messages

type AssertionFailure = { Expected: string option; Actual: string option }

/// <summary>The attempt number of an execution, and whether a later attempt supersedes it.</summary>
type RetryAttempt = { AttemptNumber: int; IsSuperseded: bool }

/// <summary>
/// How a test ended. The platform's <c>Cancelled</c> state is obsolete, so a cancelled test
/// reports <c>Skipped</c>.
/// </summary>
type TestOutcome =
    | Passed
    | Skipped
    | Failed of exn option * AssertionFailure option
    | Errored of exn option
    | TimedOut of exn option * TimeSpan option

type TestResult =
    { Outcome: TestOutcome
      Explanation: string option
      StandardOutput: string option
      StandardError: string option
      RetryAttempt: RetryAttempt option
      ExtraProperties: IProperty list }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TestResult =

    let create outcome =
        { Outcome = outcome
          Explanation = None
          StandardOutput = None
          StandardError = None
          RetryAttempt = None
          ExtraProperties = [] }

    /// <summary>Every platform property a result carries, beginning with its state.</summary>
    let properties (result: TestResult) : IProperty list =
        let explanation = Option.toObj result.Explanation

        let state: IProperty =
            match result.Outcome with
            | Passed -> PassedTestNodeStateProperty explanation
            | Skipped -> SkippedTestNodeStateProperty explanation
            | Failed(error, _) -> FailedTestNodeStateProperty(Option.toObj error, explanation)
            | Errored error -> ErrorTestNodeStateProperty(Option.toObj error, explanation)
            | TimedOut(error, timeout) ->
                TimeoutTestNodeStateProperty(Option.toObj error, explanation, Timeout = Option.toNullable timeout)

        let assertion =
            match result.Outcome with
            | Failed(_, Some failure) ->
                [ AssertionFailureProperty(Option.toObj failure.Expected, Option.toObj failure.Actual) :> IProperty ]
            | _ -> []

        let output =
            [ match result.StandardOutput with
              | Some text -> StandardOutputProperty text :> IProperty
              | None -> ()
              match result.StandardError with
              | Some text -> StandardErrorProperty text
              | None -> () ]

        let retry =
            match result.RetryAttempt with
            | Some attempt -> [ RetryAttemptProperty(attempt.AttemptNumber, attempt.IsSuperseded) :> IProperty ]
            | None -> []

        [ state ] @ assertion @ output @ retry @ result.ExtraProperties
