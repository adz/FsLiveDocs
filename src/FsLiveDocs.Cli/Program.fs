namespace FsLiveDocs.Cli

open System
open System.IO
open Argu
open Spectre.Console
open FsLiveDocs.Core
open FsLiveDocs.Runner
open FsLiveDocs.Renderer
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.FileProviders
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

/// <summary>Defines the supported command-line arguments for the livedocs tool.</summary>
type Arguments =
    /// <summary>Scaffolds a new LiveDocs project structure.</summary>
    | [<CliPrefix(CliPrefix.None)>] Init
    /// <summary>Generates CI/CD templates for GitHub Actions.</summary>
    | [<CliPrefix(CliPrefix.None)>] CI
    /// <summary>Generates a Verify-based snapshot test project that calls into LiveDocs.</summary>
    | [<CliPrefix(CliPrefix.None); AltCommandLine("generate-tests")>] GenerateTests of projectPaths:string list
    /// <summary>Extracts symbol metadata from projects into JSON snapshots.</summary>
    | [<CliPrefix(CliPrefix.None)>] Extract of projectPaths:string list
    /// <summary>Runs verified code examples found in docstrings.</summary>
    | [<CliPrefix(CliPrefix.None)>] Test of projectPaths:string list
    /// <summary>Audits coverage and compiler-checks every expanded F# documentation block.</summary>
    | [<CliPrefix(CliPrefix.None)>] Audit of projectPaths:string list
    /// <summary>Builds the full static documentation site.</summary>
    | [<CliPrefix(CliPrefix.None)>] Build of projectPaths:string list
    /// <summary>Builds every version in a verified local history manifest.</summary>
    | [<CliPrefix(CliPrefix.None); AltCommandLine("build-history")>] BuildHistory of manifestPath:string
    /// <summary>Starts a development server with live-rebuild capabilities.</summary>
    | [<CliPrefix(CliPrefix.None)>] Watch of projectPaths:string list
    /// <summary>Sets the DaisyUI visual theme.</summary>
    | [<Inherit; AltCommandLine("-t")>] Theme of string
    /// <summary>Sets the version stored by API model extraction.</summary>
    | [<Inherit>] Version of string
    /// <summary>Sets the API model extraction output path.</summary>
    | [<Inherit>] Output of string
    /// <summary>Sets the network interface used by the preview server.</summary>
    | [<Inherit>] Host of string
    /// <summary>Sets the TCP port used by the preview server.</summary>
    | [<Inherit>] Port of int
    /// <summary>Adds directory names the preview watcher must not watch or rebuild for.</summary>
    | [<Inherit>] Ignore of string
    /// <summary>Fails the run when the documented API produces quality warnings.</summary>
    | [<Inherit; AltCommandLine("--warnaserror")>] Warn_As_Error
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Init -> "Scaffold a new LiveDocs project."
            | CI -> "Generate CI/CD templates (GitHub Actions)."
            | GenerateTests _ -> "Generate a Verify-based snapshot test project for the given projects."
            | Extract _ -> "Extract symbols from one or more projects into a JSON blob."
            | Test _ -> "Run the legacy direct docstring verifier for the given projects."
            | Audit _ -> "Audit coverage, modes, and compilation for every expanded F# documentation block."
            | Build _ -> "Render the final static site for the given projects."
            | BuildHistory _ -> "Render all versions from a verified local history manifest."
            | Watch _ -> "Start a dev server with file watching."
            | Theme _ -> "Set the visual theme (default: light)."
            | Version _ -> "Set the version stored by API model extraction."
            | Output _ -> "Set the API model extraction output path."
            | Host _ -> "Set the preview bind host (default: 0.0.0.0)."
            | Port _ -> "Set the preview port (default: 5000)."
            | Ignore _ -> "Add a comma-separated list of directory names the watcher ignores. Repeatable."
            | Warn_As_Error -> "Fail the run when the documented API produces quality warnings (default: warn only)."

/// <summary>Watches source and documentation files so the preview server rebuilds after an edit.</summary>
module internal PreviewWatcher =

    /// <summary>Directory names never watched, because they are generated, vendored, or private to a tool.</summary>
    let defaultIgnoredDirectories =
        set [ ".git"; ".vs"; ".idea"; ".livedocs"; "artifacts"; "bin"; "node_modules"; "obj"; "output"; "packages"; "TestResults" ]

    /// <summary>File extensions whose contents affect the generated site.</summary>
    let watchedExtensions = set [ ".css"; ".fs"; ".fsproj"; ".fsx"; ".md" ]

    /// <summary>Splits comma-separated <c>--ignore</c> values into directory names.</summary>
    let parseIgnored (values: string list) =
        values
        |> List.collect (fun value -> value.Split(',') |> Array.toList)
        |> List.map _.Trim()
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> Set.ofList

    let private isIgnored (ignored: Set<string>) (root: string) (fullPath: string) =
        let relative = Path.GetRelativePath(root, fullPath)
        relative.Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
        |> Array.exists (fun segment -> ignored.Contains segment)

    let private isRelevant (ignored: Set<string>) (root: string) (fullPath: string) =
        watchedExtensions.Contains(Path.GetExtension(fullPath).ToLowerInvariant())
        && not (isIgnored ignored root fullPath)

    /// <summary>Coalesces a burst of file events into a single rebuild.</summary>
    let private startRebuildPump (debounceMs: int) (rebuild: unit -> unit) =
        MailboxProcessor.Start(fun inbox ->
            let rec idle () =
                async {
                    let! (path: string) = inbox.Receive()
                    return! settle path
                }
            and settle path =
                async {
                    match! inbox.TryReceive(debounceMs) with
                    | Some next -> return! settle next
                    | None ->
                        AnsiConsole.MarkupLine($"[yellow]⚡ Change detected in {Markup.Escape(path)}, rebuilding...[/]")
                        try rebuild () with e -> AnsiConsole.WriteException(e)
                        return! idle ()
                }

            idle ())

    let private createWatcher (root: string) (path: string) (recurse: bool) (ignored: Set<string>) (notify: string -> unit) =
        let watcher = new FileSystemWatcher(path)
        watcher.IncludeSubdirectories <- recurse
        watcher.NotifyFilter <- NotifyFilters.FileName ||| NotifyFilters.DirectoryName ||| NotifyFilters.LastWrite
        let handle (fullPath: string) =
            if isRelevant ignored root fullPath then
                notify (Path.GetRelativePath(root, fullPath))
        watcher.Changed.Add(fun e -> handle e.FullPath)
        watcher.Created.Add(fun e -> handle e.FullPath)
        watcher.Deleted.Add(fun e -> handle e.FullPath)
        watcher.Renamed.Add(fun e -> handle e.FullPath)
        watcher.Error.Add(fun e ->
            let error = e.GetException()
            AnsiConsole.MarkupLine($"[red]Watcher stopped for {Markup.Escape(path)}: {Markup.Escape(error.Message)}[/]")
            if error.Message.Contains("inotify") then
                AnsiConsole.MarkupLine("[yellow]Raise the limit with:[/] [blue]echo fs.inotify.max_user_watches=524288 | sudo tee -a /etc/sysctl.conf && sudo sysctl -p[/]")
                AnsiConsole.MarkupLine("[yellow]Or exclude more directories with[/] --ignore <names>")
            try
                watcher.EnableRaisingEvents <- false
                watcher.EnableRaisingEvents <- true
                AnsiConsole.MarkupLine($"[grey]Watching {Markup.Escape(path)} again.[/]")
            with _ ->
                AnsiConsole.MarkupLine($"[red]Could not restart the watcher for {Markup.Escape(path)}. Restart the preview.[/]"))
        watcher.EnableRaisingEvents <- true
        watcher

    /// <summary>
    /// Starts one watcher per top-level source directory. Ignored directories are never watched, so they cost no
    /// operating-system watch handles. The returned watchers must stay referenced for as long as the preview runs;
    /// a collected watcher stops raising events.
    /// </summary>
    let start (root: string) (extraIgnored: Set<string>) (rebuild: unit -> unit) =
        let ignored = Set.union defaultIgnoredDirectories extraIgnored
        let pump = startRebuildPump 400 rebuild
        let watched =
            Directory.GetDirectories(root)
            |> Array.map Path.GetFileName
            |> Array.filter (fun name -> not (ignored.Contains name))
            |> Array.sort
        let watchers =
            [ yield createWatcher root root false ignored pump.Post
              for name in watched -> createWatcher root (Path.Combine(root, name)) true ignored pump.Post ]
        let watchedNames = String.Join(", ", watched)
        let ignoredNames = String.Join(", ", Set.toList ignored)
        AnsiConsole.MarkupLine($"   [grey]Watching:[/] {Markup.Escape(watchedNames)}")
        AnsiConsole.MarkupLine($"   [grey]Ignoring:[/] {Markup.Escape(ignoredNames)}")
        watchers

/// <summary>The main entry point module for the CLI application.</summary>
module Program =

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None; FSharpPrelude = None }

    let printBanner () =
        let figlet = FigletText("LiveDocs")
        figlet.Color <- Color.Blue
        AnsiConsole.Write(figlet)
        AnsiConsole.MarkupLine("[grey]Verified Documentation for F#[/]\n")

    /// <summary>Loads and merges multiple project models into a unified package.</summary>
    let getUnifiedPackage (projectPaths: string list) = async {
        let packages = ResizeArray()
        let diagnostics = ResizeArray()
        for projectPath in projectPaths do
            let! package, projectDiagnostics = SymbolLister.extractFromProjectWithDiagnostics projectPath
            packages.Add(package)
            diagnostics.AddRange(projectDiagnostics)
        return SymbolLister.merge (Seq.toList packages), List.ofSeq diagnostics
    }

    let loadSiteConfig () =
        let configPath = Path.Combine(".livedocs", "config.json")
        if File.Exists(configPath) then
            try
                let config = Newtonsoft.Json.JsonConvert.DeserializeObject<SiteConfig>(File.ReadAllText(configPath), FsLiveDocs.Core.Serialization.jsonSettings)
                if isNull (box config) then defaultSiteConfig else config
            with _ ->
                defaultSiteConfig
        else
            defaultSiteConfig

    let private writeIfChanged (path: string) (content: string) =
        let normalized = content.Replace("\r\n", "\n").TrimEnd() + "\n"
        let shouldWrite =
            if File.Exists(path) then
                File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd() + "\n" <> normalized
            else
                true

        if shouldWrite then
            let dir = Path.GetDirectoryName(path)
            if not (String.IsNullOrWhiteSpace dir) && not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore
            File.WriteAllText(path, normalized)

    type private DocumentationAnalysis = {
        Blocks: DocumentationBlock list
        Results: CheckedCompilationUnit list
        Prelude: string
        CachedArtifact: SemanticDocumentationArtifact option
        CachePath: string
    }

    let private sha256Text (value: string) =
        value
        |> Text.Encoding.UTF8.GetBytes
        |> Security.Cryptography.SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private projectInputFingerprint (projectPaths: string list) =
        let root = Directory.GetCurrentDirectory()
        let ignoredSegments = set [ ".git"; ".livedocs"; "artifacts"; "bin"; "obj"; "output" ]
        let isIgnored (path: string) =
            Path.GetRelativePath(root, path).Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
            |> Array.exists ignoredSegments.Contains
        let projectFiles =
            projectPaths
            |> List.collect (fun projectPath ->
                let fullPath = Path.GetFullPath(projectPath)
                let directory = Path.GetDirectoryName(fullPath)
                fullPath :: (Directory.GetFiles(directory, "*.fs", SearchOption.AllDirectories) |> Array.toList))
        let repositoryInputs =
            [ "Directory.Build.props"; "Directory.Build.targets"; "Directory.Packages.props"; "global.json"; "NuGet.config" ]
            |> List.map (fun path -> Path.Combine(root, path))
            |> List.filter File.Exists
        projectFiles @ repositoryInputs
        |> List.filter (isIgnored >> not)
        |> List.distinct
        |> List.sort
        |> List.collect (fun path -> [ Path.GetRelativePath(root, path).Replace('\\', '/'); File.ReadAllText(path) ])
        |> String.concat "\n--fslivedocs-project-input--\n"
        |> sha256Text

    let private writeCurrentCache (path: string) (pattern: string) (value: string) =
        let directory = Path.GetDirectoryName(path)
        Directory.CreateDirectory(directory) |> ignore
        File.WriteAllText(path, value)
        for stale in Directory.GetFiles(directory, pattern) do
            if not (Path.GetFullPath(stale).Equals(Path.GetFullPath(path), StringComparison.Ordinal)) then File.Delete(stale)

    let private getUnifiedPackageCached (projectPaths: string list) =
        let inputHash = projectInputFingerprint projectPaths
        let cacheDirectory = Path.GetFullPath(Path.Combine(".livedocs", "cache"))
        let cacheKey = sha256Text $"api-schema:{History.ApiModelSchemaVersion}|extractor:{typeof<PackageModel>.Assembly.ManifestModule.ModuleVersionId}|{inputHash}"
        let cachePath = Path.Combine(cacheDirectory, cacheKey + ".package.json")
        // Diagnostics describe the run, not the snapshot, so they live beside the cached package
        // rather than inside it — otherwise a warning would be reported once and never again.
        let diagnosticsPath = Path.Combine(cacheDirectory, cacheKey + ".diagnostics.json")
        if File.Exists cachePath then
            let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(File.ReadAllText(cachePath), FsLiveDocs.Core.Serialization.jsonSettings)
            if isNull (box package) then invalidOp $"Invalid cached package model: {cachePath}"
            let diagnostics =
                if File.Exists diagnosticsPath then
                    Newtonsoft.Json.JsonConvert.DeserializeObject<ApiDiagnostic list>(File.ReadAllText(diagnosticsPath), FsLiveDocs.Core.Serialization.jsonSettings)
                    |> Option.ofObj
                    |> Option.defaultValue []
                else []
            package, diagnostics, inputHash
        else
            let package, diagnostics = getUnifiedPackage projectPaths |> Async.RunSynchronously
            writeCurrentCache cachePath "*.package.json" (Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
            writeCurrentCache diagnosticsPath "*.diagnostics.json" (Newtonsoft.Json.JsonConvert.SerializeObject(diagnostics, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
            package, diagnostics, inputHash

    /// <summary>
    /// Reports API-quality warnings, grouped by the file that declares them.
    /// </summary>
    /// <remarks>
    /// These never block a build by default. The documentation still renders correctly, and the
    /// author may not be free to change the API being documented, so a first run must not fail on
    /// them. <c>--warn-as-error</c> is for projects that have chosen to hold the line.
    /// </remarks>
    let private printApiDiagnostics (warnAsError: bool) (diagnostics: ApiDiagnostic list) =
        if diagnostics.IsEmpty then
            0
        else
            let label = if warnAsError then "[red]error[/]" else "[yellow]warning[/]"
            let root = Directory.GetCurrentDirectory()
            let relative (path: string) =
                if String.IsNullOrWhiteSpace path then "(unknown source)"
                elif Path.IsPathRooted path then Path.GetRelativePath(root, path).Replace('\\', '/')
                else path.Replace('\\', '/')

            AnsiConsole.MarkupLine("")
            for file, items in diagnostics |> List.groupBy (fun d -> relative d.Location.File) do
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape file}[/]")
                for item in items |> List.sortBy (fun d -> d.Location.Line) do
                    let symbol = Markup.Escape item.Symbol
                    AnsiConsole.MarkupLine($"  {label} [grey]{item.Location.Line}[/] {symbol} [grey]({Markup.Escape item.Code})[/]")
                    AnsiConsole.MarkupLine($"        {Markup.Escape item.Message}")
                    AnsiConsole.MarkupLine($"        [grey]{Markup.Escape item.Remedy}[/]")

            let count = diagnostics.Length
            let noun = if count = 1 then "warning" else "warnings"
            if warnAsError then
                let verb = if count = 1 then "treated as an error" else "treated as errors"
                AnsiConsole.MarkupLine($"\n[red]✖ {count} API documentation {noun} {verb} (--warn-as-error).[/]")
                count
            else
                AnsiConsole.MarkupLine($"\n[yellow]⚠ {count} API documentation {noun}.[/] [grey]Documentation still rendered; pass --warn-as-error to fail on these.[/]")
                0

    let private analyzeDocumentation (projectPaths: string list) (projectFingerprint: string) (package: PackageModel) =
        if List.isEmpty projectPaths then invalidOp "Documentation analysis requires at least one project path."
        let docsDir = Path.GetFullPath("docs")
        if not (Directory.Exists docsDir) then invalidOp $"Documentation directory is missing: {docsDir}"
        let sourceDir = Directory.GetCurrentDirectory()
        let resolvedProjects = projectPaths |> List.map Path.GetFullPath
        let defaultProject = List.head resolvedProjects
        let prelude = loadSiteConfig().FSharpPrelude |> Option.defaultValue ""
        let pages =
            [ for path in Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories) |> Array.sort do
                let relative = Path.GetRelativePath(docsDir, path).Replace('\\', '/')
                let raw = File.ReadAllText(path)
                let frontMatter = ContentProvider.parseFrontMatter raw
                let body = frontMatter |> Option.map snd |> Option.defaultValue raw
                let selectedProject =
                    match frontMatter |> Option.bind (fun (metadata, _) -> metadata.Project) with
                    | None -> defaultProject
                    | Some configured ->
                        [ Path.GetFullPath(configured, sourceDir); Path.GetFullPath(configured, docsDir) ]
                        |> List.tryFind File.Exists
                        |> Option.defaultWith (fun () -> invalidOp $"Documentation project in {relative} does not exist: {configured}")
                if not (resolvedProjects |> List.contains selectedProject) then
                    // Naming what was passed turns this from "something is wrong" into a diff the
                    // caller can act on: the usual cause is an incomplete project list.
                    let describe (path: string) = Path.GetRelativePath(sourceDir, path).Replace('\\', '/')
                    let passed =
                        resolvedProjects
                        |> List.map (fun project -> "  " + describe project)
                        |> String.concat "\n"
                    invalidOp
                        $"Documentation page {relative} selects {describe selectedProject}, but that project was not passed to livedocs.\n\
                          Projects passed:\n{passed}\n\
                          Add the selected project to the command, or change the 'project:' front matter on that page."
                let expanded = ContentProvider.resolveSnippets body sourceDir package ""
                let blocks = DocumentationDiscovery.discoverMarkdown relative (Some selectedProject) expanded
                DocumentationDiscovery.validateCoverage blocks
                let platform = frontMatter |> Option.bind (fun (metadata, _) -> metadata.Platform) |> Option.map _.ToLowerInvariant()
                match platform with
                | Some "fable" when blocks |> List.exists (fun block -> match block.Mode with NoCheck _ | Transcript -> false | _ -> true) ->
                    invalidOp $"Documentation page {relative} declares platform: fable, but FsLiveDocs cannot yet invoke the Fable compiler. Mark each F# block no-check with a reason or transclude code covered by a Fable build gate."
                | Some value when value <> "dotnet" && value <> "fable" -> invalidOp $"Documentation page {relative} declares unsupported platform '{value}'."
                | _ -> ()
                let targetFramework = frontMatter |> Option.bind (fun (metadata, _) -> metadata.TargetFramework)
                yield blocks, selectedProject, targetFramework ]
        let blocks = pages |> List.collect (fun (blocks, _, _) -> blocks)
        let packageFingerprint = Newtonsoft.Json.JsonConvert.SerializeObject(package, FsLiveDocs.Core.Serialization.jsonSettings)
        let contextFingerprint =
            [ yield $"semantic-schema:{History.SemanticSchemaVersion}"
              yield $"compiler-mvid:{typeof<EvaluatedProject>.Assembly.ManifestModule.ModuleVersionId}"
              yield $"project-inputs:{projectFingerprint}"
              yield $"prelude:{prelude}"
              yield packageFingerprint
              for blocks, project, targetFramework in pages do
                  let framework = targetFramework |> Option.defaultValue "<default>"
                  yield $"project:{project}|framework:{framework}"
                  for block in blocks do yield $"block:{block.Id}|{block.SourceHash}" ]
            |> String.concat "\n"
        let cacheDirectory = Path.Combine(".livedocs", "cache")
        let cachePath = Path.Combine(cacheDirectory, sha256Text contextFingerprint + ".semantic.json") |> Path.GetFullPath
        let cachedArtifact =
            if File.Exists cachePath then
                let artifact = Newtonsoft.Json.JsonConvert.DeserializeObject<SemanticDocumentationArtifact>(File.ReadAllText(cachePath), FsLiveDocs.Core.Serialization.jsonSettings)
                if isNull (box artifact) || artifact.SchemaVersion <> History.SemanticSchemaVersion then None else Some artifact
            else None
        let results =
            match cachedArtifact with
            | Some _ -> []
            | None ->
                let evaluated = resolvedProjects |> List.map (fun path -> path, DocumentationCompiler.evaluateProject path)
                let aggregateReferences = evaluated |> List.collect (snd >> _.References) |> List.distinct
                let evaluatedProjects = evaluated |> List.map (fun (path, project) -> path, { project with References = aggregateReferences }) |> Map.ofList
                pages
                |> List.map (fun (blocks, selectedProject, targetFramework) ->
                    let selectedEvaluation =
                        match targetFramework with
                        | None -> evaluatedProjects.[selectedProject]
                        | Some _ ->
                            let selected = DocumentationCompiler.evaluateProjectFor targetFramework selectedProject
                            let references = selected.References @ aggregateReferences |> List.distinctBy (Path.GetFileName >> _.ToUpperInvariant())
                            { selected with References = references }
                    DocumentationCompiler.checkBlocksWithProject selectedEvaluation prelude blocks)
                |> fun checks -> Async.Parallel(checks, maxDegreeOfParallelism = max 1 Environment.ProcessorCount)
                |> Async.RunSynchronously
                |> Array.toList
                |> List.collect id
        DocumentationDiscovery.validateCoverage blocks
        { Blocks = blocks; Results = results; Prelude = prelude; CachedArtifact = cachedArtifact; CachePath = cachePath }

    let private printAudit (analysis: DocumentationAnalysis) =
        let diagnosticsByBlock =
            match analysis.CachedArtifact with
            | Some artifact ->
                artifact.Pages
                |> List.collect _.Blocks
                |> List.collect (fun block ->
                    block.Diagnostics
                    |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
                    |> List.map (fun diagnostic -> block.Id, (diagnostic.StartLine, diagnostic.StartColumn, diagnostic.Message)))
                |> List.groupBy fst
                |> Map.ofList
            | None ->
                analysis.Results
                |> List.collect _.Diagnostics
                |> List.filter (fun item -> item.Severity = SemanticDiagnosticSeverity.Error)
                |> List.choose (fun item -> item.BlockId |> Option.map (fun id -> id, (item.StartLine, item.StartColumn, item.Message)))
                |> List.groupBy fst
                |> Map.ofList
        let mutable failures = 0
        for block in analysis.Blocks do
            let errors = diagnosticsByBlock |> Map.tryFind block.Id |> Option.defaultValue [] |> List.map snd
            let status, detail =
                if not errors.IsEmpty then
                    failures <- failures + 1
                    let line, column, message = List.head errors
                    "FAIL", $"{line}:{column} {message}"
                else
                    match block.Mode with
                    | Page -> "PASS", "page"
                    | Prepare -> "PASS", "prepare (shared setup)"
                    | Isolated -> "PASS", "isolated"
                    | Run -> "PASS", "run (compiled; execution is explicit)"
                    | Transcript -> "PASS", "transcript (explicit execution case)"
                    | NoCheck reason -> "EXCLUDED", reason
            let color = if status = "PASS" then "green" elif status = "FAIL" then "red" else "yellow"
            AnsiConsole.MarkupLine($"[{color}]{status,-8}[/] {Markup.Escape(block.Id)} ({Markup.Escape(detail)})")
        if failures = 0 then
            AnsiConsole.MarkupLine($"\n[green]✔ Audit complete:[/] {analysis.Blocks.Length} expanded F# block(s), all covered or explicitly excluded.")
        else
            AnsiConsole.MarkupLine($"\n[red]✖ Audit failed:[/] {failures} of {analysis.Blocks.Length} expanded F# block(s) contain compiler errors.")
        failures

    let auditAction (warnAsError: bool) (projectPaths: string list) =
        if List.isEmpty projectPaths then invalidOp "Audit requires at least one project path."
        let package, diagnostics, projectFingerprint = getUnifiedPackageCached projectPaths
        let analysis = analyzeDocumentation projectPaths projectFingerprint package
        let blockFailures = printAudit analysis
        let apiFailures = printApiDiagnostics warnAsError diagnostics
        if blockFailures = 0 && apiFailures = 0 then 0 else 1

    let private createSemanticArtifact (projectPaths: string list) (package: PackageModel) =
        let analysis = analyzeDocumentation projectPaths (projectInputFingerprint projectPaths) package
        let artifact = analysis.CachedArtifact |> Option.defaultWith (fun () -> SemanticExtractor.artifact analysis.Results)
        if analysis.CachedArtifact.IsNone then
            writeCurrentCache analysis.CachePath "*.semantic.json" (Newtonsoft.Json.JsonConvert.SerializeObject(artifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
        artifact, analysis.Prelude

    let private generateSnapshotTests (projectPaths: string list) =
        printBanner()

        if List.isEmpty projectPaths then
            AnsiConsole.MarkupLine("[red]No project paths were provided.[/]")
            1
        else
            let outputDir = Path.GetFullPath("tests/FsLiveDocs.SnapshotTests")
            if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore
            let eol = Environment.NewLine

            let resolvedProjects =
                projectPaths
                |> List.map Path.GetFullPath

            let relativeProjects =
                resolvedProjects
                |> List.map (fun projectPath -> Path.GetRelativePath(outputDir, projectPath).Replace('\\', '/'))

            let projectRefs =
                relativeProjects
                |> List.map (fun relative -> $"    <ProjectReference Include=\"{relative}\" />")
                |> String.concat eol

            let toolReferences =
                [ "FsLiveDocs.Core", typeof<PackageModel>.Assembly.Location
                  "FsLiveDocs.Runner", typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly.Location ]
                |> List.map (fun (name, path) -> $"    <Reference Include=\"{name}\"><HintPath>{System.Security.SecurityElement.Escape(path)}</HintPath></Reference>")
                |> String.concat eol

            let allAssemblyPaths =
                resolvedProjects
                |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                |> List.filter (String.IsNullOrWhiteSpace >> not)
                |> List.distinct

            let assemblyReferenceLiteral =
                allAssemblyPaths
                |> List.map (fun path -> "@\"" + path.Replace("\"", "\"\"") + "\"")
                |> String.concat "; "

            let fsproj =
                [
                    """<Project Sdk="Microsoft.NET.Sdk">"""
                    ""
                    "  <PropertyGroup>"
                    "    <TargetFramework>net10.0</TargetFramework>"
                    "    <IsPackable>false</IsPackable>"
                    "  </PropertyGroup>"
                    ""
                    "  <ItemGroup>"
                    "    <Compile Include=\"SnapshotTests.fs\" />"
                    "  </ItemGroup>"
                    ""
                    "  <ItemGroup>"
                    "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />"
                    "    <PackageReference Include=\"FSharp.Core\" Version=\"10.1.201\" />"
                    "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />"
                    "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\" />"
                    "    <PackageReference Include=\"Verify.Xunit\" Version=\"31.12.5\" />"
                    "  </ItemGroup>"
                    ""
                    "  <ItemGroup>"
                    projectRefs
                    toolReferences
                    "  </ItemGroup>"
                    ""
                    "</Project>"
                ]
                |> String.concat eol

            let projectExamples =
                resolvedProjects
                |> List.map (fun projectPath ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    projectPath, DocTestRunner.snapshotExampleNames package)

            let testBodies =
                projectExamples
                |> List.mapi (fun index (projectPath, exampleNames) ->
                    let projectName = Path.GetFileNameWithoutExtension(projectPath)
                    let escapedProjectPath = Path.GetFullPath(projectPath).Replace("\"", "\"\"")
                    let packageName = $"xmlPackage{index}"
                    let facts =
                        exampleNames
                        |> List.map (fun exampleName ->
                            let escapedName = exampleName.Replace("`", "'").Replace("\"", "\"\"")
                            [ ""
                              "    [<Fact>]"
                              $"    let ``xml {projectName}#example-{escapedName}`` () ="
                              "        task {"
                              $"            let projectPath = @\"{escapedProjectPath}\""
                              $"            let references = [ {assemblyReferenceLiteral} ]"
                              $"            let! snapshot = DocTestRunner.collectSnapshotByName {packageName}.Value projectPath references @\"{escapedName}\""
                              "            return! Verifier.Verify(snapshot)"
                              "        }" ]
                            |> String.concat eol)
                        |> String.concat eol
                    [ $"    let private {packageName} = lazy (SymbolLister.extractFromProject @\"{escapedProjectPath}\" |> Async.RunSynchronously)"
                      facts ]
                    |> String.concat eol)
                |> String.concat (eol + eol)

            let package, _ = getUnifiedPackage resolvedProjects |> Async.RunSynchronously
            let docsDir = Path.GetFullPath("docs")
            let sourceDir = Directory.GetCurrentDirectory()
            let defaultProject = List.head resolvedProjects
            let documentationCases =
                [ for path in Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories) |> Array.sort do
                    let relative = Path.GetRelativePath(docsDir, path).Replace('\\', '/')
                    let raw = File.ReadAllText(path)
                    let frontMatter = ContentProvider.parseFrontMatter raw
                    let body = frontMatter |> Option.map snd |> Option.defaultValue raw
                    let selectedProject =
                        match frontMatter |> Option.bind (fun (metadata, _) -> metadata.Project) with
                        | None -> defaultProject
                        | Some configured ->
                            [ Path.GetFullPath(configured, sourceDir); Path.GetFullPath(configured, docsDir) ]
                            |> List.tryFind File.Exists
                            |> Option.defaultWith (fun () -> invalidOp $"Documentation project in {relative} does not exist: {configured}")
                    let expanded = ContentProvider.resolveSnippets body sourceDir package ""
                    let encoded = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes expanded)
                    let blocks = DocumentationDiscovery.discoverMarkdown relative (Some selectedProject) expanded
                    DocumentationDiscovery.validateCoverage blocks
                    yield "coverage", relative + "#coverage", selectedProject, relative, encoded
                    for unit in DocumentationDiscovery.compilationUnits selectedProject "" blocks do
                        yield "compile", unit.Id, selectedProject, relative, encoded
                    for block in blocks do
                        match block.Mode, block.Origin with
                        | (Run | Transcript), XmlExample -> () // The existing named XML snapshot case owns this execution.
                        | (Run | Transcript), _ -> yield "execute", block.Id, selectedProject, relative, encoded
                        | _ -> () ]

            let documentationTestBodies =
                documentationCases
                |> List.map (fun (action, id, projectPath, sourcePath, encoded) ->
                    let escapedProject = Path.GetFullPath(projectPath).Replace("\"", "\"\"")
                    let escapedSource = sourcePath.Replace("\"", "\"\"")
                    let escapedId = id.Replace("`", "'").Replace("\"", "\"\"")
                    [ ""
                      "    [<Fact>]"
                      $"    let ``{action} {escapedId}`` () ="
                      $"        let projectPath = @\"{escapedProject}\""
                      $"        let references = [ {assemblyReferenceLiteral} ]"
                      $"        let markdown = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(\"{encoded}\"))"
                      if action = "coverage" then
                          $"        GeneratedVerification.validateCoverage projectPath @\"{escapedSource}\" markdown"
                      elif action = "compile" then
                          $"        GeneratedVerification.verifyCompilationUnit projectPath references @\"{escapedSource}\" markdown @\"{id}\" |> Async.RunSynchronously"
                      else
                          $"        GeneratedVerification.executeBlock projectPath references @\"{escapedSource}\" markdown @\"{id}\"" ]
                    |> String.concat eol)
                |> String.concat eol

            let testsFs =
                [
                    "namespace FsLiveDocs.SnapshotTests"
                    ""
                    "open System.Threading.Tasks"
                    "open FsLiveDocs.Core"
                    "open FsLiveDocs.Runner"
                    "open VerifyXunit"
                    "open Xunit"
                    ""
                    "module SnapshotTests ="
                    testBodies
                    documentationTestBodies
                ]
                |> String.concat eol

            let fsprojPath = Path.Combine(outputDir, "FsLiveDocs.SnapshotTests.fsproj")
            let testsPath = Path.Combine(outputDir, "SnapshotTests.fs")

            writeIfChanged fsprojPath fsproj
            writeIfChanged testsPath testsFs

            AnsiConsole.MarkupLine($"[green]✔ Snapshot test project generated:[/] {outputDir}")
            0

    /// <summary>Orchestrates the build process for one or more projects.</summary>
    let buildAction (warnAsError: bool) (projectPaths: string list) (theme: string) (version: string option) =
        let extracted, apiDiagnostics, projectFingerprint = getUnifiedPackageCached projectPaths
        let packageRaw = { extracted with Version = version |> Option.defaultValue extracted.Version }
        let analysis = analyzeDocumentation projectPaths projectFingerprint packageRaw
        if printAudit analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before building."
        if printApiDiagnostics warnAsError apiDiagnostics <> 0 then
            invalidOp "API documentation warnings were treated as errors because --warn-as-error was passed."
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[blue]Building documentation site...[/]", fun ctx ->
                let semanticArtifact = analysis.CachedArtifact |> Option.defaultWith (fun () -> SemanticExtractor.artifact analysis.Results)
                if analysis.CachedArtifact.IsNone then
                    writeCurrentCache analysis.CachePath "*.semantic.json" (Newtonsoft.Json.JsonConvert.SerializeObject(semanticArtifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
                let sourceDir = Directory.GetCurrentDirectory() 
                let semanticCode =
                    {
                        SemanticCode.defaults with
                            Artifact = Some semanticArtifact
                            Prelude = analysis.Prelude
                    }
                let package = ContentProvider.applyApiDocsWithOptions "docs" sourceDir packageRaw semanticCode
                let pages = ContentProvider.scanDocsWithOptions "docs" sourceDir package "" semanticCode
                let config = loadSiteConfig()
                
                let historyDir = ".livedocs/history"
                if not (Directory.Exists(historyDir)) then Directory.CreateDirectory(historyDir) |> ignore
                
                SiteBuilder.buildAll historyDir package pages config theme "output"
                ContentProvider.copyStaticFiles "docs" "output"
                
                let psi = System.Diagnostics.ProcessStartInfo("npx", "-y pagefind --site output")
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                let proc = System.Diagnostics.Process.Start(psi)
                proc.WaitForExit()
            )
        AnsiConsole.MarkupLine("[green]✔ Build complete:[/] output/")

    let buildHistoryAction manifestPath theme =
        let manifest, entries = History.loadManifest manifestPath
        let config = loadSiteConfig()
        let sites =
            entries
            |> List.map (fun (entry, modelPath, docsDir) ->
                if not (Directory.Exists(docsDir)) then
                    invalidOp $"History docs tree is missing for {entry.Version}: {docsDir}"
                let packageRaw = History.loadArtifact entry.Version entry.ModelSha256 modelPath
                let sourceDir = Path.GetDirectoryName(docsDir)
                let semanticCode =
                    match entry.SemanticPath, entry.SemanticSha256 with
                    | Some semanticPath, Some checksum ->
                        let manifestRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                        let artifact = History.loadSemanticArtifact checksum (Path.GetFullPath(Path.Combine(manifestRoot, semanticPath)))
                        { SemanticCode.defaults with Artifact = Some artifact; Prelude = artifact.Prelude }
                    | _ -> SemanticCode.disabled
                let package = ContentProvider.applyApiDocsWithOptions docsDir sourceDir packageRaw semanticCode
                let rootPath = if entry.Version = manifest.CurrentVersion then "" else "../../"
                let pages = ContentProvider.scanDocsWithOptions docsDir sourceDir package rootPath semanticCode
                entry.Version, package, pages, docsDir)

        SiteBuilder.buildHistory manifest.CurrentVersion sites config theme "output"

        let psi = System.Diagnostics.ProcessStartInfo("npx", "-y pagefind --site output")
        psi.UseShellExecute <- false
        use proc = System.Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        if proc.ExitCode <> 0 then invalidOp $"Pagefind failed with exit code {proc.ExitCode}."
        AnsiConsole.MarkupLine("[green]✔ History build complete:[/] output/")

    /// <summary>CLI entry point.</summary>
    [<EntryPoint>]
    let main args =
        let parser = ArgumentParser.Create<Arguments>(programName = "livedocs")
        
        let printUsage (msg: string option) =
            printBanner()
            if msg.IsSome then AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(msg.Value)}[/]\n")
            AnsiConsole.WriteLine(parser.PrintUsage())

        if args.Length = 0 then
            printUsage None
            0
        else
            try
                let results = parser.Parse(args)
                let theme = results.GetResult(Theme, defaultValue = "light")
                
                if results.Contains Init then
                    printBanner()
                    AnsiConsole.MarkupLine("[blue]Scaffolding new project...[/]")
                    if not (Directory.Exists(".livedocs/history")) then Directory.CreateDirectory(".livedocs/history") |> ignore
                    if not (Directory.Exists(".livedocs")) then Directory.CreateDirectory(".livedocs") |> ignore
                    if not (File.Exists(".livedocs/config.json")) then File.WriteAllText(".livedocs/config.json", "{}")
                    if not (Directory.Exists("docs")) then Directory.CreateDirectory("docs") |> ignore
                    if not (File.Exists("docs/index.md")) then
                        let starter = """---
title: Home
weight: 1
---

# Your verified F# documentation

FsLiveDocs builds API reference pages from your compiled project and checks F# guide examples against the same
compiler context. Readers get inferred-type and XML-documentation hovers without running a compiler in the browser.

## First useful build

Replace the project path below, then run from your repository root:

```bash
dotnet build
livedocs audit src/YourLibrary/YourLibrary.fsproj
livedocs build src/YourLibrary/YourLibrary.fsproj
livedocs watch src/YourLibrary/YourLibrary.fsproj --host 127.0.0.1 --port 5000
```

Add an ordinary `fsharp` fence to a guide for compile-only verification. Use `run` only for intentional execution,
`transcript` for FSI input/output, `isolated` for standalone code, `prepare` for hidden setup, or
`no-check reason="..."` for deliberate pseudocode.

Next: add XML `summary` and `example` elements to public APIs, then generate stable xUnit cases with
`livedocs generate-tests src/YourLibrary/YourLibrary.fsproj`.
"""
                        File.WriteAllText("docs/index.md", starter)
                    AnsiConsole.MarkupLine("[green]✔ Done![/]")
                    0

                elif results.Contains CI then
                    printBanner()
                    AnsiConsole.MarkupLine("[blue]Generating GitHub Actions workflow...[/]")
                    if not (Directory.Exists(".github/workflows")) then Directory.CreateDirectory(".github/workflows") |> ignore
                    let workflow = """
name: LiveDocs
on:
  pull_request:
  push:
    branches: [ main ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: dotnet tool restore
      - name: Verify and build documentation
        run: |
          mapfile -t projects < <(find src -name '*.fsproj' -type f | sort)
          test ${#projects[@]} -gt 0
          dotnet build --nologo
          dotnet livedocs generate-tests "${projects[@]}"
          dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj --nologo
          dotnet livedocs build "${projects[@]}"
      - uses: actions/upload-pages-artifact@v3
        with:
          path: output
"""
                    File.WriteAllText(".github/workflows/livedocs.yml", workflow)
                    AnsiConsole.MarkupLine("[green]✔ Done:[/] .github/workflows/livedocs.yml")
                    0

                elif results.Contains GenerateTests then
                    let projectPaths = results.GetResult GenerateTests
                    generateSnapshotTests projectPaths

                elif results.Contains Extract then
                    printBanner()
                    let projectPaths = results.GetResult Extract
                    let mutable extractDiagnostics = []
                    AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                        let packageRaw, apiDiagnostics = getUnifiedPackage projectPaths |> Async.RunSynchronously
                        extractDiagnostics <- apiDiagnostics
                        let version = results.GetResult(Version, defaultValue = packageRaw.Version)
                        let package = { packageRaw with Version = version }
                        let artifact : ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
                        let json = Newtonsoft.Json.JsonConvert.SerializeObject(artifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings)
                        let fileName = results.GetResult(Output, defaultValue = $".livedocs/models/{version}.json")
                        let outputDirectory = Path.GetDirectoryName(fileName)
                        if not (String.IsNullOrWhiteSpace outputDirectory) && not (Directory.Exists(outputDirectory)) then
                            Directory.CreateDirectory(outputDirectory) |> ignore
                        File.WriteAllText(fileName, json)
                        let semanticArtifact, _ = createSemanticArtifact projectPaths package
                        let semanticJson = Newtonsoft.Json.JsonConvert.SerializeObject(semanticArtifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings)
                        let semanticDirectory = Path.GetDirectoryName(fileName) |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not) |> Option.defaultValue "."
                        let outputStem = Path.GetFileNameWithoutExtension(fileName)
                        let semanticStem = if outputStem.EndsWith(".api", StringComparison.OrdinalIgnoreCase) then outputStem.Substring(0, outputStem.Length - 4) else outputStem
                        let semanticFileName = Path.Combine(semanticDirectory, semanticStem + ".semantic.json")
                        File.WriteAllText(semanticFileName, semanticJson)
                    )
                    AnsiConsole.MarkupLine("[green]✔ API and semantic documentation extraction complete.[/]")
                    printApiDiagnostics (results.Contains Warn_As_Error) extractDiagnostics

                elif results.Contains Test then
                    printBanner()
                    let projectPaths = results.GetResult Test
                    let mutable allPassed = auditAction (results.Contains Warn_As_Error) projectPaths = 0
                    for projectPath in projectPaths do
                        AnsiConsole.MarkupLine($"[bold blue]➜ Testing:[/] {projectPath}")
                        let results = 
                            AnsiConsole.Status().Start($"Running doc-tests...", fun ctx ->
                                let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                                DocTestRunner.verifyExamples package projectPath [] |> Async.RunSynchronously
                            )
                        
                        for (name, success, output) in results do
                            if success then AnsiConsole.MarkupLine($"  [green]pass[/] {Markup.Escape(name)}")
                            else 
                                AnsiConsole.MarkupLine($"  [red]fail[/] {Markup.Escape(name)}")
                                AnsiConsole.MarkupLine($"       [grey]{Markup.Escape(output)}[/]")
                                allPassed <- false
                    if allPassed then 
                        AnsiConsole.MarkupLine("\n[bold green]✔ All doc-tests passed successfully![/]")
                        0 
                    else 
                        AnsiConsole.MarkupLine("\n[bold red]✖ Some doc-tests failed.[/]")
                        1

                elif results.Contains Audit then
                    printBanner()
                    auditAction (results.Contains Warn_As_Error) (results.GetResult Audit)

                elif results.Contains Build then
                    printBanner()
                    let projectPaths = results.GetResult Build
                    buildAction (results.Contains Warn_As_Error) projectPaths theme (results.TryGetResult Version)
                    0

                elif results.Contains BuildHistory then
                    printBanner()
                    buildHistoryAction (results.GetResult BuildHistory) theme
                    0

                elif results.Contains Watch then
                    printBanner()
                    let projectPaths = results.GetResult Watch
                    let version = results.TryGetResult Version
                    let host = results.GetResult(Host, defaultValue = "0.0.0.0")
                    let port = results.GetResult(Port, defaultValue = 5000)
                    if String.IsNullOrWhiteSpace host then invalidArg "host" "Preview host must not be empty."
                    if port < 1 || port > 65535 then invalidArg "port" "Preview port must be between 1 and 65535."
                    let previewUrl = $"http://{host}:{port}"
                    buildAction (results.Contains Warn_As_Error) projectPaths theme version
                    
                    try
                        let builder = WebApplication.CreateBuilder()
                        builder.Logging.ClearProviders() |> ignore
                        builder.Logging.AddConsole() |> ignore
                        builder.Logging.SetMinimumLevel(LogLevel.Warning) |> ignore
                        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning) |> ignore

                        let app = builder.Build()
                        
                        app.UseDefaultFiles() |> ignore
                        let outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output")
                        app.UseStaticFiles(StaticFileOptions(
                            FileProvider = new PhysicalFileProvider(outputDir),
                            RequestPath = "",
                            ServeUnknownFileTypes = true,
                            DefaultContentType = "application/octet-stream"
                        )) |> ignore
                        
                        app.Use(fun (context: HttpContext) (next: Func<Threading.Tasks.Task>) ->
                            if context.Request.Path.Value = "/" then
                                context.Response.Redirect("/index.html")
                                Threading.Tasks.Task.CompletedTask
                            else
                                next.Invoke()
                        ) |> ignore

                        AnsiConsole.MarkupLine("[bold blue]🚀 Preview server is live![/]")
                        AnsiConsole.MarkupLine($"   [grey]Listening:[/] {Markup.Escape(previewUrl)}")
                        if host = "0.0.0.0" then
                            AnsiConsole.MarkupLine($"   [grey]Browse locally:[/] http://localhost:{port}")
                        let watchers =
                            PreviewWatcher.start
                                (Directory.GetCurrentDirectory())
                                (PreviewWatcher.parseIgnored (results.GetResults Ignore))
                                (fun () -> buildAction (results.Contains Warn_As_Error) projectPaths theme version)
                        AnsiConsole.MarkupLine("")

                        app.Run(previewUrl)
                        // The watchers stop raising events once they are collected, so they must outlive the server.
                        for watcher in watchers do watcher.Dispose()
                        0
                    with 
                    | :? IOException as e when e.Message.Contains("inotify") ->
                        AnsiConsole.MarkupLine("[red]ERROR: System inotify limit reached.[/]")
                        AnsiConsole.MarkupLine("[yellow]To fix this, increase the limit by running:[/]")
                        AnsiConsole.MarkupLine("[blue]echo fs.inotify.max_user_instances=512 | sudo tee -a /etc/sysctl.conf && sudo sysctl -p[/]")
                        1
                    | e ->
                        AnsiConsole.WriteException(e)
                        1
                
                else 
                    printUsage (Some "No command specified.")
                    0
            with 
            | :? ArguParseException as e ->
                AnsiConsole.WriteLine(e.Message)
                1
            | e ->
                AnsiConsole.WriteException(e)
                1
