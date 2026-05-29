namespace FsLiveDocs.Runner

open System
open System.IO
open System.Diagnostics
open FsLiveDocs.Core

/// <summary>The execution engine for verified docstrings (DocTests).</summary>
module DocTestRunner =

    let private resolveProjectPath (projectPath: string) =
        if Path.IsPathRooted(projectPath) && File.Exists(projectPath) then projectPath
        else
            let assemblyDir =
                typeof<PackageModel>.Assembly.Location
                |> Path.GetDirectoryName

            let projectName = Path.GetFileNameWithoutExtension(projectPath)
            let candidate =
                Path.Combine(
                    assemblyDir,
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    projectName,
                    Path.GetFileName(projectPath)
                )
                |> Path.GetFullPath

            if File.Exists(candidate) then candidate
            else Path.GetFullPath(projectPath)

    let private resolveAssemblyPath (projectPath: string) =
        let projectName = Path.GetFileNameWithoutExtension(projectPath)
        let projDir = Path.GetDirectoryName(projectPath)

        let searchPaths = [
            Path.Combine(projDir, "../../artifacts/bin")
            Path.Combine(projDir, "bin/Debug/net10.0")
            Path.Combine(projDir, "bin/Release/net10.0")
        ]

        searchPaths
        |> List.filter Directory.Exists
        |> List.tryPick (fun path ->
            let files = Directory.GetFiles(path, $"{projectName}.dll", SearchOption.AllDirectories)
            if files.Length > 0 then Some files.[0] else None)
        |> Option.defaultValue ""

    let private normalizeScript (script: string) =
        script.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd()

    let private normalizeFsiOutput (text: string) =
        text.Replace("\r\n", "\n").Replace("\r", "\n")
        |> fun value -> value.Split('\n')
        |> Array.map (fun line -> line.TrimEnd())
        |> Array.filter (fun line ->
            let trimmed = line.TrimStart()
            not (String.IsNullOrWhiteSpace trimmed)
            && not (trimmed.StartsWith("val ", StringComparison.Ordinal))
            && not (trimmed.StartsWith("module ", StringComparison.Ordinal)))
        |> String.concat "\n"
        |> fun value -> value.Trim()

    let private buildLoadScript (assemblyPath: string) (references: string list) (projectNamespace: string) =
        let refs =
            assemblyPath :: references
            |> List.distinct
            |> List.filter (fun path -> not (String.IsNullOrWhiteSpace path))
            |> List.map (fun path -> $"#r @\"{Path.GetFullPath path}\"")

        let opens =
            [
                "open System"
                if not (String.IsNullOrWhiteSpace projectNamespace) then $"open {projectNamespace}"
            ]

        String.concat "\n" (refs @ opens)

    let private runFsiScript (script: string) =
        let tempDir = Path.Combine(Path.GetTempPath(), "FsLiveDocs", Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        let scriptFile = Path.Combine(tempDir, "input.fsx")
        File.WriteAllText(scriptFile, script)

        let psi = ProcessStartInfo("dotnet", $"fsi --nologo --exec \"{scriptFile}\"")
        psi.WorkingDirectory <- tempDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false

        use proc = Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        let error = proc.StandardError.ReadToEnd()
        proc.WaitForExit()

        let output = normalizeFsiOutput output
        let error = normalizeFsiOutput error

        if proc.ExitCode = 0 then
            if String.IsNullOrWhiteSpace error then output else output + "\n" + error
        else
            [
                if not (String.IsNullOrWhiteSpace output) then Some output else None
                if not (String.IsNullOrWhiteSpace error) then Some error else None
                Some $"fsi exited with code {proc.ExitCode}"
            ]
            |> List.choose id
            |> String.concat "\n"

    let private runExample (projectAssembly: string) (projectNamespace: string) (references: string list) (scenario: ScenarioModel option) (example: ExampleModel) =
        let transcript = ExampleTranscript.parse example.Content
        let scenarioCall =
            scenario
            |> Option.map (fun s -> $"{s.MethodId}()")

        let lines =
            [
                buildLoadScript projectAssembly references projectNamespace
                if scenarioCall.IsSome then
                    scenarioCall.Value + ";;"
                transcript.Script
            ]

        let script = lines |> List.filter (fun s -> not (String.IsNullOrWhiteSpace s)) |> String.concat "\n\n"
        let output = runFsiScript script
        output, transcript.ExpectedOutput

    let private getAllExamples (entities: EntityModel list) =
        let rec walk (e: EntityModel) =
            let members = e.Members |> List.collect (fun m -> m.Examples)
            let nested = e.Entities |> List.collect walk
            e.Examples @ members @ nested

        entities |> List.collect walk

    /// <summary>Verifies all docstring examples extracted from a package.</summary>
    /// <param name="package">The package model containing examples and scenarios.</param>
    /// <param name="projectPath">The project that produced the package.</param>
    /// <param name="references">Additional references needed by generated examples.</param>
    /// <returns>A list of example names paired with pass/fail results and diagnostic output.</returns>
    let verifyExamples (package: PackageModel) (projectPath: string) (references: string list) = async {
        let resolvedProjectPath = resolveProjectPath projectPath
        let projectAssembly = resolveAssemblyPath resolvedProjectPath

        if String.IsNullOrWhiteSpace projectAssembly || not (File.Exists projectAssembly) then
            return [
                "project", false, $"Could not resolve built assembly for {resolvedProjectPath}"
            ]
        else
            let projectNamespace = Path.GetFileNameWithoutExtension(resolvedProjectPath)
            let allExamples = getAllExamples package.Entities

            if allExamples.IsEmpty then
                return []
            else
                let mutable results = []
                for ex in allExamples do
                    let scenario =
                        ex.Scenario
                        |> Option.bind (fun sName -> package.Scenarios |> List.tryFind (fun s -> s.Name = sName))

                    let output, expected = runExample projectAssembly projectNamespace references scenario ex
                    let actual = output.Trim()

                    match expected with
                    | Some expectedText ->
                        let expectedText = expectedText.Trim()
                        if String.Equals(actual, expectedText, StringComparison.Ordinal) then
                            results <- (ex.Name, true, actual) :: results
                        else
                            results <- (ex.Name, false, $"Expected:\n{expectedText}\n\nActual:\n{actual}") :: results
                    | None ->
                        results <- (ex.Name, true, actual) :: results

                return List.rev results
    }
