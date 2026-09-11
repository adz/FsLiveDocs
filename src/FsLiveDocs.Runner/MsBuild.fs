namespace FsLiveDocs.Runner

open System
open System.IO
open Axial
open Axial.Console
open Axial.FileSystem
open Axial.PlatformService
open Axial.Process
open Newtonsoft.Json.Linq

/// Runs `dotnet msbuild` for one project and parses its JSON property output. Hides process
/// execution (Axial.Process) behind one synchronous call that raises on failure, matching the
/// module's existing synchronous evaluation contract -- callers see no Flow, no Axial types.
module MsBuild =

    type private RunnerEnvironment =
        { Process: IProcess }
        interface IHasProcess with
            member this.Process = this.Process

    let private environment : RunnerEnvironment = { Process = Process.live Clock.live FileSystem.live Console.live }

    let private failure (fullPath: string) (detail: string) =
        // A project outside the solution is never restored by a solution-level build, so this
        // failure usually means the project list includes something the solution does not build.
        if detail.Contains("NETSDK1004", StringComparison.Ordinal) then
            invalidOp
                $"Project is not restored: {fullPath}\n\
                  Run 'dotnet restore \"{fullPath}\"' first. If this project is not part of your solution, \
                  a solution-level restore never covers it — check that you meant to pass it to livedocs."
        else
            invalidOp $"MSBuild evaluation failed for {fullPath}: {detail}"

    /// Runs `dotnet msbuild <fullPath> ...arguments -nologo` and parses stdout as JSON.
    /// Raises InvalidOperationException with a message built from both streams on failure,
    /// since MSBuild reports errors on either stream depending on the failure kind.
    let evaluate (fullPath: string) (arguments: string list) : JObject =
        let specification =
            Process.commandArgs "dotnet" ([ "msbuild"; fullPath ] @ arguments @ [ "-nologo" ])
            |> Process.workingDirectory (Path.GetDirectoryName(fullPath))
            |> Process.capture
        match specification |> Flow.run environment with
        | Exit.Success result -> JObject.Parse(result.StdOut)
        | Exit.Failure(Cause.Fail(ProcessError.StageFailed stageFailure)) ->
            let detail = (stageFailure.Result.StdErr + Environment.NewLine + stageFailure.Result.StdOut).Trim()
            failure fullPath detail
        | Exit.Failure(Cause.Fail processError) -> failure fullPath (ProcessError.describe processError)
        | Exit.Failure cause -> failure fullPath (string cause)
