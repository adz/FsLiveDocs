namespace FsLiveDocs.Runner

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open FsLiveDocs.Core
open Newtonsoft.Json.Linq

type EvaluatedProject = {
    ProjectPath: string
    TargetFramework: string
    References: string list
    Defines: string list
    LanguageVersion: string option
    OtherOptions: string list
}

type MappedCompilerDiagnostic = {
    BlockId: string option
    SourcePath: string
    Severity: SemanticDiagnosticSeverity
    Message: string
    StartLine: int
    StartColumn: int
    EndLine: int
    EndColumn: int
}

type CheckedCompilationUnit = {
    Unit: CompilationUnit
    SyntheticSource: string
    Diagnostics: MappedCompilerDiagnostic list
    BlockRanges: CompilationSourceRange list
    CheckResults: FSharpCheckFileResults option
}

and CompilationSourceRange = { Block: DocumentationBlock; StartLine: int; EndLine: int }

/// Evaluates the real MSBuild project and checks canonical documentation compilation units with FCS.
module DocumentationCompiler =

    let private readString (item: JToken) name =
        match item.[name] with
        | null -> None
        | value -> value.Value<string>() |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let private runMsBuild (fullPath: string) (arguments: string list) =
        let startInfo = ProcessStartInfo("dotnet")
        startInfo.WorkingDirectory <- Path.GetDirectoryName(fullPath)
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        startInfo.ArgumentList.Add("msbuild")
        startInfo.ArgumentList.Add(fullPath)
        for argument in arguments do startInfo.ArgumentList.Add(argument)
        startInfo.ArgumentList.Add("-nologo")
        use evaluationProcess = Process.Start(startInfo)
        let output = evaluationProcess.StandardOutput.ReadToEnd()
        let errors = evaluationProcess.StandardError.ReadToEnd()
        evaluationProcess.WaitForExit()
        if evaluationProcess.ExitCode <> 0 then
            // MSBuild reports errors on either stream depending on the failure.
            let detail = (errors + Environment.NewLine + output).Trim()
            // A project outside the solution is never restored by a solution-level build, so this
            // failure usually means the project list includes something the solution does not build.
            if detail.Contains("NETSDK1004", StringComparison.Ordinal) then
                invalidOp
                    $"Project is not restored: {fullPath}\n\
                      Run 'dotnet restore \"{fullPath}\"' first. If this project is not part of your solution, \
                      a solution-level restore never covers it — check that you meant to pass it to livedocs."
            else
                invalidOp $"MSBuild evaluation failed for {fullPath}: {detail}"
        JObject.Parse(output)

    /// Runs an inner-build ResolveReferences target so package, project, framework, and SDK references all come from MSBuild evaluation.
    /// For a cross-targeting project, the first framework declared in TargetFrameworks is the documentation context.
    let evaluateProjectFor (targetFramework: string option) (projectPath: string) =
        let fullPath = Path.GetFullPath(projectPath)
        if not (File.Exists fullPath) then invalidOp $"Documentation project does not exist: {fullPath}"
        // ResolveReferences is supplied by the inner language build. A cross-targeting
        // outer build imports only the dispatch targets, so choose its first declared
        // framework before asking MSBuild for compiler references.
        let dimensions =
            runMsBuild fullPath [ "-getProperty:TargetFramework,TargetFrameworks" ]
        let dimensionProperties = dimensions.["Properties"]
        let declaredFrameworks =
            match readString dimensionProperties "TargetFramework", readString dimensionProperties "TargetFrameworks" with
            | Some framework, _ -> [ framework ]
            | None, Some frameworks ->
                frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries) |> Array.toList
            | None, None -> []
        let selectedFramework = targetFramework |> Option.orElseWith (fun () -> List.tryHead declaredFrameworks)
        match targetFramework with
        | Some requested when not (declaredFrameworks |> List.contains requested) ->
            let declared = String.concat ", " declaredFrameworks
            invalidOp $"Target framework '{requested}' is not declared by {fullPath}. Declared frameworks: {declared}."
        | _ -> ()
        let frameworkArgument =
            selectedFramework |> Option.map (fun framework -> $"-property:TargetFramework={framework}") |> Option.toList
        let json =
            runMsBuild
                fullPath
                (frameworkArgument
                 @ [ "-target:ResolveReferences"
                     "-getProperty:TargetFramework,TargetPath,LangVersion,DefineConstants,NoWarn,WarningsAsErrors"
                     "-getItem:ReferencePath" ])
        let properties = json.["Properties"]
        let property name = readString properties name
        let references =
            match json.SelectToken("Items.ReferencePath") with
            | :? JArray as items ->
                items
                |> Seq.choose (fun item -> readString item "FullPath" |> Option.orElseWith (fun () -> readString item "Identity"))
                |> Seq.filter File.Exists
                |> Seq.distinct
                |> Seq.toList
            | _ -> []
            |> fun resolved ->
                match property "TargetPath" with
                | Some target when File.Exists target -> target :: resolved
                | _ -> resolved
            |> List.distinct
        if references.IsEmpty then invalidOp $"MSBuild resolved no compiler references for {fullPath}."
        let defines =
            property "DefineConstants"
            |> Option.map (fun value -> value.Split([| ';'; ',' |], StringSplitOptions.RemoveEmptyEntries) |> Array.toList)
            |> Option.defaultValue []
        let other =
            [ for symbol in defines -> $"--define:{symbol}"
              match property "LangVersion" with Some value -> yield $"--langversion:{value}" | None -> ()
              match property "NoWarn" with Some value -> yield $"--nowarn:{value}" | None -> ()
              match property "WarningsAsErrors" with Some value when not (String.IsNullOrWhiteSpace value) -> yield $"--warnaserror:{value}" | _ -> () ]
        {
            ProjectPath = fullPath
            TargetFramework = property "TargetFramework" |> Option.defaultValue ""
            References = references
            Defines = defines
            LanguageVersion = property "LangVersion"
            OtherOptions = other
        }

    /// Evaluates the project's default documentation framework (the sole or first declared target).
    let evaluateProject projectPath = evaluateProjectFor None projectPath

    let private appendLines (writer: Text.StringBuilder) (value: string) =
        writer.Append(value) |> ignore
        if not (value.EndsWith("\n", StringComparison.Ordinal)) then writer.AppendLine() |> ignore

    let private syntheticSource (unit: CompilationUnit) =
        let source = Text.StringBuilder()
        let mutable line = 1
        let add value =
            appendLines source value
            let normalized = DocumentationDiscovery.normalizeSource value
            let newlines = normalized |> Seq.filter ((=) '\n') |> Seq.length
            line <- line + newlines + (if normalized.EndsWith("\n", StringComparison.Ordinal) then 0 else 1)
        if not (String.IsNullOrWhiteSpace unit.Prelude) then add unit.Prelude
        let ranges =
            [ for block in unit.Blocks do
                add $"// <livedocs-block id=\"{block.Id}\">"
                let startLine = line
                add block.ExpandedSource
                let sourceLineCount = DocumentationDiscovery.normalizeSource(block.ExpandedSource).TrimEnd('\n').Split('\n').Length
                let endLine = startLine + sourceLineCount - 1
                add "// </livedocs-block>"
                yield { Block = block; StartLine = startLine; EndLine = endLine } ]
        source.ToString(), ranges

    let private mapDiagnostic sourcePath ranges (diagnostic: FSharpDiagnostic) =
        let owning = ranges |> List.tryFind (fun range -> diagnostic.StartLine >= range.StartLine && diagnostic.StartLine <= range.EndLine)
        let relativeLine line range = max 1 (line - range.StartLine + 1)
        {
            BlockId = owning |> Option.map _.Block.Id
            SourcePath = sourcePath
            Severity =
                if diagnostic.Severity = FSharpDiagnosticSeverity.Error then SemanticDiagnosticSeverity.Error
                else SemanticDiagnosticSeverity.Warning
            Message = diagnostic.Message
            StartLine = owning |> Option.map (relativeLine diagnostic.StartLine) |> Option.defaultValue diagnostic.StartLine
            StartColumn = diagnostic.StartColumn
            EndLine = owning |> Option.map (relativeLine diagnostic.EndLine) |> Option.defaultValue diagnostic.EndLine
            EndColumn = diagnostic.EndColumn
        }

    let private checkerCount = min 4 (max 1 Environment.ProcessorCount)
    let private checkers = Array.init checkerCount (fun _ -> lazy FSharpChecker.Create(keepAssemblyContents = true))
    let private optionChecker = checkers.[0]
    let mutable private nextChecker = -1

    let private optionsCache = ConcurrentDictionary<string, Lazy<FSharpProjectOptions * FSharpDiagnostic list>>()

    let private projectOptionsTemplate (project: EvaluatedProject) (otherFlags: string array) =
        let key = String.concat "\u001f" (project.ProjectPath :: Array.toList otherFlags)
        optionsCache.GetOrAdd(
            key,
            fun _ ->
                lazy
                    let cacheName =
                        key
                        |> Text.Encoding.UTF8.GetBytes
                        |> Security.Cryptography.SHA256.HashData
                        |> Convert.ToHexString
                    let cacheFile = Path.Combine(Path.GetTempPath(), "fslivedocs", cacheName + ".fsx")
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)) |> ignore
                    optionChecker.Value.GetProjectOptionsFromScript(cacheFile, SourceText.ofString "", otherFlags = otherFlags)
                    |> Async.RunSynchronously).Value

    /// Checks one page or isolated unit. It never evaluates the resulting script.
    let checkUnit (project: EvaluatedProject) (unit: CompilationUnit) = async {
        let checker = checkers.[(Threading.Interlocked.Increment(&nextChecker) &&& Int32.MaxValue) % checkerCount].Value
        let source, ranges = syntheticSource unit
        let fileName = Path.Combine(Path.GetTempPath(), "fslivedocs", unit.Id.Replace('/', '_').Replace('#', '_') + ".fsx")
        let otherFlags =
            [ yield! project.OtherOptions
              for reference in project.References -> $"-r:{reference}" ]
            |> List.toArray
        let template, optionDiagnostics = projectOptionsTemplate project otherFlags
        let options = { template with ProjectFileName = unit.ProjectPath; SourceFiles = [| fileName |] }
        let! _, answer = checker.ParseAndCheckFileInProject(fileName, 0, SourceText.ofString source, options)
        let checkResults, checkDiagnostics =
            match answer with
            | FSharpCheckFileAnswer.Succeeded result -> Some result, result.Diagnostics
            | FSharpCheckFileAnswer.Aborted -> None, [||]
        let diagnostics =
            List.append optionDiagnostics (Array.toList checkDiagnostics)
            |> List.distinctBy (fun diagnostic -> diagnostic.ErrorNumber, diagnostic.StartLine, diagnostic.StartColumn, diagnostic.Message)
            |> List.map (mapDiagnostic (unit.Blocks |> List.tryHead |> Option.map _.SourcePath |> Option.defaultValue unit.Id) ranges)
        return { Unit = unit; SyntheticSource = source; Diagnostics = diagnostics; BlockRanges = ranges; CheckResults = checkResults }
    }

    let checkBlocks projectPath prelude blocks = async {
        let project = evaluateProject projectPath
        let units = DocumentationDiscovery.compilationUnits project.ProjectPath prelude blocks
        let! results = units |> List.map (checkUnit project) |> Async.Parallel
        return results |> Array.toList
    }

    let checkBlocksWithProject project prelude blocks = async {
        let units = DocumentationDiscovery.compilationUnits project.ProjectPath prelude blocks
        let! results = units |> List.map (checkUnit project) |> Async.Parallel
        return results |> Array.toList
    }
