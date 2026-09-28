namespace FsLiveDocs.Runner

open System
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Axial
open Axial.Process
open FsLiveDocs.Core.Effects
open FsLiveDocs.TranscriptHost

/// <summary>Runs transcript examples in the separate <c>FsLiveDocs.TranscriptHost</c> worker process.</summary>
/// <remarks>
/// FsLiveDocs itself loads libraries (Axial among them) that a documented project may also use at another version. In
/// one process the tool's copy wins, so a transcript calling an API newer than the tool's copy fails. The worker loads
/// only the documented project's graph. See <c>dev-docs/VERIFICATION_ARCHITECTURE.md</c>.
/// </remarks>
module TranscriptHostClient =
    /// Overrides the worker's location, for development layouts where it is not beside FsLiveDocs.Runner.
    [<Literal>]
    let WorkerPathVariable = "FSLIVEDOCS_TRANSCRIPT_HOST"

    /// Overrides how long one worker may run, in seconds.
    [<Literal>]
    let TimeoutVariable = "FSLIVEDOCS_TRANSCRIPT_TIMEOUT_SECONDS"

    let private defaultTimeout = TimeSpan.FromMinutes 10.0

    // Stderr carries only diagnostics and output from evaluated code; keep enough to explain a failure.
    let private stderrTailBytes = 64 * 1024

    let private workerPath () =
        match Environment.GetEnvironmentVariable WorkerPathVariable with
        | configured when not (String.IsNullOrWhiteSpace configured) -> Path.GetFullPath configured
        | _ ->
            // The worker ships beside the assembly that declares its protocol: the tool folder, a test output
            // folder, or a generated verification project's output folder.
            Path.Combine(Path.GetDirectoryName(typeof<TranscriptRequest>.Assembly.Location), Protocol.WorkerFileName)

    /// The files the worker needs to start: itself, its runtime files, and each runtime and resource asset its
    /// `deps.json` lists, as paths relative to the directory it ships in.
    let private workerFiles (worker: string) =
        let directory = Path.GetDirectoryName worker
        let name = Path.GetFileNameWithoutExtension worker
        let own = [ Path.GetFileName worker; $"{name}.deps.json"; $"{name}.runtimeconfig.json" ]

        use deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"{name}.deps.json")))

        let assets =
            [ for target in deps.RootElement.GetProperty("targets").EnumerateObject() do
                  for library in target.Value.EnumerateObject() do
                      let mutable runtime = Unchecked.defaultof<JsonElement>
                      if library.Value.TryGetProperty("runtime", &runtime) then
                          for asset in runtime.EnumerateObject() -> Path.GetFileName asset.Name

                      let mutable resources = Unchecked.defaultof<JsonElement>
                      if library.Value.TryGetProperty("resources", &resources) then
                          for asset in resources.EnumerateObject() ->
                              Path.Combine(asset.Value.GetProperty("locale").GetString(), Path.GetFileName asset.Name) ]

        own @ assets
        |> List.distinct
        |> List.filter (fun relative -> File.Exists(Path.Combine(directory, relative)))

    /// <summary>
    /// Copies the worker into a directory that holds nothing else, and returns the copy's path.
    /// </summary>
    /// <remarks>
    /// The worker ships beside FsLiveDocs.Runner, whose folder also holds the tool's own Axial assemblies. A separate
    /// process keeps them out of the worker's runtime, but the F# compiler inside it also resolves an assembly's
    /// dependencies from the folder the compiler was loaded from. A documented `Axial.HttpClient.dll` referenced before
    /// its `Axial.dll` then type-checked against the tool's older Axial. The staged folder is keyed by the worker files'
    /// sizes and timestamps, built once per process, and moved into place so concurrent runs share one copy.
    /// </remarks>
    let private stage (worker: string) =
        let directory = Path.GetDirectoryName worker
        let files = workerFiles worker

        let key =
            files
            |> List.map (fun relative ->
                let info = FileInfo(Path.Combine(directory, relative))
                $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")
            |> String.concat "\n"
            |> Encoding.UTF8.GetBytes
            |> SHA256.HashData
            |> Convert.ToHexString
            |> fun hash -> hash.Substring(0, 16).ToLowerInvariant()

        let root = Path.Combine(Path.GetTempPath(), "fslivedocs-transcript-host")
        let target = Path.Combine(root, key)
        let staged = Path.Combine(target, Path.GetFileName worker)

        if not (File.Exists staged) then
            let temporary = Path.Combine(root, $"{key}.{Guid.NewGuid():N}")
            for relative in files do
                let destination = Path.Combine(temporary, relative)
                Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
                File.Copy(Path.Combine(directory, relative), destination)
            try
                Directory.Move(temporary, target)
            with :? IOException when File.Exists staged ->
                // Another run staged the same worker first.
                Directory.Delete(temporary, true)

        staged

    let private stagedWorkers = Collections.Concurrent.ConcurrentDictionary<string, Lazy<string>>()

    /// The `dotnet` host that is running this process, so the worker uses the same installation even when `dotnet`
    /// is not on PATH (a global tool launched through its apphost).
    let private dotnetHost () =
        let executable = if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "dotnet.exe" else "dotnet"
        let isDotnet (path: string) =
            not (String.IsNullOrWhiteSpace path)
            && Path.GetFileName(path).Equals(executable, StringComparison.OrdinalIgnoreCase)
            && File.Exists path
        // The runtime lives at <root>/shared/Microsoft.NETCore.App/<version>/, beside <root>/dotnet.
        let fromRuntimeDirectory =
            let runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            let root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName runtimeDirectory))
            if isNull root then "" else Path.Combine(root, executable)

        [ Environment.GetEnvironmentVariable "DOTNET_HOST_PATH"; Environment.ProcessPath; fromRuntimeDirectory ]
        |> List.tryFind isDotnet
        |> Option.defaultValue "dotnet"

    let private timeout () =
        match Environment.GetEnvironmentVariable TimeoutVariable with
        | configured when not (String.IsNullOrWhiteSpace configured) ->
            match Int32.TryParse configured with
            | true, seconds when seconds > 0 -> TimeSpan.FromSeconds(float seconds)
            | _ -> invalidOp $"{TimeoutVariable} must be a positive number of seconds, not '{configured}'."
        | _ -> defaultTimeout

    let private failure (detail: string) (stderr: string) =
        let stderr = stderr.Trim()
        let tail = if String.IsNullOrEmpty stderr then "" else $"{Environment.NewLine}Worker stderr:{Environment.NewLine}{stderr}"
        invalidOp $"Transcript worker failed: {detail}{tail}"

    /// <summary>Evaluates examples in one fresh worker process, killing it and its children after <paramref name="limit" />.</summary>
    /// <returns>One normalized output per example, in order.</returns>
    let runWithin (limit: TimeSpan) (examples: TranscriptExample list) : string list =
        if examples.IsEmpty then
            []
        else
            let worker = workerPath ()
            if not (File.Exists worker) then
                invalidOp $"The transcript worker was not found at {worker}. It ships beside FsLiveDocs.Runner; set {WorkerPathVariable} to override."

            let worker = stagedWorkers.GetOrAdd(worker, fun path -> lazy (stage path)).Value

            let request = Protocol.serializeRequest { ProtocolVersion = Protocol.Version; Examples = List.toArray examples }

            let specification =
                Process.commandArgs (dotnetHost ()) [ "exec"; worker ]
                |> Process.stdin (InputSource.Text request)
                |> Process.stderr (OutputTarget.CaptureTail stderrTailBytes)
                |> Process.encoding (UTF8Encoding false)
                // The worker exits 1 after writing a response that explains what failed.
                |> Process.successCodes [ 0; 1 ]
                |> Process.timeout limit
                |> Process.capture

            match specification |> Flow.run LiveEnvironment.instance with
            | Exit.Success result ->
                let response =
                    try
                        Protocol.deserializeResponse result.StdOut
                    with error ->
                        failure $"its response could not be read ({error.Message}); exit code {result.ExitCode}." result.StdErr

                if response.ProtocolVersion <> Protocol.Version then
                    failure $"it answered with protocol version {response.ProtocolVersion}, expected {Protocol.Version}." result.StdErr
                elif not (isNull response.Error) then
                    failure response.Error result.StdErr
                elif response.Outputs.Length <> examples.Length then
                    failure $"it returned {response.Outputs.Length} outputs for {examples.Length} examples." result.StdErr
                else
                    List.ofArray response.Outputs
            | Exit.Failure(Cause.Fail(ProcessError.StageFailed stageFailure)) ->
                failure $"exit code {stageFailure.Result.ExitCode}." stageFailure.Result.StdErr
            | Exit.Failure(Cause.Fail processError) -> failure (ProcessError.describe processError) ""
            | Exit.Failure cause -> failure (string cause) ""

    /// <summary>Evaluates examples in one fresh worker process and returns one output per example.</summary>
    /// <remarks>The worker is stopped after ten minutes, or after <c>FSLIVEDOCS_TRANSCRIPT_TIMEOUT_SECONDS</c>.</remarks>
    let run (examples: TranscriptExample list) : string list = runWithin (timeout ()) examples
