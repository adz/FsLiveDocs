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

    let private formatTypeName (valueType: Type) =
        match valueType.FullName with
        | "System.Int32" -> "int"
        | "System.Int64" -> "int64"
        | "System.Int16" -> "int16"
        | "System.Byte" -> "byte"
        | "System.Double" -> "float"
        | "System.Single" -> "float32"
        | "System.Decimal" -> "decimal"
        | "System.String" -> "string"
        | "System.Boolean" -> "bool"
        | "System.Char" -> "char"
        | "System.Void" -> "unit"
        | fullName when fullName = typeof<unit>.FullName -> "unit"
        | fullName when fullName = typeof<int option>.FullName -> "int option"
        | fullName when fullName = typeof<string option>.FullName -> "string option"
        | fullName when fullName <> null && fullName.StartsWith("Microsoft.FSharp.Collections.FSharpList") -> "list"
        | _ -> valueType.Name

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

    let private isDeclarationBlock (block: string) =
        let trimmed = block.TrimStart()
        trimmed.StartsWith("let ")
        || trimmed.StartsWith("do ")
        || trimmed.StartsWith("open ")
        || trimmed.StartsWith("module ")
        || trimmed.StartsWith("type ")
        || trimmed.StartsWith("namespace ")
        || trimmed.StartsWith("#")

    let private evalBlock (session: FsiEvaluationSession) (outStream: StringWriter) (errStream: StringWriter) (block: string) =
        let output = ResizeArray<string>()
        let outBuilder = outStream.GetStringBuilder()
        let errBuilder = errStream.GetStringBuilder()
        let outStart = outBuilder.Length
        let errStart = errBuilder.Length

        let normalized = block.Replace("\r\n", "\n").Replace("\r", "\n").Trim()

        if isDeclarationBlock normalized then
            let boundOutputs = ResizeArray<string>()

            use subscription =
                session.ValueBound.Subscribe(fun (valueObj, valueType, name) ->
                    let name = if String.IsNullOrWhiteSpace name then "it" else name
                    let typeName = formatTypeName valueType
                    let valueText = session.FormatValue(valueObj, valueType)
                    boundOutputs.Add($"val {name}: {typeName} = {valueText}")
                )

            try
                session.EvalInteraction(normalized) |> ignore
            with ex ->
                output.Add(ex.Message)

            boundOutputs |> Seq.iter output.Add
        else
            let expr =
                if normalized.EndsWith(";;", StringComparison.Ordinal) then
                    normalized.Substring(0, normalized.Length - 2).TrimEnd()
                else
                    normalized

            try
                match session.EvalExpression(expr) with
                | Some value ->
                    let valueText = session.FormatValue(value.ReflectionValue, value.ReflectionType)
                    let typeName = formatTypeName value.ReflectionType
                    output.Add($"val it: {typeName} = {valueText}")
                | None -> ()
            with ex ->
                output.Add(ex.Message)

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

        let scriptBlocks =
            [
                buildLoadScript projectAssembly references projectNamespace
            ]
            @ (scenarioCall |> Option.map (fun call -> "do " + call) |> Option.toList)
            |> List.append transcript.Interactions

        let output = runFsiTranscript scriptBlocks
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
