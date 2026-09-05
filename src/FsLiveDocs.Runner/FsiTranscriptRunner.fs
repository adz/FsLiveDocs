namespace FsLiveDocs.Runner

open System
open System.IO
open FsLiveDocs.Core
open FSharp.Compiler.Interactive.Shell

/// <summary>Runs FSI transcripts and formats the resulting output.</summary>
module FsiTranscriptRunner =
    type DocTestExecutionContext = {
        Project: ResolvedProject
        References: string list
        Scenario: ScenarioModel option
        Example: ExampleModel
    }

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

    let private buildLoadScript (project: ResolvedProject) (references: string list) (extraOpens: string list) =
        let projectDependencies =
            if String.IsNullOrWhiteSpace project.AssemblyPath then []
            else
                let directory = Path.GetDirectoryName project.AssemblyPath
                if Directory.Exists directory then
                    Directory.GetFiles(directory, "*.dll")
                    |> Array.filter (fun path ->
                        let name = Path.GetFileNameWithoutExtension path
                        not (name.Equals("FSharp.Core", StringComparison.OrdinalIgnoreCase))
                        && not (name.Equals("FSharp.Compiler.Service", StringComparison.OrdinalIgnoreCase)))
                    |> Array.toList
                else []
        let refs =
            project.AssemblyPath :: (projectDependencies @ references)
            |> List.distinct
            |> List.filter (fun path -> not (String.IsNullOrWhiteSpace path))
            |> List.map (fun path -> $"#r @\"{Path.GetFullPath path}\"")

        let opens =
            [
                "open System"
                if not (String.IsNullOrWhiteSpace project.ProjectNamespace) then $"open {project.ProjectNamespace}"
                // Assembly names are not namespace names. Opening each reference worked only
                // by accident until FsLiveDocs.Annotations became an F# assembly whose package
                // name differs from its FsLiveDocs namespace.
                yield! extraOpens
            ]
            |> List.distinct

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
        |> fun value -> value.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
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
                let valueText =
                    if valueType = typeof<decimal> then (unbox<decimal> valueObj).ToString(System.Globalization.CultureInfo.InvariantCulture) + "M"
                    else session.FormatValue(valueObj, valueType)
                boundOutputs.Add($"val {name}: {typeName} = {valueText}")
            )

        try
            if normalized.StartsWith("#", StringComparison.Ordinal) then
                normalized.Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
                |> Array.iter (fun line -> session.EvalInteraction(line) |> ignore)
            else
                // A run block may contain page setup (`open`, declarations, then an
                // expression), so it is an FSI interaction rather than one expression.
                session.EvalInteraction(normalized) |> ignore
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

    let private runFsiTranscript setupCount (blocks: string list) =
        let session, outStream, errStream = createSession ()
        use session = session

        blocks
        |> List.filter (fun block -> not (String.IsNullOrWhiteSpace block))
        |> List.mapi (fun index block ->
            let output = evalBlock session outStream errStream block
            if index < setupCount && not (output |> List.exists (fun line -> line.Contains("error FS", StringComparison.OrdinalIgnoreCase))) then [] else output)
        |> List.collect id
        |> String.concat "\n"

    let runExample (context: DocTestExecutionContext) =
        let transcript = ExampleTranscript.parse context.Example.Content
        let scenarioCall =
            context.Scenario
            |> Option.map (fun s -> $"{s.MethodId}()")
        let scriptBlocks =
            [
                buildLoadScript context.Project context.References []
            ]
            @ (scenarioCall |> Option.toList)
            @ transcript.Interactions

        let output = runFsiTranscript (1 + (if scenarioCall.IsSome then 1 else 0)) scriptBlocks
        output, transcript.ExpectedOutput, transcript.DisplayText
