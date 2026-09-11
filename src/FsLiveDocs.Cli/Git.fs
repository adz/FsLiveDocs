namespace FsLiveDocs.Cli

open Axial
open Axial.Process
open FsLiveDocs.Core.Effects

/// Runs local `git` queries. Hides process execution (Axial.Process) behind one synchronous
/// call that raises on failure, matching the CLI's existing synchronous command style.
module internal Git =

    /// Returns the current commit's full SHA for the repository at `workingDirectory`. Raises
    /// InvalidOperationException if there is no commit to record (not a git repository, or an
    /// empty repository).
    let currentRevision (workingDirectory: string) : string =
        let specification =
            Process.commandArgs "git" [ "rev-parse"; "HEAD" ]
            |> Process.workingDirectory workingDirectory
            |> Process.capture
        let revision = Run.orFallback (specification |> Flow.map (fun result -> result.StdOut.Trim())) ""
        if System.String.IsNullOrWhiteSpace revision then
            invalidOp "Release capture requires a Git commit so the capsule can record source provenance."
        revision
