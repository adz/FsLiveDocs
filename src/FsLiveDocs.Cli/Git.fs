namespace FsLiveDocs.Cli

open Axial
open Axial.Console
open Axial.FileSystem
open Axial.PlatformService
open Axial.Process

/// Runs local `git` queries. Hides process execution (Axial.Process) behind one synchronous
/// call that raises on failure, matching the CLI's existing synchronous command style.
module internal Git =

    type private RunnerEnvironment =
        { Process: IProcess }
        interface IHasProcess with
            member this.Process = this.Process

    let private environment : RunnerEnvironment = { Process = Process.live Clock.live FileSystem.live Console.live }

    /// Returns the current commit's full SHA for the repository at `workingDirectory`. Raises
    /// InvalidOperationException if there is no commit to record (not a git repository, or an
    /// empty repository).
    let currentRevision (workingDirectory: string) : string =
        let specification =
            Process.commandArgs "git" [ "rev-parse"; "HEAD" ]
            |> Process.workingDirectory workingDirectory
            |> Process.capture
        let revision =
            match specification |> Flow.run environment with
            | Exit.Success result -> result.StdOut.Trim()
            | Exit.Failure _ -> ""
        if System.String.IsNullOrWhiteSpace revision then
            invalidOp "Release capture requires a Git commit so the capsule can record source provenance."
        revision
