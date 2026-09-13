namespace Partas.TestingPlatform

open System.Threading
open Microsoft.Testing.Platform.Extensions.TestFramework
open Microsoft.Testing.Platform.TestHost

[<Struct>]
type SessionContext =
    { SessionId: SessionUid
      CancellationToken: CancellationToken }

[<RequireQualifiedAccess>]
type SessionOutcome =
    | Succeeded of warning: string option
    | Failed of error: string * warning: string option

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SessionContext =

    let ofCreate (context: CreateTestSessionContext) =
        { SessionId = context.SessionUid
          CancellationToken = context.CancellationToken }

    let ofClose (context: CloseTestSessionContext) =
        { SessionId = context.SessionUid
          CancellationToken = context.CancellationToken }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SessionOutcome =

    let internal toCreateResult outcome =
        match outcome with
        | SessionOutcome.Succeeded warning ->
            CreateTestSessionResult(IsSuccess = true, WarningMessage = Option.toObj warning)
        | SessionOutcome.Failed(error, warning) ->
            CreateTestSessionResult(IsSuccess = false, ErrorMessage = error, WarningMessage = Option.toObj warning)

    let internal toCloseResult outcome =
        match outcome with
        | SessionOutcome.Succeeded warning ->
            CloseTestSessionResult(IsSuccess = true, WarningMessage = Option.toObj warning)
        | SessionOutcome.Failed(error, warning) ->
            CloseTestSessionResult(IsSuccess = false, ErrorMessage = error, WarningMessage = Option.toObj warning)

module Session =

    /// <summary>
    /// Builds and resolves a tree, reporting a construction failure or a uid collision as the
    /// message a failed session carries.
    /// </summary>
    let resolveTree (build: unit -> TestTree<'T>) : Result<ResolvedTestTree<'T>, string> =
        try
            match TestTree.resolve (build ()) with
            | Ok resolved -> Ok resolved
            | Error collisions ->
                collisions
                |> List.map _.Uid
                |> String.concat ", "
                |> sprintf "Duplicate test uids: %s"
                |> Error
        with error ->
            Error $"The test tree could not be built: {error.Message}"
