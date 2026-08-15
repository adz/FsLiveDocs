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
    /// <summary>Captures a self-contained, renderer-neutral documentation release.</summary>
    | [<CliPrefix(CliPrefix.None)>] Capture of projectPaths:string list
    /// <summary>Verifies and describes a release capsule.</summary>
    | [<CliPrefix(CliPrefix.None)>] Inspect of capsulePath:string
    /// <summary>Adds an immutable capsule reference to a release history index.</summary>
    | [<CliPrefix(CliPrefix.None); AltCommandLine("history-add")>] HistoryAdd of version:string
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
    /// <summary>Sets a local capsule path for history-add.</summary>
    | [<Inherit>] Capsule of string
    /// <summary>Sets an HTTPS capsule URL for history-add.</summary>
    | [<Inherit>] Url of string
    /// <summary>Sets the expected capsule SHA-256 for history-add.</summary>
    | [<Inherit>] Sha256 of string
    /// <summary>Sets the network interface used by the preview server.</summary>
    | [<Inherit>] Host of string
    /// <summary>Sets the TCP port used by the preview server.</summary>
    | [<Inherit>] Port of int
    /// <summary>Adds directory names the preview watcher must not watch or rebuild for.</summary>
    | [<Inherit>] Ignore of string
    /// <summary>Fails the run when the documented API produces quality warnings.</summary>
    | [<Inherit; AltCommandLine("--warnaserror")>] Warn_As_Error
    /// <summary>Sets console verbosity to warnings, info, or debug.</summary>
    | [<Inherit>] Verbosity of string
    /// <summary>Enables or disables interactive terminal rendering.</summary>
    | [<Inherit>] Interactive of bool
    /// <summary>Enables or disables the LiveDocs banner.</summary>
    | [<Inherit>] Banner of bool
    /// <summary>Discovers documentable projects and records them in .livedocs/config.json during init.</summary>
    | [<Inherit>] Discover_Projects
    /// <summary>Validates capture and reports its expected result without publishing the requested output.</summary>
    | [<Inherit>] Dry_Run
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Init -> "Scaffold a new LiveDocs project."
            | CI -> "Generate CI/CD templates (GitHub Actions)."
            | GenerateTests _ -> "Generate a Verify-based snapshot test project for the given projects."
            | Extract _ -> "Extract symbols from one or more projects into a JSON blob."
            | Capture _ -> "Verify and capture a self-contained documentation release capsule."
            | Inspect _ -> "Verify and describe a documentation release capsule."
            | HistoryAdd _ -> "Add an immutable capsule reference to a release history index."
            | Test _ -> "Verify documentation without generating a test project: audits every F# block, then runs each snapshot-selected example."
            | Audit _ -> "Audit coverage, modes, and compilation for every expanded F# documentation block."
            | Build _ -> "Render the final static site for the given projects."
            | BuildHistory _ -> "Render all versions from a verified local history manifest."
            | Watch _ -> "Start a dev server with file watching."
            | Theme _ -> "Set the visual theme (default: light)."
            | Version _ -> "Set the version stored by API model extraction."
            | Output _ -> "Set the API model extraction output path."
            | Capsule _ -> "Set the local capsule path for history-add."
            | Url _ -> "Set the HTTPS capsule URL for history-add."
            | Sha256 _ -> "Set the expected capsule SHA-256 for history-add."
            | Host _ -> "Set the preview bind host (default: 0.0.0.0)."
            | Port _ -> "Set the preview port (default: 5000)."
            | Ignore _ -> "Add a comma-separated list of directory names the watcher ignores. Repeatable."
            | Warn_As_Error -> "Fail the run when the documented API produces quality warnings (default: warn only)."
            | Verbosity _ -> "Set console verbosity: warnings (default), info, or debug."
            | Interactive _ -> "Enable or disable interactive terminal rendering (default: true)."
            | Banner _ -> "Enable or disable the LiveDocs banner (default: true)."
            | Discover_Projects -> "Discover project files and write them to .livedocs/config.json (with init)."
            | Dry_Run -> "Validate capture and report expected output without writing the requested capsule."

type internal VerbosityLevel =
    | Warnings
    | Info
    | Debug

module internal ConsoleOutput =
    let mutable verbosity = Warnings
    let mutable interactive = true
    let mutable banner = true
    let mutable animateBanner = false

    let configure (verbosityValue: string option) (interactiveValue: bool) (bannerValue: bool) =
        verbosity <-
            match verbosityValue |> Option.map _.Trim().ToLowerInvariant() with
            | None | Some "warnings" -> Warnings
            | Some "info" -> Info
            | Some "debug" -> Debug
            | Some value -> invalidArg "verbosity" $"Unsupported verbosity '{value}'. Use warnings, info, or debug."
        interactive <- interactiveValue
        banner <- bannerValue

    let isInfo () = verbosity = Info || verbosity = Debug
    let isDebug () = verbosity = Debug

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
                        if ConsoleOutput.isInfo () then
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
        if ConsoleOutput.isDebug () then
            AnsiConsole.MarkupLine($"   [grey]Watching:[/] {Markup.Escape(watchedNames)}")
            AnsiConsole.MarkupLine($"   [grey]Ignoring:[/] {Markup.Escape(ignoredNames)}")
        watchers

/// <summary>The main entry point module for the CLI application.</summary>
module Program =

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None; FSharpPrelude = None }

    /// <summary>Finds documentable projects when callers omit the project list.</summary>
    let discoverProjects () =
        let root = Directory.GetCurrentDirectory()
        let ignored = set [ ".git"; ".livedocs"; "artifacts"; "bin"; "node_modules"; "obj"; "output"; "packages"; "TestResults"; "tests" ]
        let isIgnored (path: string) =
            Path.GetRelativePath(root, path).Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
            |> Array.exists ignored.Contains
        Directory.GetFiles(root, "*.fsproj", SearchOption.AllDirectories)
        |> Array.filter (isIgnored >> not)
        |> Array.sort
        |> Array.map (fun path -> Path.GetRelativePath(root, path).Replace('\\', '/'))
        |> Array.toList

    let private configuredProjects () =
        let configPath = Path.Combine(".livedocs", "config.json")
        if not (File.Exists configPath) then []
        else
            let config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText configPath)
            match config.GetValue("projects", StringComparison.OrdinalIgnoreCase) with
            | :? Newtonsoft.Json.Linq.JArray as projects ->
                projects.Values<string>()
                |> Seq.filter (String.IsNullOrWhiteSpace >> not)
                |> Seq.toList
            | _ -> []

    let private resolveProjects command projectPaths =
        match projectPaths with
        | _ :: _ -> projectPaths
        | [] ->
            match configuredProjects () with
            | _ :: _ as projects -> projects
            | [] ->
                match discoverProjects () with
                | [] -> invalidOp $"{command} requires at least one project, and no .fsproj files were discovered. Pass project paths explicitly."
                | projects ->
                    AnsiConsole.MarkupLine($"[grey]Discovered {projects.Length} project(s). Pass paths explicitly, or run 'livedocs init --discover-projects' to record the selection.[/]")
                    projects

    let private writeDiscoveredProjects () =
        let projects = discoverProjects ()
        if projects.IsEmpty then invalidOp "No documentable .fsproj files were discovered."
        let configPath = Path.Combine(".livedocs", "config.json")
        let config =
            if File.Exists configPath then Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText configPath)
            else Newtonsoft.Json.Linq.JObject()
        config["projects"] <- Newtonsoft.Json.Linq.JArray(projects |> List.map Newtonsoft.Json.Linq.JValue)
        File.WriteAllText(configPath, config.ToString(Newtonsoft.Json.Formatting.Indented) + Environment.NewLine)
        AnsiConsole.MarkupLine($"[green]✔ Recorded {projects.Length} project(s):[/] {Markup.Escape configPath}")

    let printBanner () =
        if ConsoleOutput.banner && not ConsoleOutput.animateBanner then
            let figlet = FigletText("LiveDocs")
            figlet.Color <- Color.Blue
            AnsiConsole.Write(figlet)
            AnsiConsole.MarkupLine("[grey]Verified Documentation for F#[/]\n")

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

    /// <summary>Names of every XML example some documentation page transcludes.</summary>
    /// <remarks>
    /// A raw scan of the shortcodes, so it needs no package and can run before extraction. An
    /// example a page transcludes is compiled as part of that page and must not be compiled again
    /// on its own.
    /// </remarks>
    let private transcludedExamples () =
        let docsDir = Path.GetFullPath("docs")
        if not (Directory.Exists docsDir) then
            Set.empty
        else
            Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            |> Array.map (File.ReadAllText >> ContentProvider.transcludedExampleNames)
            |> Set.unionMany

    /// <summary>Loads and merges multiple project models into a unified package.</summary>
    let private getUnifiedPackageWithProgress reportProgress (projectPaths: string list) = async {
        let packages = ResizeArray()
        let diagnostics = ResizeArray()
        let covered = transcludedExamples ()
        // The same prelude a page block is compiled with. Without it an example referencing the
        // library by its own namespace fails for want of an open, not for anything wrong with it.
        let prelude = loadSiteConfig().FSharpPrelude |> Option.defaultValue ""
        let builtAssemblies =
            projectPaths
            |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.distinct
        for index, projectPath in projectPaths |> List.indexed do
            reportProgress "Extracting API documentation" (index + 1) projectPaths.Length
            let! package, projectDiagnostics = SymbolLister.extractFromProjectWithDiagnostics projectPath
            packages.Add(package)
            diagnostics.AddRange(projectDiagnostics)
            // Every example not covered elsewhere is compiled against the project that declares it,
            // so "the documented code compiles" holds for XML examples as it does for fences.
            let! exampleDiagnostics = GeneratedVerification.compileUncoveredExamples projectPath prelude builtAssemblies covered package
            diagnostics.AddRange(exampleDiagnostics)
        return SymbolLister.merge (Seq.toList packages), List.ofSeq diagnostics
    }

    let getUnifiedPackage projectPaths =
        getUnifiedPackageWithProgress (fun _ _ _ -> ()) projectPaths

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

    /// <summary>One documentation page, resolved against the projects passed to livedocs.</summary>
    type private DocumentationPage = {
        /// <summary>Path of the page relative to the documentation directory.</summary>
        Relative: string
        /// <summary>The project this page's F# blocks are compiled against.</summary>
        SelectedProject: string
        /// <summary>Page body after snippet and example transclusion.</summary>
        Expanded: string
        /// <summary>F# blocks discovered in the expanded body.</summary>
        Blocks: DocumentationBlock list
        /// <summary>Renderer-neutral page metadata retained in release content.</summary>
        Metadata: ContentMetadata
        /// <summary>Target framework this page pins, if any.</summary>
        TargetFramework: string option
    }

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

    let private getUnifiedPackageCachedWithProgress reportProgress (projectPaths: string list) =
        let inputHash = projectInputFingerprint projectPaths
        let cacheDirectory = Path.GetFullPath(Path.Combine(".livedocs", "cache"))
        // The key covers every assembly whose code shapes what is cached. Keying on Core alone
        // silently replayed stale diagnostics whenever the Runner or the CLI changed, which is
        // indistinguishable from a fix having no effect.
        let extractorVersions =
            [ typeof<PackageModel>.Assembly
              typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly
              Reflection.Assembly.GetExecutingAssembly() ]
            |> List.map (fun assembly -> string assembly.ManifestModule.ModuleVersionId)
            |> String.concat ","
        let cacheKey = sha256Text $"api-schema:{History.ApiModelSchemaVersion}|extractor:{extractorVersions}|{inputHash}"
        let cachePath = Path.Combine(cacheDirectory, cacheKey + ".package.json")
        // Diagnostics describe the run, not the snapshot, so they live beside the cached package
        // rather than inside it — otherwise a warning would be reported once and never again.
        let diagnosticsPath = Path.Combine(cacheDirectory, cacheKey + ".diagnostics.json")
        if File.Exists cachePath then
            reportProgress "Extracting API documentation" projectPaths.Length projectPaths.Length
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
            let package, diagnostics = getUnifiedPackageWithProgress reportProgress projectPaths |> Async.RunSynchronously
            writeCurrentCache cachePath "*.package.json" (Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
            writeCurrentCache diagnosticsPath "*.diagnostics.json" (Newtonsoft.Json.JsonConvert.SerializeObject(diagnostics, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
            package, diagnostics, inputHash

    let private getUnifiedPackageCached projectPaths =
        getUnifiedPackageCachedWithProgress (fun _ _ _ -> ()) projectPaths

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
            let repoUrl = loadSiteConfig().RepoUrl |> Option.map (fun value -> value.TrimEnd('/'))
            let files = diagnostics |> List.groupBy (fun d -> relative d.Location.File)
            for file, items in files do
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape file}[/]")
                if ConsoleOutput.isDebug () then
                    for item in items |> List.sortBy (fun d -> d.Location.Line) do
                        let symbol = Markup.Escape item.Symbol
                        AnsiConsole.MarkupLine($"  {label} [grey]{item.Location.Line}[/] {symbol} [grey]({Markup.Escape item.Code})[/]")
                        AnsiConsole.MarkupLine($"        {Markup.Escape item.Message}")
                        AnsiConsole.MarkupLine($"        [grey]{Markup.Escape item.Remedy}[/]")
                else
                    for code, matching in items |> List.groupBy _.Code |> List.sortBy fst do
                        let lines = matching |> List.sortBy _.Location.Line |> List.map (fun item -> string item.Location.Line) |> String.concat ", "
                        let issue =
                            match code with
                            | "example-does-not-compile" -> "examples do not compile"
                            | _ -> code.Replace('-', ' ')
                        AnsiConsole.MarkupLine($"  {label} {matching.Length} {Markup.Escape(issue)} [grey](lines {Markup.Escape(lines)})[/]")
                match repoUrl with
                | Some root when file <> "(unknown source)" ->
                    let url = $"{root}/blob/HEAD/{file}"
                    AnsiConsole.MarkupLine($"  [link={Markup.Escape url}]View source on GitHub[/]")
                | _ -> ()

            let count = diagnostics.Length
            let noun = if count = 1 then "warning" else "warnings"
            if warnAsError then
                let verb = if count = 1 then "treated as an error" else "treated as errors"
                AnsiConsole.MarkupLine($"\n[red]✖ {count} API documentation {noun} across {files.Length} file(s), {verb} (--warn-as-error).[/]")
                count
            else
                AnsiConsole.MarkupLine($"\n[yellow]⚠ {count} API documentation {noun} across {files.Length} file(s).[/] [grey]Use --verbosity debug for details or --warn-as-error to fail.[/]")
                0

    /// <summary>
    /// Walks the documentation directory once, resolving every page against the projects passed to
    /// livedocs.
    /// </summary>
    /// <remarks>
    /// Audit, build and generated tests all need the same page set resolved the same way. When
    /// they each walked the directory themselves the copies drifted, and the copy behind
    /// generated tests silently omitted the check that a selected project was actually passed.
    /// </remarks>
    let private documentationPages (projectPaths: string list) (package: PackageModel) =
        if List.isEmpty projectPaths then invalidOp "Documentation analysis requires at least one project path."
        let docsDir = Path.GetFullPath("docs")
        if not (Directory.Exists docsDir) then invalidOp $"Documentation directory is missing: {docsDir}"
        let sourceDir = Directory.GetCurrentDirectory()
        let resolvedProjects = projectPaths |> List.map Path.GetFullPath
        let defaultProject = List.head resolvedProjects
        [ for path in Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories) |> Array.sort do
                let relative = Path.GetRelativePath(docsDir, path).Replace('\\', '/')
                let raw = File.ReadAllText(path)
                let frontMatter = ContentProvider.parseFrontMatter raw
                let body = frontMatter |> Option.map snd |> Option.defaultValue raw
                let metadata =
                    frontMatter
                    |> Option.map fst
                    |> Option.defaultValue {
                        Title = ContentProvider.defaultTitle path
                        Type = None
                        Project = None
                        TargetFramework = None
                        Platform = None
                    }
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
                let expanded = ContentProvider.expandTransclusions body sourceDir package
                let blocks = DocumentationDiscovery.discoverMarkdown relative (Some selectedProject) expanded
                DocumentationDiscovery.validateCoverage blocks
                let platform = frontMatter |> Option.bind (fun (metadata, _) -> metadata.Platform) |> Option.map _.ToLowerInvariant()
                match platform with
                | Some "fable" when blocks |> List.exists (fun block -> match block.Mode with NoCheck _ | Transcript -> false | _ -> true) ->
                    invalidOp $"Documentation page {relative} declares platform: fable, but FsLiveDocs cannot yet invoke the Fable compiler. Mark each F# block no-check with a reason or transclude code covered by a Fable build gate."
                | Some value when value <> "dotnet" && value <> "fable" -> invalidOp $"Documentation page {relative} declares unsupported platform '{value}'."
                | _ -> ()
                let targetFramework = frontMatter |> Option.bind (fun (metadata, _) -> metadata.TargetFramework)
                yield {
                    Relative = relative
                    SelectedProject = selectedProject
                    Expanded = expanded
                    Blocks = blocks
                    Metadata = metadata
                    TargetFramework = targetFramework
                } ]

    let private analyzeDocumentationWithProgress reportProgress (projectPaths: string list) (projectFingerprint: string) (package: PackageModel) =
        let prelude = loadSiteConfig().FSharpPrelude |> Option.defaultValue ""
        let builtAssemblies =
            projectPaths
            |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.distinct
        let pages = documentationPages projectPaths package
        let resolvedProjects = projectPaths |> List.map Path.GetFullPath
        let blocks = pages |> List.collect _.Blocks
        let packageFingerprint = Newtonsoft.Json.JsonConvert.SerializeObject(package, FsLiveDocs.Core.Serialization.jsonSettings)
        let contextFingerprint =
            [ yield $"semantic-schema:{History.SemanticSchemaVersion}"
              yield $"compiler-mvid:{typeof<EvaluatedProject>.Assembly.ManifestModule.ModuleVersionId}"
              yield $"project-inputs:{projectFingerprint}"
              yield $"prelude:{prelude}"
              yield packageFingerprint
              for page in pages do
                  let framework = page.TargetFramework |> Option.defaultValue "<default>"
                  yield $"project:{page.SelectedProject}|framework:{framework}"
                  for block in page.Blocks do yield $"block:{block.Id}|{block.SourceHash}" ]
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
            | Some _ ->
                reportProgress "Checking documentation pages" pages.Length pages.Length
                []
            | None ->
                // Only page-selected projects need compiler evaluation. Evaluating every project
                // leaks solution composition into documentation checking and can make an unrelated
                // project-reference graph fail capture. Other documented projects contribute their
                // already-built assemblies to the aggregate reference context.
                let selectedProjects = pages |> List.map _.SelectedProject |> List.distinct
                let evaluated = selectedProjects |> List.map (fun path -> path, DocumentationCompiler.evaluateProject path)
                let builtAssemblies =
                    resolvedProjects
                    |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                    |> List.filter (String.IsNullOrWhiteSpace >> not)
                let aggregateReferences =
                    (evaluated |> List.collect (snd >> _.References)) @ builtAssemblies
                    |> List.distinct
                let evaluatedProjects = evaluated |> List.map (fun (path, project) -> path, { project with References = aggregateReferences }) |> Map.ofList
                let completed = ref 0
                pages
                |> List.map (fun page -> async {
                    let selectedEvaluation =
                        match page.TargetFramework with
                        | None -> evaluatedProjects.[page.SelectedProject]
                        | Some _ ->
                            let selected = DocumentationCompiler.evaluateProjectFor page.TargetFramework page.SelectedProject
                            let references = selected.References @ aggregateReferences |> List.distinctBy (Path.GetFileName >> _.ToUpperInvariant())
                            { selected with References = references }
                    let! checkedUnits = DocumentationCompiler.checkBlocksWithProject selectedEvaluation prelude page.Blocks
                    let current = Threading.Interlocked.Increment(completed)
                    reportProgress "Checking documentation pages" current pages.Length
                    return checkedUnits })
                |> fun checks -> Async.Parallel(checks, maxDegreeOfParallelism = max 1 Environment.ProcessorCount)
                |> Async.RunSynchronously
                |> Array.toList
                |> List.collect id
        DocumentationDiscovery.validateCoverage blocks
        {
            Blocks = blocks
            Results = results
            Prelude = prelude
            CachedArtifact = cachedArtifact
            CachePath = cachePath
        }

    let private analyzeDocumentation projectPaths projectFingerprint package =
        analyzeDocumentationWithProgress (fun _ _ _ -> ()) projectPaths projectFingerprint package

    let private printAudit showSuccess (analysis: DocumentationAnalysis) =
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
            if status = "FAIL" || ConsoleOutput.isDebug () then
                let color = if status = "PASS" then "green" elif status = "FAIL" then "red" else "yellow"
                AnsiConsole.MarkupLine($"[{color}]{status,-8}[/] {Markup.Escape(block.Id)} ({Markup.Escape(detail)})")
        if failures = 0 then
            let excluded = analysis.Blocks |> List.filter (fun block -> match block.Mode with NoCheck _ -> true | _ -> false) |> List.length
            let verified = analysis.Blocks.Length - excluded
            if showSuccess then
                AnsiConsole.MarkupLine($"[green]✔ Audit complete:[/] {analysis.Blocks.Length} blocks — {verified} verified, {excluded} excluded, 0 failed.")
        else
            AnsiConsole.MarkupLine($"\n[red]✖ Audit failed:[/] {failures} of {analysis.Blocks.Length} expanded F# block(s) contain compiler errors.")
        failures

    let auditAction (warnAsError: bool) (projectPaths: string list) =
        if List.isEmpty projectPaths then invalidOp "Audit requires at least one project path."
        let package, diagnostics, projectFingerprint = getUnifiedPackageCached projectPaths
        let analysis = analyzeDocumentation projectPaths projectFingerprint package
        let blockFailures = printAudit true analysis
        let apiFailures = printApiDiagnostics warnAsError diagnostics
        if blockFailures = 0 && apiFailures = 0 then 0 else 1

    let private createSemanticArtifact (projectPaths: string list) (package: PackageModel) =
        let analysis = analyzeDocumentation projectPaths (projectInputFingerprint projectPaths) package
        let artifact = analysis.CachedArtifact |> Option.defaultWith (fun () -> SemanticExtractor.artifact analysis.Results)
        if analysis.CachedArtifact.IsNone then
            writeCurrentCache analysis.CachePath "*.semantic.json" (Newtonsoft.Json.JsonConvert.SerializeObject(artifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
        artifact, analysis.Prelude

    let private currentRevision () =
        let startInfo = Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        use gitProcess = Diagnostics.Process.Start(startInfo)
        let revision = gitProcess.StandardOutput.ReadToEnd().Trim()
        gitProcess.WaitForExit()
        if gitProcess.ExitCode <> 0 || String.IsNullOrWhiteSpace revision then
            invalidOp "Release capture requires a Git commit so the capsule can record source provenance."
        revision

    let private captureAssets docsDir =
        Directory.GetFiles(docsDir, "*", SearchOption.AllDirectories)
        |> Array.filter (fun path -> not (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        |> Array.map (fun path -> Path.GetRelativePath(docsDir, path).Replace('\\', '/'), File.ReadAllBytes path)
        |> Array.toList

    let private verifyExplicitReleaseCases projectPaths package (pages: DocumentationPage list) references =
        for projectPath in projectPaths do
            let projectPackage = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            for name in DocTestRunner.snapshotExampleNames projectPackage do
                let snapshot = DocTestRunner.collectSnapshotByName projectPackage projectPath references name |> Async.RunSynchronously
                match snapshot.Status with
                | ExampleStatus.Verified | ExampleStatus.FirstCut -> ()
                | ExampleStatus.Mismatch ->
                    invalidOp $"XML example {name} output did not match its expected release output."
                | ExampleStatus.Error ->
                    invalidOp $"XML example {name} failed during release capture: {snapshot.ActualOutput}"

        for page in pages do
            let externallyExecuted =
                page.Blocks
                |> List.choose (fun block ->
                    match block.Mode, block.Origin with
                    | (Run | Transcript), XmlExample -> Some block.Id
                    | _ -> None)
                |> Set.ofList
            for case in DocumentationDiscovery.generatedCases page.SelectedProject "" page.Relative page.Expanded externallyExecuted do
                match case.Action with
                | ExecuteBlock _ | ExecuteTranscriptBlock _ ->
                    GeneratedVerification.runCase references case |> Async.RunSynchronously
                | CompileUnit _ -> ()

    let captureAction warnAsError dryRun projectPaths version output =
        let extracted, apiDiagnostics, projectFingerprint = getUnifiedPackageCached projectPaths
        let package = { extracted with Version = version |> Option.defaultValue extracted.Version }
        let analysis = analyzeDocumentation projectPaths projectFingerprint package
        if printAudit true analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before capture."
        if printApiDiagnostics warnAsError apiDiagnostics <> 0 then
            invalidOp "API documentation warnings were treated as errors because --warn-as-error was passed."

        let pages = documentationPages projectPaths package
        let references =
            projectPaths
            |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.distinct
        verifyExplicitReleaseCases projectPaths package pages references

        let semantic = analysis.CachedArtifact |> Option.defaultWith (fun () -> SemanticExtractor.artifact analysis.Results)
        let api : ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
        let contentPages =
            pages
            |> List.map (fun page -> { SourcePath = page.Relative; Metadata = page.Metadata; Markdown = page.Expanded })
        let outputPath = output |> Option.defaultValue $".livedocs/releases/{package.Version}.livedocs.zip"
        let toolVersion = Reflection.Assembly.GetExecutingAssembly().GetName().Version |> string
        let actualOutputPath =
            if dryRun then Path.Combine(Path.GetTempPath(), "fslivedocs-dry-run-" + Guid.NewGuid().ToString("N") + ".zip")
            else outputPath
        let created =
            ReleaseCapsule.create
                actualOutputPath
                (currentRevision ())
                toolVersion
                api
                semantic
                (loadSiteConfig())
                contentPages
                (captureAssets "docs")
        let report = ReleaseCapsule.inspect actualOutputPath
        if report.Sha256 <> created.Sha256 then invalidOp "Release capsule checksum changed during post-write verification."
        let publicReport = { report with Path = Path.GetFullPath outputPath }
        if dryRun then
            File.Delete actualOutputPath
            AnsiConsole.MarkupLine("[green]✔ Release capture dry run complete.[/]")
            AnsiConsole.MarkupLine($"  Planned output: {Markup.Escape publicReport.Path}")
        else
            let reportPath = outputPath + ".report.json"
            File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(publicReport, Newtonsoft.Json.Formatting.Indented, Serialization.jsonSettings))
            AnsiConsole.MarkupLine($"[green]✔ Release capsule:[/] {Markup.Escape publicReport.Path}")
            AnsiConsole.MarkupLine($"  Report: {Markup.Escape(Path.GetFullPath reportPath)}")
        AnsiConsole.MarkupLine($"  Version: [blue]{Markup.Escape publicReport.Manifest.ProductVersion}[/]")
        AnsiConsole.MarkupLine($"  API: {publicReport.Manifest.Api.Size:N0} bytes")
        AnsiConsole.MarkupLine($"  Semantic: {publicReport.Manifest.Semantic.Size:N0} bytes")
        AnsiConsole.MarkupLine($"  Content: {publicReport.Manifest.Content.Size:N0} bytes")
        AnsiConsole.MarkupLine($"  Inventory: {publicReport.Counts.Entities:N0} entities, {publicReport.Counts.Members:N0} members, {publicReport.Counts.DocumentationNodes:N0} documentation nodes, {publicReport.Counts.Examples:N0} examples")
        AnsiConsole.MarkupLine($"  Content: {publicReport.Counts.Pages:N0} pages, {publicReport.Counts.CodeBlocks:N0} code blocks, {publicReport.Counts.Tooltips:N0} tooltips, {publicReport.Counts.Diagnostics:N0} diagnostics, {publicReport.Counts.Assets:N0} assets")
        AnsiConsole.MarkupLine($"  Compressed: {publicReport.CompressedSize:N0} bytes")
        AnsiConsole.MarkupLine($"  Uncompressed: {publicReport.UncompressedSize:N0} bytes")
        AnsiConsole.MarkupLine($"  SHA-256: {publicReport.Sha256}")
        0

    let historyAddAction indexPath version capsulePath capsuleUrl checksum =
        let index =
            if File.Exists indexPath then ReleaseCapsule.loadHistoryIndex indexPath
            else { SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion; CurrentVersion = version; Entries = [] }
        if index.Entries |> List.exists (fun entry -> entry.Version = version) then
            invalidOp $"Release history already contains version {version}. Published entries are immutable."
        let path, url, sha256 =
            match capsulePath, capsuleUrl with
            | Some path, None ->
                let fullPath = Path.GetFullPath path
                if not (File.Exists fullPath) then invalidOp $"Release capsule is missing: {fullPath}"
                Some path, None, checksum |> Option.defaultValue (History.sha256 fullPath)
            | None, Some url ->
                let expected = checksum |> Option.defaultWith (fun () -> invalidOp "A remote capsule requires --sha256.")
                None, Some url, expected
            | _ -> invalidOp "Specify exactly one of --capsule or --url."
        let updated =
            {
                index with
                    CurrentVersion = version
                    Entries =
                        { Version = version; CapsulePath = path; CapsuleUrl = url; CapsuleSha256 = sha256 }
                        :: index.Entries
                        |> List.sortByDescending _.Version
            }
        let directory = Path.GetDirectoryName(Path.GetFullPath indexPath)
        Directory.CreateDirectory directory |> ignore
        File.WriteAllText(indexPath, Newtonsoft.Json.JsonConvert.SerializeObject(updated, Newtonsoft.Json.Formatting.Indented, Serialization.jsonSettings))
        // Load what was written so malformed checksums and source combinations cannot be persisted silently.
        ReleaseCapsule.loadHistoryIndex indexPath |> ignore
        AnsiConsole.MarkupLine($"[green]✔ History index updated:[/] {Markup.Escape(Path.GetFullPath indexPath)}")
        0

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
            let documentationCases =
                [ for page in documentationPages resolvedProjects package do
                    let externallyExecuted =
                        page.Blocks
                        |> List.choose (fun block ->
                            match block.Mode, block.Origin with
                            | (Run | Transcript), XmlExample -> Some block.Id
                            | _ -> None)
                        |> Set.ofList
                    yield!
                        DocumentationDiscovery.generatedCases
                            page.SelectedProject
                            ""
                            page.Relative
                            page.Expanded
                            externallyExecuted ]

            let documentationTestBodies =
                documentationCases
                |> List.map (fun case ->
                    let escapedProject = Path.GetFullPath(case.ProjectPath).Replace("\"", "\"\"")
                    let escapedSource = case.SourcePath.Replace("\"", "\"\"")
                    let escapedId = case.Id.Replace("`", "'").Replace("\"", "\"\"")
                    let escapedCaseId = case.Id.Replace("\"", "\"\"")
                    let encoded = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes case.ExpandedMarkdown)
                    let actionExpression =
                        match case.Action with
                        | CompileUnit id ->
                            let escapedActionId = id.Replace("\"", "\"\"")
                            $"CompileUnit @\"{escapedActionId}\""
                        | ExecuteBlock id ->
                            let escapedActionId = id.Replace("\"", "\"\"")
                            $"ExecuteBlock @\"{escapedActionId}\""
                        | ExecuteTranscriptBlock id ->
                            let escapedActionId = id.Replace("\"", "\"\"")
                            $"ExecuteTranscriptBlock @\"{escapedActionId}\""
                    [ ""
                      "    [<Fact>]"
                      $"    let ``documentation {escapedId}`` () ="
                      $"        let projectPath = @\"{escapedProject}\""
                      $"        let references = [ {assemblyReferenceLiteral} ]"
                      $"        let markdown = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(\"{encoded}\"))"
                      $"        let case = {{ Id = @\"{escapedCaseId}\"; ProjectPath = projectPath; SourcePath = @\"{escapedSource}\"; ExpandedMarkdown = markdown; Action = {actionExpression} }}"
                      "        GeneratedVerification.runCase references case |> Async.RunSynchronously" ]
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

    let private prepareBuildDocumentation reportProgress reportNote projectPaths projectFingerprint package =
        let analysis = analyzeDocumentationWithProgress reportProgress projectPaths projectFingerprint package
        if printAudit false analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before building."
        let excluded = analysis.Blocks |> List.filter (fun block -> match block.Mode with NoCheck _ -> true | _ -> false) |> List.length
        let verified = analysis.Blocks.Length - excluded
        reportNote $"Audit complete: {analysis.Blocks.Length} blocks — {verified} verified, {excluded} excluded, 0 failed."
        let semanticArtifact = analysis.CachedArtifact |> Option.defaultWith (fun () -> SemanticExtractor.artifact analysis.Results)
        if analysis.CachedArtifact.IsNone then
            writeCurrentCache analysis.CachePath "*.semantic.json" (Newtonsoft.Json.JsonConvert.SerializeObject(semanticArtifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
        semanticArtifact, analysis.Prelude

    /// <summary>Orchestrates the build process for one or more projects.</summary>
    let buildAction (warnAsError: bool) (projectPaths: string list) (theme: string) (version: string option) =
        let mutable deferredApiDiagnostics: ApiDiagnostic list = []
        let pipeline reportStage reportProgress reportNote =
            reportStage "Extracting API documentation"
            let extracted, apiDiagnostics, projectFingerprint = getUnifiedPackageCachedWithProgress reportProgress projectPaths
            let packageRaw = { extracted with Version = version |> Option.defaultValue extracted.Version }
            reportStage "Checking documentation examples"
            let semanticArtifact, prelude =
                prepareBuildDocumentation reportProgress reportNote projectPaths projectFingerprint packageRaw
            if warnAsError then
                if printApiDiagnostics true apiDiagnostics <> 0 then
                    invalidOp "API documentation warnings were treated as errors because --warn-as-error was passed."
            else
                deferredApiDiagnostics <- apiDiagnostics
            reportStage "Rendering documentation site"
            let sourceDir = Directory.GetCurrentDirectory()
            let semanticCode =
                {
                    SemanticCode.defaults with
                        Artifact = Some semanticArtifact
                        Prelude = prelude
                }
            let package = ContentProvider.applyApiDocsWithOptions "docs" sourceDir packageRaw semanticCode
            let pages = ContentProvider.scanDocsWithOptions "docs" sourceDir package "" semanticCode
            let config = loadSiteConfig()

            let historyDir = ".livedocs/history"
            if not (Directory.Exists(historyDir)) then Directory.CreateDirectory(historyDir) |> ignore

            SiteBuilder.buildAll historyDir package pages config theme "output"
            ContentProvider.copyStaticFiles "docs" "output"

            reportStage "Building search index"
            let psi = System.Diagnostics.ProcessStartInfo("npx", "-y pagefind --site output")
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            let proc = System.Diagnostics.Process.Start(psi)
            proc.WaitForExit()
        if ConsoleOutput.interactive then
            // Wrapper scripts commonly run the preview server as a background job so they can
            // monitor a stop file. Spectre classifies that process as non-interactive even though
            // stdout is still a terminal. An explicit --interactive true must override detection.
            let settings = AnsiConsoleSettings()
            settings.Interactive <- InteractionSupport.Yes
            let interactiveConsole = AnsiConsole.Create(settings)
            if ConsoleOutput.banner then
                let started = Diagnostics.Stopwatch.StartNew()
                let syncRoot = obj ()
                let completed = ResizeArray<string * float * bool * string list>()
                let mutable currentName: string option = None
                let mutable currentText = "Starting documentation build"
                let mutable currentStarted = 0.0
                let currentNotes = ResizeArray<string>()
                let finishCurrent succeeded =
                    match currentName with
                    | Some name ->
                        completed.Add(name, started.Elapsed.TotalSeconds - currentStarted, succeeded, List.ofSeq currentNotes)
                        currentNotes.Clear()
                        currentName <- None
                    | None -> ()
                let startStage name =
                    lock syncRoot (fun () ->
                        if currentName <> Some name then
                            finishCurrent true
                            currentName <- Some name
                            currentText <- name
                            currentStarted <- started.Elapsed.TotalSeconds)
                let reportProgress name current total =
                    lock syncRoot (fun () ->
                        if currentName <> Some name then
                            finishCurrent true
                            currentName <- Some name
                            currentStarted <- started.Elapsed.TotalSeconds
                        currentText <- $"{name} ({current}/{total})")
                let reportNote note = lock syncRoot (fun () -> currentNotes.Add note)
                let render () =
                    let elapsed = started.Elapsed.TotalMilliseconds
                    let activityFrames = Spinner.Known.DotsCircle.Frames
                    let activity = activityFrames.[int (elapsed / 80.0) % activityFrames.Count]
                    let status =
                        lock syncRoot (fun () ->
                            [ for name, duration, succeeded, notes in completed do
                                  let mark = if succeeded then "[green]✓[/]" else "[red]✗[/]"
                                  let formattedDuration = duration.ToString("0.0")
                                  yield $"{mark} {Markup.Escape(name)} [grey]({formattedDuration}s)[/]"
                                  for note in notes do yield $"  [grey]{Markup.Escape(note)}[/]"
                              match currentName with
                              | Some _ ->
                                  let duration = started.Elapsed.TotalSeconds - currentStarted
                                  let formattedDuration = duration.ToString("0.0")
                                  yield $"[bold blue]{Markup.Escape(activity)} {Markup.Escape(currentText)}[/] [grey]({formattedDuration}s)[/]"
                                  for note in currentNotes do yield $"  [grey]{Markup.Escape(note)}[/]"
                              | None -> () ]
                            |> String.concat "\n")
                    LiveDocsBanner.render elapsed status
                interactiveConsole.Live(render ())
                    .AutoClear(false)
                    .Start(fun context ->
                        use stopAnimation = new Threading.CancellationTokenSource()
                        let animation =
                            Threading.Tasks.Task.Run(fun () ->
                                while not stopAnimation.IsCancellationRequested do
                                    context.UpdateTarget(render ())
                                    Threading.Thread.Sleep(80))
                        try
                            try
                                pipeline startStage reportProgress reportNote
                                lock syncRoot (fun () -> finishCurrent true)
                            with error ->
                                lock syncRoot (fun () -> finishCurrent false)
                                reraise ()
                        finally
                            stopAnimation.Cancel()
                            animation.Wait())
            else
                interactiveConsole.Status()
                    .Spinner(Spinner.Known.DotsCircle)
                    .SpinnerStyle(Style.Parse("bold blue"))
                    .Start("[bold blue]Starting documentation build[/]", fun context ->
                        let update text = context.Status($"[bold blue]{Markup.Escape(text)}[/]") |> ignore
                        pipeline update (fun name current total -> update $"{name} ({current}/{total})") (fun note -> AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(note)}[/]")))
        else
            let reportStage stage = if ConsoleOutput.isInfo () then AnsiConsole.MarkupLine($"{Markup.Escape(stage)}...")
            let reportProgress stage current total =
                if ConsoleOutput.isInfo () then AnsiConsole.MarkupLine($"{Markup.Escape(stage)} ({current}/{total})")
            let reportNote note = AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(note)}[/]")
            pipeline reportStage reportProgress reportNote
        printApiDiagnostics false deferredApiDiagnostics |> ignore
        AnsiConsole.MarkupLine("[green]✔ Build complete:[/] output/")

    let buildHistoryAction manifestPath theme =
        let raw = File.ReadAllText manifestPath
        let isCapsuleIndex =
            raw.Contains("\"CapsulePath\"", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("\"CapsuleUrl\"", StringComparison.OrdinalIgnoreCase)
        if isCapsuleIndex then
            let index = ReleaseCapsule.loadHistoryIndex manifestPath
            let indexRoot = Path.GetDirectoryName(Path.GetFullPath manifestPath)
            let temporaryRoot = Path.Combine(Path.GetTempPath(), "fslivedocs-history-" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory temporaryRoot |> ignore
            try
                let loaded =
                    index.Entries
                    |> List.map (fun entry ->
                        let capsulePath = ReleaseCapsule.acquire indexRoot (Path.GetFullPath(".livedocs/releases")) entry
                        let docsDir = Path.Combine(temporaryRoot, entry.Version, "docs")
                        let packageRaw, semanticArtifact, site = ReleaseCapsule.materializeContent capsulePath docsDir
                        if packageRaw.Version <> entry.Version then
                            invalidOp $"Release capsule version mismatch: expected {entry.Version}, got {packageRaw.Version}."
                        let semanticCode = { SemanticCode.defaults with Artifact = Some semanticArtifact; Prelude = semanticArtifact.Prelude }
                        let package = ContentProvider.applyApiDocsWithOptions docsDir docsDir packageRaw semanticCode
                        let rootPath = if entry.Version = index.CurrentVersion then "" else "../../"
                        let pages = ContentProvider.scanDocsWithOptions docsDir docsDir package rootPath semanticCode
                        entry.Version, package, pages, docsDir, site)
                let config =
                    loaded
                    |> List.find (fun (version, _, _, _, _) -> version = index.CurrentVersion)
                    |> fun (_, _, _, _, site) -> site
                let sites = loaded |> List.map (fun (version, package, pages, docsDir, _) -> version, package, pages, docsDir)
                SiteBuilder.buildHistory index.CurrentVersion sites config theme "output"
            finally
                if Directory.Exists temporaryRoot then Directory.Delete(temporaryRoot, true)
        else
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
                ConsoleOutput.configure
                    (results.TryGetResult Verbosity)
                    (results.GetResult(Interactive, defaultValue = true))
                    (results.GetResult(Banner, defaultValue = true))
                ConsoleOutput.animateBanner <-
                    ConsoleOutput.interactive && ConsoleOutput.banner && (results.Contains Build || results.Contains Watch)
                let theme = results.GetResult(Theme, defaultValue = "light")
                
                if results.Contains Init then
                    printBanner()
                    AnsiConsole.MarkupLine("[blue]Scaffolding new project...[/]")
                    if not (Directory.Exists(".livedocs")) then Directory.CreateDirectory(".livedocs") |> ignore
                    if not (File.Exists(".livedocs/config.json")) then File.WriteAllText(".livedocs/config.json", "{}")
                    if results.Contains Discover_Projects then writeDiscoveredProjects ()
                    if not (File.Exists(".livedocs/history.json")) then
                        let historyStarter = """{
  "SchemaVersion": 1,
  "CurrentVersion": "0.0.0",
  "Entries": []
}
"""
                        File.WriteAllText(".livedocs/history.json", historyStarter)
                    let ignorePath = ".gitignore"
                    let ignored =
                        if File.Exists ignorePath then File.ReadAllText(ignorePath).Replace("\r\n", "\n")
                        else ""
                    let requiredIgnores = [ ".livedocs/cache/"; ".livedocs/releases/" ]
                    let missingIgnores = requiredIgnores |> List.filter (fun item -> ignored.Split('\n') |> Array.contains item |> not)
                    if not missingIgnores.IsEmpty then
                        let prefix = if String.IsNullOrEmpty ignored || ignored.EndsWith("\n") then ignored else ignored + "\n"
                        File.WriteAllText(ignorePath, prefix + String.concat "\n" missingIgnores + "\n")
                    if not (Directory.Exists("docs")) then Directory.CreateDirectory("docs") |> ignore
                    if not (File.Exists("docs/index.md")) then
                        let starter = """---
title: Home
weight: 1
---

# Document your F# library

FsLiveDocs generates API reference pages and verifies F# examples with your project's compiler settings.

## Build the documentation

Replace the project path below, then run from your repository root:

```bash
dotnet build
livedocs audit
livedocs build
livedocs watch --host 127.0.0.1 --port 5000
```

Add an ordinary `fsharp` fence to a guide for compile-only verification. Use `run` only for intentional execution,
`transcript` for FSI input/output, `isolated` for standalone code, `prepare` for hidden setup, or
`no-check reason="..."` for deliberate pseudocode.

To capture a release after verification succeeds, run:

```bash
livedocs capture --version 1.0.0
```
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
    tags: [ 'v*' ]
permissions:
  contents: write
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
          dotnet build --nologo
          dotnet livedocs test
          dotnet livedocs build
      - name: Capture release documentation
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          version="${GITHUB_REF_NAME#v}"
          dotnet livedocs capture --version "$version" --output "artifacts/livedocs-$version.zip"
      - name: Publish immutable release capsule
        if: startsWith(github.ref, 'refs/tags/v')
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          version="${GITHUB_REF_NAME#v}"
          gh release create "$GITHUB_REF_NAME" \
            "artifacts/livedocs-$version.zip" \
            "artifacts/livedocs-$version.zip.report.json" \
            --verify-tag --generate-notes
      - uses: actions/upload-pages-artifact@v3
        with:
          path: output
"""
                    File.WriteAllText(".github/workflows/livedocs.yml", workflow)
                    AnsiConsole.MarkupLine("[green]✔ Done:[/] .github/workflows/livedocs.yml")
                    0

                elif results.Contains GenerateTests then
                    let projectPaths = results.GetResult GenerateTests |> resolveProjects "generate-tests"
                    generateSnapshotTests projectPaths

                elif results.Contains Capture then
                    printBanner()
                    let projectPaths = results.GetResult Capture |> resolveProjects "capture"
                    let version = results.TryGetResult Version
                    let output = results.TryGetResult Output
                    captureAction (results.Contains Warn_As_Error) (results.Contains Dry_Run) projectPaths version output

                elif results.Contains Inspect then
                    let path = results.GetResult Inspect
                    let report = ReleaseCapsule.inspect path
                    AnsiConsole.MarkupLine($"[green]✔ Valid release capsule:[/] {Markup.Escape report.Path}")
                    AnsiConsole.MarkupLine($"  Version: [blue]{Markup.Escape report.Manifest.ProductVersion}[/]")
                    AnsiConsole.MarkupLine($"  Revision: {Markup.Escape report.Manifest.SourceRevision}")
                    AnsiConsole.MarkupLine($"  API schema: {report.Manifest.Api.SchemaVersion} ({report.Manifest.Api.Size:N0} bytes)")
                    AnsiConsole.MarkupLine($"  Semantic schema: {report.Manifest.Semantic.SchemaVersion} ({report.Manifest.Semantic.Size:N0} bytes)")
                    AnsiConsole.MarkupLine($"  Content schema: {report.Manifest.Content.SchemaVersion} ({report.Manifest.Content.Size:N0} bytes)")
                    AnsiConsole.MarkupLine($"  Inventory: {report.Counts.Entities:N0} entities, {report.Counts.Members:N0} members, {report.Counts.DocumentationNodes:N0} documentation nodes, {report.Counts.Examples:N0} examples")
                    AnsiConsole.MarkupLine($"  Content: {report.Counts.Pages:N0} pages, {report.Counts.CodeBlocks:N0} code blocks, {report.Counts.Tooltips:N0} tooltips, {report.Counts.Diagnostics:N0} diagnostics, {report.Counts.Assets:N0} assets")
                    AnsiConsole.MarkupLine($"  Capsule: {report.CompressedSize:N0} bytes")
                    AnsiConsole.MarkupLine($"  Uncompressed: {report.UncompressedSize:N0} bytes")
                    AnsiConsole.MarkupLine($"  SHA-256: {report.Sha256}")
                    0

                elif results.Contains HistoryAdd then
                    let version = results.GetResult HistoryAdd
                    let indexPath = results.GetResult(Output, defaultValue = ".livedocs/history.json")
                    historyAddAction
                        indexPath
                        version
                        (results.TryGetResult Capsule)
                        (results.TryGetResult Url)
                        (results.TryGetResult Sha256)

                elif results.Contains Extract then
                    printBanner()
                    let projectPaths = results.GetResult Extract |> resolveProjects "extract"
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
                    let projectPaths = results.GetResult Test |> resolveProjects "test"
                    let mutable allPassed = auditAction (results.Contains Warn_As_Error) projectPaths = 0
                    // The same references the generated cases receive. Passing none, as the retired
                    // path did, made any example touching another project fail for want of a
                    // reference rather than for anything wrong with the example.
                    let references =
                        projectPaths
                        |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                        |> List.filter (String.IsNullOrWhiteSpace >> not)
                        |> List.distinct
                    for projectPath in projectPaths do
                        AnsiConsole.MarkupLine($"[bold blue]➜ Testing:[/] {projectPath}")
                        let snapshots =
                            AnsiConsole.Status().Start($"Running doc-tests...", fun ctx ->
                                let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                                DocTestRunner.snapshotExampleNames package
                                |> List.map (fun name ->
                                    DocTestRunner.collectSnapshotByName package projectPath references name
                                    |> Async.RunSynchronously))

                        for snapshot in snapshots do
                            match snapshot.Status with
                            | ExampleStatus.Verified | ExampleStatus.FirstCut ->
                                AnsiConsole.MarkupLine($"  [green]pass[/] {Markup.Escape(snapshot.Name)}")
                            | ExampleStatus.Mismatch ->
                                AnsiConsole.MarkupLine($"  [red]fail[/] {Markup.Escape(snapshot.Name)}")
                                let expected = snapshot.ExpectedOutput |> Option.defaultValue ""
                                AnsiConsole.MarkupLine($"       [grey]Expected:[/] {Markup.Escape(expected)}")
                                AnsiConsole.MarkupLine($"       [grey]Actual:[/] {Markup.Escape(snapshot.ActualOutput)}")
                                allPassed <- false
                            | ExampleStatus.Error ->
                                AnsiConsole.MarkupLine($"  [red]fail[/] {Markup.Escape(snapshot.Name)}")
                                AnsiConsole.MarkupLine($"       [grey]{Markup.Escape(snapshot.ActualOutput)}[/]")
                                allPassed <- false
                    // Executable markdown blocks are compiled by the audit above but only run by
                    // the generated cases; running them here is what makes this command a real
                    // alternative to generating a test project rather than a subset of one.
                    let package, _ = getUnifiedPackage projectPaths |> Async.RunSynchronously
                    for page in documentationPages projectPaths package do
                        let externallyExecuted =
                            page.Blocks
                            |> List.choose (fun block ->
                                match block.Mode, block.Origin with
                                | (Run | Transcript), XmlExample -> Some block.Id
                                | _ -> None)
                            |> Set.ofList
                        let cases =
                            DocumentationDiscovery.generatedCases
                                page.SelectedProject "" page.Relative page.Expanded externallyExecuted
                        for case in cases do
                            match case.Action with
                            | ExecuteBlock _ | ExecuteTranscriptBlock _ ->
                                try
                                    GeneratedVerification.runCase references case |> Async.RunSynchronously
                                    AnsiConsole.MarkupLine($"  [green]pass[/] {Markup.Escape(case.Id)}")
                                with error ->
                                    AnsiConsole.MarkupLine($"  [red]fail[/] {Markup.Escape(case.Id)}")
                                    AnsiConsole.MarkupLine($"       [grey]{Markup.Escape(error.Message)}[/]")
                                    allPassed <- false
                            | _ -> ()

                    if allPassed then 
                        AnsiConsole.MarkupLine("\n[bold green]✔ All doc-tests passed successfully![/]")
                        0 
                    else 
                        AnsiConsole.MarkupLine("\n[bold red]✖ Some doc-tests failed.[/]")
                        1

                elif results.Contains Audit then
                    printBanner()
                    auditAction (results.Contains Warn_As_Error) (results.GetResult Audit |> resolveProjects "audit")

                elif results.Contains Build then
                    printBanner()
                    let projectPaths = results.GetResult Build |> resolveProjects "build"
                    buildAction (results.Contains Warn_As_Error) projectPaths theme (results.TryGetResult Version)
                    0

                elif results.Contains BuildHistory then
                    printBanner()
                    buildHistoryAction (results.GetResult BuildHistory) theme
                    0

                elif results.Contains Watch then
                    printBanner()
                    let projectPaths = results.GetResult Watch |> resolveProjects "watch"
                    let version = results.TryGetResult Version
                    let host = results.GetResult(Host, defaultValue = "0.0.0.0")
                    let port = results.GetResult(Port, defaultValue = 5000)
                    if String.IsNullOrWhiteSpace host then invalidArg "host" "Preview host must not be empty."
                    if port < 1 || port > 65535 then invalidArg "port" "Preview port must be between 1 and 65535."
                    let previewUrl = $"http://{host}:{port}"
                    let buildPreview () =
                        // buildAction owns documentation verification and diagnostic reporting. Running
                        // auditAction first duplicates both in watch output without adding coverage.
                        buildAction (results.Contains Warn_As_Error) projectPaths theme version
                    buildPreview ()
                    
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
                                buildPreview
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
            | :? InvalidOperationException as e ->
                AnsiConsole.MarkupLine($"[red]✖ {Markup.Escape(e.Message)}[/]")
                1
            | e ->
                AnsiConsole.WriteException(e)
                1
