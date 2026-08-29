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

module Program =

    let private resolveProjects command projectPaths =
        Workspace.resolveProjects
            (fun count -> AnsiConsole.MarkupLine($"[grey]Discovered {count} project(s). Pass paths explicitly, or run 'livedocs init --discover-projects' to record the selection.[/]"))
            command
            projectPaths

    let printBanner () =
        if ConsoleOutput.banner && not ConsoleOutput.animateBanner then
            let figlet = FigletText("LiveDocs")
            figlet.Color <- Color.Blue
            AnsiConsole.Write(figlet)
            AnsiConsole.MarkupLine("[grey]Verified Documentation for F#[/]\n")

    let private getUnifiedPackageWithProgress reportProgress projectPaths =
        PackageExtraction.extractWithProgress reportProgress (Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue "") projectPaths

    let getUnifiedPackage projectPaths =
        PackageExtraction.extract (Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue "") projectPaths

    let private getUnifiedPackageCachedWithProgress reportProgress projectPaths =
        PackageExtraction.extractCachedWithProgress reportProgress (Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue "") projectPaths

    let private getUnifiedPackageCached projectPaths =
        PackageExtraction.extractCached (Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue "") projectPaths

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
            let repoUrl = Workspace.loadSiteConfig().RepoUrl |> Option.map (fun value -> value.TrimEnd('/'))
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

    let private analyzeDocumentationWithProgress reportProgress projectPaths projectFingerprint package =
        let prelude = Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue ""
        DocAnalysis.analyzeWithProgress reportProgress prelude projectPaths projectFingerprint package

    let private analyzeDocumentation projectPaths projectFingerprint package =
        let prelude = Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue ""
        DocAnalysis.analyze prelude projectPaths projectFingerprint package

    let private printAudit showSuccess (analysis: DocAnalysis.Analysis) =
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
        let analysis = analyzeDocumentation projectPaths (PackageExtraction.inputFingerprint projectPaths) package
        DocAnalysis.semanticArtifact analysis, analysis.Prelude

    let captureAction warnAsError dryRun projectPaths version output =
        let result =
            ReleaseCapture.capture {
                ProjectPaths = projectPaths
                Version = version
                OutputPath = output
                DryRun = dryRun
                WarnAsError = warnAsError
                Site = Workspace.loadSiteConfig()
                ToolVersion = Reflection.Assembly.GetExecutingAssembly().GetName().Version |> string
                ReportProgress = (fun _ _ _ -> ())
                ReportAudit = (fun analysis -> printAudit true analysis |> ignore)
                ReportApiDiagnostics = (fun treatAsError diagnostics -> printApiDiagnostics treatAsError diagnostics |> ignore)
            }
        let report = result.Report
        if result.DryRun then
            AnsiConsole.MarkupLine("[green]✔ Release capture dry run complete.[/]")
            AnsiConsole.MarkupLine($"  Planned output: {Markup.Escape result.PlannedOutputPath}")
        else
            AnsiConsole.MarkupLine($"[green]✔ Release capsule:[/] {Markup.Escape report.Path}")
            AnsiConsole.MarkupLine($"  Report: {Markup.Escape result.ReportPath.Value}")
        AnsiConsole.MarkupLine($"  Version: [blue]{Markup.Escape report.Manifest.ProductVersion}[/]")
        AnsiConsole.MarkupLine($"  API: {report.Manifest.Api.Size:N0} bytes")
        AnsiConsole.MarkupLine($"  Semantic: {report.Manifest.Semantic.Size:N0} bytes")
        AnsiConsole.MarkupLine($"  Content: {report.Manifest.Content.Size:N0} bytes")
        AnsiConsole.MarkupLine($"  Inventory: {report.Counts.Entities:N0} entities, {report.Counts.Members:N0} members, {report.Counts.DocumentationNodes:N0} documentation nodes, {report.Counts.Examples:N0} examples")
        AnsiConsole.MarkupLine($"  Content: {report.Counts.Pages:N0} pages, {report.Counts.CodeBlocks:N0} code blocks, {report.Counts.Tooltips:N0} tooltips, {report.Counts.Diagnostics:N0} diagnostics, {report.Counts.Assets:N0} assets")
        AnsiConsole.MarkupLine($"  Compressed: {report.CompressedSize:N0} bytes")
        AnsiConsole.MarkupLine($"  Uncompressed: {report.UncompressedSize:N0} bytes")
        AnsiConsole.MarkupLine($"  SHA-256: {report.Sha256}")
        0

    /// Expands `{version}` and `{tag}` in a configured capsule URL pattern. The pattern is a
    /// plain format string the repository owns; the tool has no provider knowledge.
    let expandUrlPattern (pattern: string) (version: string) =
        pattern.Replace("{version}", version).Replace("{tag}", "v" + version)

    let private resolveChecksum (checksum: string option) (sha256File: string option) (localCapsule: string option) =
        match checksum, sha256File with
        | Some value, _ -> value.Trim().ToLowerInvariant()
        | None, Some file ->
            if not (File.Exists file) then invalidOp $"SHA-256 file is missing: {Path.GetFullPath file}"
            (File.ReadAllText file).Trim().ToLowerInvariant()
        | None, None ->
            match localCapsule with
            | Some path -> History.sha256 path
            | None -> invalidOp "A capsule URL requires --sha256 or --sha256-file."

    let historyAddAction indexPath version capsulePath capsuleUrl checksum sha256File =
        let index =
            if File.Exists indexPath then ReleaseCapsule.loadHistoryIndex indexPath
            else { SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion; CurrentVersion = version; Entries = [] }
        if index.Entries |> List.exists (fun entry -> entry.Version = version) then
            invalidOp $"Release history already contains version {version}. Published entries are immutable."
        let indexRoot = Path.GetDirectoryName(Path.GetFullPath indexPath)
        let configuredUrl =
            Workspace.loadHistoryConfig().UrlPattern
            |> Option.map (fun pattern -> expandUrlPattern pattern version)
        let path, url, sha256 =
            match capsulePath, (capsuleUrl |> Option.orElse configuredUrl) with
            | Some path, _ ->
                let fullPath = Path.GetFullPath path
                if not (File.Exists fullPath) then invalidOp $"Release capsule is missing: {fullPath}"
                Some(Path.GetRelativePath(indexRoot, fullPath)), None, resolveChecksum checksum sha256File (Some fullPath)
            | None, Some url ->
                None, Some url, resolveChecksum checksum sha256File None
            | None, None ->
                invalidOp "Specify --capsule, --url, or configure history.urlPattern in .livedocs/config.json."
        let updated =
            {
                index with
                    CurrentVersion = version
                    Entries = { Version = version; CapsulePath = path; CapsuleUrl = url; CapsuleSha256 = sha256 } :: index.Entries
            }
        ReleaseCapsule.saveHistoryIndex indexPath updated
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

            let fsproj = Templates.snapshotProject eol projectRefs toolReferences

            let projectExamples =
                resolvedProjects
                |> List.map (fun projectPath ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    projectPath, DocTestRunner.snapshotExampleNames package)

            let testBodies =
                projectExamples
                |> List.mapi (fun index (projectPath, exampleNames) ->
                    Templates.xmlFacts eol assemblyReferenceLiteral index projectPath exampleNames)
                |> String.concat (eol + eol)

            let package, _ = getUnifiedPackage resolvedProjects |> Async.RunSynchronously
            let documentationCases =
                [ for page in DocAnalysis.pages resolvedProjects package do
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
                |> List.map (Templates.documentationFact eol assemblyReferenceLiteral)
                |> String.concat eol

            let testsFs = Templates.snapshotTests eol testBodies documentationTestBodies

            let fsprojPath = Path.Combine(outputDir, "FsLiveDocs.SnapshotTests.fsproj")
            let testsPath = Path.Combine(outputDir, "SnapshotTests.fs")

            Workspace.writeIfChanged fsprojPath fsproj
            Workspace.writeIfChanged testsPath testsFs

            AnsiConsole.MarkupLine($"[green]✔ Snapshot test project generated:[/] {outputDir}")
            0

    let private prepareBuildDocumentation reportProgress reportNote projectPaths projectFingerprint package =
        let analysis = analyzeDocumentationWithProgress reportProgress projectPaths projectFingerprint package
        if printAudit false analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before building."
        let excluded = analysis.Blocks |> List.filter (fun block -> match block.Mode with NoCheck _ -> true | _ -> false) |> List.length
        let verified = analysis.Blocks.Length - excluded
        reportNote $"Audit complete: {analysis.Blocks.Length} blocks — {verified} verified, {excluded} excluded, 0 failed."
        DocAnalysis.semanticArtifact analysis, analysis.Prelude

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
            let config = Workspace.loadSiteConfig()

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

    /// Renders every version in a manifest into <paramref name="outputDir"/>. Shared by
    /// `build-history` (which then indexes the site) and `history check` (which verifies it).
    let renderHistoryInto (manifestPath: string) (theme: string) (retryAttempts: int) (outputDir: string) =
        if retryAttempts < 1 then invalidArg "retry" "Retry attempts must be at least one."
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
                        let capsulePath = ReleaseCapsule.acquireWithRetries retryAttempts indexRoot (Path.GetFullPath(".livedocs/releases")) entry
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
                SiteBuilder.buildHistory index.CurrentVersion sites config theme outputDir
            finally
                if Directory.Exists temporaryRoot then Directory.Delete(temporaryRoot, true)
        else
            let manifest, entries = History.loadManifest manifestPath
            let config = Workspace.loadSiteConfig()
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

            SiteBuilder.buildHistory manifest.CurrentVersion sites config theme outputDir

    let private runPagefind (siteDir: string) =
        let psi = System.Diagnostics.ProcessStartInfo("npx", $"-y pagefind --site {siteDir}")
        psi.UseShellExecute <- false
        use proc = System.Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        if proc.ExitCode <> 0 then invalidOp $"Pagefind failed with exit code {proc.ExitCode}."

    let buildHistoryAction manifestPath theme retryAttempts =
        renderHistoryInto manifestPath theme retryAttempts "output"
        runPagefind "output"
        AnsiConsole.MarkupLine("[green]✔ History build complete:[/] output/")

    /// Renders the committed history — optionally with a local candidate capsule spliced in as
    /// the release under test — into a temporary directory and verifies it. Never writes the index.
    let historyCheckAction (indexPath: string) (candidateCapsule: string option) (candidateVersion: string option) (theme: string) (retryAttempts: int) =
        if not (File.Exists indexPath) then
            invalidOp $"Release history index is missing: {Path.GetFullPath indexPath}"
        let index = ReleaseCapsule.loadHistoryIndex indexPath
        let indexRoot = Path.GetDirectoryName(Path.GetFullPath indexPath)
        // Resolve committed relative capsule paths to absolute so a temp index elsewhere still finds them.
        let absoluteEntries =
            index.Entries
            |> List.map (fun entry ->
                match entry.CapsulePath with
                | Some relative -> { entry with CapsulePath = Some(Path.GetFullPath(Path.Combine(indexRoot, relative))) }
                | None -> entry)
        let candidate =
            match candidateCapsule, candidateVersion with
            | Some capsule, Some version ->
                let fullPath = Path.GetFullPath capsule
                if not (File.Exists fullPath) then invalidOp $"Release capsule is missing: {fullPath}"
                if index.Entries |> List.exists (fun entry -> entry.Version = version) then
                    invalidOp $"Release history already contains version {version}. Published entries are immutable."
                Some { Version = version; CapsulePath = Some fullPath; CapsuleUrl = None; CapsuleSha256 = History.sha256 fullPath }
            | Some _, None -> invalidOp "history check --capsule requires --version."
            | None, Some _ -> invalidOp "history check --version requires --capsule."
            | None, None -> None
        let merged =
            ReleaseCapsule.normalizeHistoryIndex
                { index with Entries = (candidate |> Option.toList) @ absoluteEntries }
        let workRoot = Path.Combine(Path.GetTempPath(), "fslivedocs-check-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory workRoot |> ignore
        let tempIndex = Path.Combine(workRoot, "history.json")
        let tempOutput = Path.Combine(workRoot, "output")
        try
            ReleaseCapsule.saveHistoryIndex tempIndex merged
            renderHistoryInto tempIndex theme retryAttempts tempOutput
            // The search index is a separate downstream step; `verify` skips `pagefind/` links.
            let pageCount = ReleaseHistoryCommands.verify tempIndex tempOutput
            match candidate with
            | Some entry ->
                AnsiConsole.MarkupLine($"[green]✔ Candidate {Markup.Escape entry.Version} renders and verifies in the full release history.[/]")
            | None ->
                AnsiConsole.MarkupLine("[green]✔ Release history renders and verifies.[/]")
            AnsiConsole.MarkupLine($"  Releases: {merged.Entries.Length}, pages: {pageCount}")
            0
        finally
            if Directory.Exists workRoot then Directory.Delete(workRoot, true)

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
                    let discovered = Workspace.initialize (results.Contains Discover_Projects)
                    match discovered with
                    | Some (count, configPath) ->
                        AnsiConsole.MarkupLine($"[green]✔ Recorded {count} project(s):[/] {Markup.Escape configPath}")
                    | None -> ()
                    AnsiConsole.MarkupLine("[green]✔ Done![/]")
                    0

                elif results.Contains Generate_CI then
                    printBanner()
                    match results.GetResult(Provider, defaultValue = "github").ToLowerInvariant() with
                    | "github" ->
                        AnsiConsole.MarkupLine("[blue]Generating GitHub Actions workflow...[/]")
                        if not (Directory.Exists(".github/workflows")) then Directory.CreateDirectory(".github/workflows") |> ignore
                        if File.Exists(".github/workflows/livedocs.yml") then
                            invalidOp ".github/workflows/livedocs.yml already exists. Delete it to regenerate."
                        File.WriteAllText(".github/workflows/livedocs.yml", Templates.GitHubWorkflow)
                        AnsiConsole.MarkupLine("[green]✔ Done:[/] .github/workflows/livedocs.yml")
                        0
                    | other -> invalidOp $"Unknown --provider '{other}'. Supported: github. Other hosts follow the generic recipe in docs/guides/continuous-integration.md."

                elif results.Contains Generate_Tests then
                    let projectPaths = results.GetResult Generate_Tests |> resolveProjects "generate-tests"
                    generateSnapshotTests projectPaths

                elif results.Contains Capture then
                    printBanner()
                    let projectPaths = results.GetResult Capture |> resolveProjects "capture"
                    let version = results.TryGetResult Arguments.Version
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

                elif results.Contains History_Add then
                    let version =
                        match results.GetResult History_Add, results.TryGetResult Arguments.Version with
                        | [ positional ], _ -> positional
                        | [], Some flag -> flag
                        | [], None -> invalidOp "history-add needs a version, positionally or as --version."
                        | _ -> invalidOp "history-add takes one version."
                    let indexPath = results.GetResult(Output, defaultValue = ".livedocs/history.json")
                    historyAddAction
                        indexPath
                        version
                        (results.TryGetResult Capsule)
                        (results.TryGetResult Url)
                        (results.TryGetResult Sha256)
                        (results.TryGetResult Sha256_File)

                elif results.Contains History_Check then
                    let indexPath = results.GetResult(Output, defaultValue = ".livedocs/history.json")
                    historyCheckAction
                        indexPath
                        (results.TryGetResult Capsule)
                        (results.TryGetResult Arguments.Version)
                        theme
                        (results.GetResult(Retry, defaultValue = 3))

                elif results.Contains History_Sync then
                    let indexPath = results.GetResult(Output, defaultValue = ".livedocs/history.json")
                    let source =
                        match results.GetResult History_Sync, results.TryGetResult From, Workspace.loadHistoryConfig().Discover with
                        | [ repository ], _, _ -> ReleaseHistoryCommands.GithubRepo repository
                        | [], Some command, _ -> ReleaseHistoryCommands.Command command
                        | [], None, Some command -> ReleaseHistoryCommands.Command command
                        | [], None, None ->
                            invalidOp "history-sync needs a GitHub owner/repo argument, --from \"<command>\", or history.discover in .livedocs/config.json."
                        | _ -> invalidOp "history-sync accepts at most one repository argument."
                    let updated =
                        ReleaseHistoryCommands.sync source indexPath
                            (results.TryGetResult Arguments.Version)
                            (results.TryGetResult Url)
                            (results.TryGetResult Sha256)
                    AnsiConsole.MarkupLine($"[green]✔ History synchronized:[/] {Markup.Escape(Path.GetFullPath indexPath)}")
                    AnsiConsole.MarkupLine($"  Current: [blue]{Markup.Escape updated.CurrentVersion}[/]")
                    AnsiConsole.MarkupLine($"  Releases: {updated.Entries.Length}")
                    0

                elif results.Contains Verify_Output then
                    let manifestPath = results.GetResult Verify_Output
                    let outputPath = results.GetResult(Output, defaultValue = "output")
                    let pageCount = ReleaseHistoryCommands.verify manifestPath outputPath
                    let releaseCount = (ReleaseCapsule.loadHistoryIndex manifestPath).Entries.Length
                    AnsiConsole.MarkupLine($"[green]✔ History output verified:[/] {releaseCount} versions, {pageCount} HTML pages")
                    0

                elif results.Contains Extract then
                    printBanner()
                    let projectPaths = results.GetResult Extract |> resolveProjects "extract"
                    let mutable extractDiagnostics = []
                    AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                        let packageRaw, apiDiagnostics = getUnifiedPackage projectPaths |> Async.RunSynchronously
                        extractDiagnostics <- apiDiagnostics
                        let version = results.GetResult(Arguments.Version, defaultValue = packageRaw.Version)
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
                    for page in DocAnalysis.pages projectPaths package do
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
                    buildAction (results.Contains Warn_As_Error) projectPaths theme (results.TryGetResult Arguments.Version)
                    0

                elif results.Contains Build_History then
                    printBanner()
                    buildHistoryAction (results.GetResult Build_History) theme (results.GetResult(Retry, defaultValue = 3))
                    0

                elif results.Contains Watch then
                    printBanner()
                    let projectPaths = results.GetResult Watch |> resolveProjects "watch"
                    let version = results.TryGetResult Arguments.Version
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
