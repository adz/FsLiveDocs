namespace FsLiveDocs.Runner

open System
open System.IO
open FsLiveDocs.Core
open FSharp.Compiler.Interactive.Shell

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
        text.Replace("\r\n", "\n").Replace("\r", "\n").Trim()

    let private splitLines (text: string) =
        text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')

    let rec private formatTypeName (valueType: Type) =
        if valueType = typeof<unit> then "unit"
        elif valueType = typeof<int> then "int"
        elif valueType = typeof<int64> then "int64"
        elif valueType = typeof<int16> then "int16"
        elif valueType = typeof<byte> then "byte"
        elif valueType = typeof<double> then "float"
        elif valueType = typeof<single> then "float32"
        elif valueType = typeof<decimal> then "decimal"
        elif valueType = typeof<string> then "string"
        elif valueType = typeof<bool> then "bool"
        elif valueType = typeof<char> then "char"
        elif valueType.IsGenericType && valueType.GetGenericTypeDefinition() = typedefof<option<_>> then
            $"{formatTypeName (valueType.GetGenericArguments().[0])} option"
        elif valueType.IsGenericType && valueType.GetGenericTypeDefinition() = typedefof<list<_>> then
            $"{formatTypeName (valueType.GetGenericArguments().[0])} list"
        elif valueType.IsGenericType && valueType.GetGenericTypeDefinition() = typedefof<Map<_, _>> then
            let args = valueType.GetGenericArguments()
            $"Map<{formatTypeName args.[0]},{formatTypeName args.[1]}>"
        elif valueType.IsGenericType then
            let name = valueType.Name.Split('`').[0]
            let args = valueType.GetGenericArguments() |> Array.map formatTypeName |> String.concat ","
            $"{name}<{args}>"
        else
            valueType.Name

    let private buildLoadScript (assemblyPath: string) (references: string list) (projectNamespace: string) (extraOpens: string list) =
        let refs =
            assemblyPath :: references
            |> List.distinct
            |> List.filter (fun path -> not (String.IsNullOrWhiteSpace path))
            |> List.map (fun path -> $"#r @\"{Path.GetFullPath path}\"")

        let opens =
            [
                "open System"
                if not (String.IsNullOrWhiteSpace projectNamespace) then $"open {projectNamespace}"
                yield! extraOpens
            ]

        String.concat "\n" (refs @ opens)

    let private createSession () =
        let inStream = new StringReader("")
        let outStream = new StringWriter()
        let errStream = new StringWriter()
        let argv = [| "fsi.exe"; "--noninteractive"; "--quiet" |]
        let config = FsiEvaluationSession.GetDefaultConfiguration()

        FsiEvaluationSession.Create(config, argv, inStream, errStream, outStream), outStream, errStream

    let private appendLines (target: ResizeArray<string>) (text: string) =
        text
        |> splitLines
        |> Array.map (fun line -> line.TrimEnd())
        |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        |> Array.iter target.Add

    let private evalBlock (session: FsiEvaluationSession) (outStream: StringWriter) (errStream: StringWriter) (block: string) =
        let output = ResizeArray<string>()
        let outBuilder = outStream.GetStringBuilder()
        let errBuilder = errStream.GetStringBuilder()
        let outStart = outBuilder.Length
        let errStart = errBuilder.Length

        let normalized = block.Replace("\r\n", "\n").Replace("\r", "\n").Trim()

        let boundOutputs = ResizeArray<string>()

        use subscription =
            session.ValueBound.Subscribe(fun (valueObj, valueType, name) ->
                let name = if String.IsNullOrWhiteSpace name then "it" else name
                let typeName = formatTypeName valueType
                let valueText = session.FormatValue(valueObj, valueType)
                boundOutputs.Add($"val {name}: {typeName} = {valueText}")
            )

        try
            if normalized.StartsWith("#", StringComparison.Ordinal) then
                normalized.Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
                |> Array.iter (fun line -> session.EvalInteraction(line) |> ignore)
            elif normalized.EndsWith(";;", StringComparison.Ordinal) then
                session.EvalInteraction(normalized) |> ignore
            else
                session.EvalExpression(normalized) |> ignore
        with ex ->
            output.Add(ex.Message)

        boundOutputs |> Seq.iter output.Add

        let outText = outBuilder.ToString(outStart, outBuilder.Length - outStart)
        let errText = errBuilder.ToString(errStart, errBuilder.Length - errStart)

        if not (String.IsNullOrWhiteSpace outText) then
            appendLines output outText

        if not (String.IsNullOrWhiteSpace errText) then
            appendLines output errText

        output |> Seq.toList

    let private runFsiTranscript (blocks: string list) =
        let session, outStream, errStream = createSession ()
        use session = session

        blocks
        |> List.filter (fun block -> not (String.IsNullOrWhiteSpace block))
        |> List.collect (fun block -> evalBlock session outStream errStream block)
        |> String.concat "\n"

    let private runExample (projectAssembly: string) (projectNamespace: string) (references: string list) (scenario: ScenarioModel option) (example: ExampleModel) =
        let transcript = ExampleTranscript.parse example.Content
        let scenarioCall =
            scenario
            |> Option.map (fun s -> $"{s.MethodId}()")
        let scenarioOpens =
            scenario
            |> Option.map (fun s ->
                let parts = s.MethodId.Split('.')
                if parts.Length > 1 then parts.[parts.Length - 2] else s.MethodId)
            |> Option.toList

        let scriptBlocks =
            [
                buildLoadScript projectAssembly references projectNamespace scenarioOpens
            ]
            @ (scenarioCall |> Option.map (fun call -> "do " + call) |> Option.toList)
            @ transcript.Interactions

        let output = runFsiTranscript scriptBlocks
        output, transcript.ExpectedOutput, transcript.DisplayText

    let private getAllExamples (entities: EntityModel list) =
        let rec walk (e: EntityModel) =
            let members = e.Members |> List.collect (fun m -> m.Examples)
            let nested = e.Entities |> List.collect walk
            e.Examples @ members @ nested

        entities |> List.collect walk

    let private getSnapshotExamples (entities: EntityModel list) =
        getAllExamples entities
        |> List.filter (fun ex -> ex.IsSnapshotTest)

    let private statusOf (expected: string option) (actual: string) =
        match expected with
        | None -> "first-cut"
        | Some expectedText when String.Equals(actual, expectedText.Trim(), StringComparison.Ordinal) -> "verified"
        | Some _ -> "mismatch"

    /// <summary>Runs snapshot-selected examples and returns structured results for a generated Verify project.</summary>
    /// <param name="package">The package model containing examples and scenarios.</param>
    /// <param name="projectPath">The project that produced the package.</param>
    /// <param name="references">Additional references needed by generated examples.</param>
    /// <returns>A snapshot payload that can be verified by a generated test project.</returns>
    let collectSnapshots (package: PackageModel) (projectPath: string) (references: string list) = async {
        let resolvedProjectPath = resolveProjectPath projectPath
        let projectAssembly = resolveAssemblyPath resolvedProjectPath

        if String.IsNullOrWhiteSpace projectAssembly || not (File.Exists projectAssembly) then
            return {
                ProjectPath = resolvedProjectPath
                ProjectNamespace = Path.GetFileNameWithoutExtension(resolvedProjectPath)
                Examples = [
                    {
                        Name = "project"
                        Scenario = None
                        Source = ""
                        ExpectedOutput = None
                        ActualOutput = $"Could not resolve built assembly for {resolvedProjectPath}"
                        Status = "error"
                    }
                ]
            }
        else
            let projectNamespace = Path.GetFileNameWithoutExtension(resolvedProjectPath)
            let examples = getSnapshotExamples package.Entities

            let results =
                examples
                |> List.map (fun ex ->
                    let scenario =
                        ex.Scenario
                        |> Option.bind (fun sName -> package.Scenarios |> List.tryFind (fun s -> s.Name = sName))

                    let output, expected, source = runExample projectAssembly projectNamespace references scenario ex
                    let actual = output.Trim()
                    {
                        Name = ex.Name
                        Scenario = ex.Scenario
                        Source = source
                        ExpectedOutput = expected
                        ActualOutput = actual
                        Status = statusOf expected actual
                    })

            return {
                ProjectPath = resolvedProjectPath
                ProjectNamespace = projectNamespace
                Examples = results
            }
    }

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
            let allExamples = getSnapshotExamples package.Entities

            if List.isEmpty allExamples then
                return []
            else
                let mutable results = []
                for ex in allExamples do
                    let scenario =
                        ex.Scenario
                        |> Option.bind (fun sName -> package.Scenarios |> List.tryFind (fun s -> s.Name = sName))

                    let output, expected, _ = runExample projectAssembly projectNamespace references scenario ex
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
