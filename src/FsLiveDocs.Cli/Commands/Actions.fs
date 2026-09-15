namespace FsLiveDocs.Cli

open System
open System.IO
open Argu
open Spectre.Console
open Axial
open Axial.FileSystem
open Reified
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects
open FsLiveDocs.Core.Schema
open FsLiveDocs.Runner
open FsLiveDocs.Renderer
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.FileProviders
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging


/// CLI command actions: the implementation behind each "livedocs" subcommand.
/// Program.fs stays a thin entry point that parses arguments and dispatches here.
module Actions =

    let private packageCodec = Json.compile ApiSchema.packageModel
    let private apiModelArtifactCodec = Json.compile ApiSchema.apiModelArtifact
    let private semanticArtifactCodec = Json.compile SemanticSchema.semanticDocumentationArtifact

    let private configuredDocsSets projectPaths =
        Workspace.loadDocsSetConfigs ()
        |> Option.map (fun configured ->
            let site = Workspace.loadSiteConfig ()
            DocsSet.resolve site.SiteName projectPaths site.FSharpPrelude (Some configured))

    let internal documentationPages projectPaths package =
        match configuredDocsSets projectPaths with
        | Some sets -> DocAnalysis.pagesForDocsSets sets projectPaths package
        | None ->
            let prelude = Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue ""

            DocAnalysis.pages projectPaths package
            |> List.map (fun page -> { page with Prelude = prelude })

    let resolveProjects command projectPaths =
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
    let printApiDiagnostics (warnAsError: bool) (diagnostics: ApiDiagnostic list) =
        if diagnostics.IsEmpty then
            0
        else
            let label = if warnAsError then "[red]error[/]" else "[yellow]warning[/]"
            let root = Run.orRaise FileSystemError.describe "Could not determine the current directory" FileSystem.getCurrentDirectory
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

        match configuredDocsSets projectPaths with
        | Some sets ->
            DocAnalysis.analyzeDocsSetsWithProgress reportProgress sets projectPaths projectFingerprint package
        | None -> DocAnalysis.analyzeWithProgress reportProgress prelude projectPaths projectFingerprint package

    let private analyzeDocumentation projectPaths projectFingerprint package =
        let prelude = Workspace.loadSiteConfig().FSharpPrelude |> Option.defaultValue ""

        match configuredDocsSets projectPaths with
        | Some sets -> DocAnalysis.analyzeDocsSets sets projectPaths projectFingerprint package
        | None -> DocAnalysis.analyze prelude projectPaths projectFingerprint package

    let private printAudit showSuccess (analysis: DocAnalysis.Analysis) =
        let diagnosticsByBlock = analysis.Errors |> List.groupBy fst |> Map.ofList
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

    let createSemanticArtifact (projectPaths: string list) (package: PackageModel) =
        let analysis = analyzeDocumentation projectPaths (PackageExtraction.inputFingerprint projectPaths) package
        DocAnalysis.semanticArtifact analysis, analysis.Prelude

    let captureAction warnAsError dryRun projectPaths version output =
        let result =
            ReleaseCapture.capture
                { ProjectPaths = projectPaths
                  Version = version
                  OutputPath = output
                  DryRun = dryRun
                  WarnAsError = warnAsError
                  Site = Workspace.loadSiteConfig ()
                  DocsSets = configuredDocsSets projectPaths
                  ToolVersion = Reflection.Assembly.GetExecutingAssembly().GetName().Version |> string
                  ReportProgress = (fun _ _ _ -> ())
                  ReportAudit = (fun analysis -> printAudit true analysis |> ignore)
                  ReportApiDiagnostics =
                    (fun treatAsError diagnostics -> printApiDiagnostics treatAsError diagnostics |> ignore) }

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
            let work =
                flow {
                    let! exists = FileSystem.fileExists file
                    if not exists then invalidOp $"SHA-256 file is missing: {Path.GetFullPath file}"
                    return! FileSystem.readAllText file
                }
            (Run.orRaise FileSystemError.describe $"Could not read SHA-256 file {file}" work).Trim().ToLowerInvariant()
        | None, None ->
            match localCapsule with
            | Some path -> History.sha256 path
            | None -> invalidOp "A capsule URL requires --sha256 or --sha256-file."

    let historyAddAction indexPath version capsulePath capsuleUrl checksum sha256File =
        let index =
            if Run.orFallback (FileSystem.fileExists indexPath) false then ReleaseCapsule.loadHistoryIndex indexPath
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
                if not (Run.orRaise FileSystemError.describe $"Could not check release capsule {fullPath}" (FileSystem.fileExists fullPath)) then
                    invalidOp $"Release capsule is missing: {fullPath}"
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

    let generateSnapshotTests (projectPaths: string list) =
        printBanner()

        if List.isEmpty projectPaths then
            AnsiConsole.MarkupLine("[red]No project paths were provided.[/]")
            1
        else
            let outputDir = Path.GetFullPath("tests/FsLiveDocs.SnapshotTests")
            Run.orRaise FileSystemError.describe $"Could not create directory {outputDir}" (FileSystem.createDirectory outputDir)
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
                              page.Prelude
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

    /// <summary>Reports blog authoring warnings, failing the build when warnings are errors.</summary>
    let private reportBlogDiagnostics warnAsError reportNote (pages: ContentPage list) =
        match Blog.diagnostics pages with
        | [] -> ()
        | warnings when warnAsError ->
            invalidOp ("Blog warnings were treated as errors because --warn-as-error was passed:" + Environment.NewLine + String.concat Environment.NewLine warnings)
        | warnings -> for warning in warnings do reportNote $"Warning: {warning}"

    /// <summary>Orchestrates the build process for one or more projects.</summary>
    let buildAction (warnAsError: bool) (includeDrafts: bool) (projectPaths: string list) (theme: string) (version: string option) =
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
            let sourceDir = Run.orRaise FileSystemError.describe "Could not determine the current directory" FileSystem.getCurrentDirectory
            let semanticCode =
                { SemanticCode.defaults with
                    Artifact = Some semanticArtifact
                    Prelude = prelude }

            let config = Workspace.loadSiteConfig ()

            let historyDir = ".livedocs/history"
            Run.orRaise FileSystemError.describe $"Could not create directory {historyDir}" (FileSystem.createDirectory historyDir)

            match configuredDocsSets projectPaths with
            | Some sets ->
                let prepared = DocumentationSets.prepareCurrent true sets packageRaw semanticArtifact ""

                let prepared =
                    { prepared with
                        Sites =
                            prepared.Sites
                            |> List.map (fun site ->
                                { site with Pages = site.Pages |> List.filter (fun page -> includeDrafts || not page.Metadata.Draft) }) }

                prepared.Sites |> List.collect _.Pages |> reportBlogDiagnostics warnAsError reportNote

                let current: SiteBuilder.DocsSetVersionSite =
                    { Version = packageRaw.Version
                      Package = packageRaw
                      Sets = prepared.Sites
                      StaticRoot = None
                      UsesDocumentationSets = true }

                let historyWork =
                    flow {
                        let! files = FileSystem.getFiles historyDir "*.json" SearchOption.TopDirectoryOnly
                        return!
                            files
                            |> Array.toList
                            |> Flow.traverse (fun path -> FileSystem.readAllText path |> Flow.map (fun text -> path, text))
                    }
                let historyFiles = Run.orRaise FileSystemError.describe $"Could not scan history directory {historyDir}" historyWork

                let historical =
                    historyFiles
                    |> List.map (fun (path, text) ->
                        let historicalPackage = Json.deserialize packageCodec text

                        let historicalPrepared =
                            DocumentationSets.prepareCurrent true sets historicalPackage semanticArtifact ""

                        let historicalPrepared =
                            { historicalPrepared with
                                Sites =
                                    historicalPrepared.Sites
                                    |> List.map (fun site ->
                                        { site with Pages = site.Pages |> List.filter (fun page -> includeDrafts || not page.Metadata.Draft) }) }

                        ({ Version = Path.GetFileNameWithoutExtension path
                           Package = historicalPackage
                           Sets = historicalPrepared.Sites
                           StaticRoot = None
                           UsesDocumentationSets = true }
                        : SiteBuilder.DocsSetVersionSite))

                SiteBuilder.buildDocsSetsHistory packageRaw.Version (current :: historical) config theme "output"

                for source, prefix, files in prepared.StaticFiles do
                    ContentProvider.copyStaticFilesForSet source prefix files "output"
            | None ->
                let package =
                    ContentProvider.applyApiDocsWithOptions "docs" sourceDir packageRaw semanticCode

                let pages =
                    ContentProvider.scanDocsWithOptions "docs" sourceDir package "" semanticCode
                    |> List.filter (fun page -> includeDrafts || not page.Metadata.Draft)

                reportBlogDiagnostics warnAsError reportNote pages

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
        let raw = Run.orRaise FileSystemError.describe $"Could not read {manifestPath}" (FileSystem.readAllText manifestPath)
        let isCapsuleIndex =
            raw.Contains("\"CapsulePath\"", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("\"CapsuleUrl\"", StringComparison.OrdinalIgnoreCase)
        if isCapsuleIndex then
            let index = ReleaseCapsule.loadHistoryIndex manifestPath
            let indexRoot = Path.GetDirectoryName(Path.GetFullPath manifestPath)
            let temporaryRoot = Path.Combine(Path.GetTempPath(), "fslivedocs-history-" + Guid.NewGuid().ToString("N"))
            Run.orRaise FileSystemError.describe $"Could not create directory {temporaryRoot}" (FileSystem.createDirectory temporaryRoot)
            try
                let loaded =
                    index.Entries
                    |> List.map (fun entry ->
                        let capsulePath = ReleaseCapsule.acquireWithRetries retryAttempts indexRoot (Path.GetFullPath(".livedocs/releases")) entry
                        let docsDir = Path.Combine(temporaryRoot, entry.Version, "docs")

                        let packageRaw, semanticArtifact, content =
                            ReleaseCapsule.materializeContentWithSets capsulePath docsDir

                        if packageRaw.Version <> entry.Version then
                            invalidOp
                                $"Release capsule version mismatch: expected {entry.Version}, got {packageRaw.Version}."

                        // Authored and API links are relative to each version's own output root.
                        // SiteBuilder separately supplies the path back to shared site assets and
                        // the version switcher; using that path here would point old pages at latest.
                        let contentRootPath = ""

                        let sites =
                            if content.UsesDocumentationSets then
                                (DocumentationSets.prepareCaptured docsDir content packageRaw semanticArtifact "")
                                    .Sites
                            else
                                let semanticCode =
                                    { SemanticCode.defaults with
                                        Artifact = Some semanticArtifact
                                        Prelude = semanticArtifact.Prelude }

                                let package =
                                    ContentProvider.applyApiDocsWithOptions docsDir docsDir packageRaw semanticCode

                                let pages =
                                    ContentProvider.scanDocsWithOptions docsDir docsDir package contentRootPath semanticCode

                                [ { Set = content.DocsSets.Head
                                    Package = package
                                    Pages = pages } ]

                        ({ Version = entry.Version
                           Package = packageRaw
                           Sets = sites
                           StaticRoot = Some docsDir
                           UsesDocumentationSets = content.UsesDocumentationSets }
                        : SiteBuilder.DocsSetVersionSite),
                        content.Site)

                let config =
                    loaded
                    |> List.find (fun (site, _) -> site.Version = index.CurrentVersion)
                    |> snd

                let sites = loaded |> List.map fst
                SiteBuilder.buildDocsSetsHistory index.CurrentVersion sites config theme outputDir
            finally
                let cleanupWork =
                    flow {
                        let! exists = FileSystem.directoryExists temporaryRoot
                        if exists then do! FileSystem.deleteDirectory temporaryRoot true
                    }
                Run.orRaise FileSystemError.describe $"Could not remove temporary directory {temporaryRoot}" cleanupWork
        else
            let manifest, entries = History.loadManifest manifestPath
            let config = Workspace.loadSiteConfig()
            let docsDirExistence =
                let work =
                    entries
                    |> List.map (fun (_, _, docsDir) -> docsDir)
                    |> List.distinct
                    |> Flow.traverse (fun docsDir -> FileSystem.directoryExists docsDir |> Flow.map (fun exists -> docsDir, exists))
                Run.orRaise FileSystemError.describe "Could not check history docs trees" work
                |> Map.ofList
            let sites =
                entries
                |> List.map (fun (entry, modelPath, docsDir) ->
                    if not (docsDirExistence |> Map.find docsDir) then
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
                    // Guide and API links stay inside the version being rendered. SiteBuilder owns
                    // the separate relative path used for shared shell/version navigation.
                    let pages = ContentProvider.scanDocsWithOptions docsDir sourceDir package "" semanticCode
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
        if not (Run.orRaise FileSystemError.describe $"Could not check {indexPath}" (FileSystem.fileExists indexPath)) then
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
                if not (Run.orRaise FileSystemError.describe $"Could not check release capsule {fullPath}" (FileSystem.fileExists fullPath)) then
                    invalidOp $"Release capsule is missing: {fullPath}"
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
        Run.orRaise FileSystemError.describe $"Could not create directory {workRoot}" (FileSystem.createDirectory workRoot)
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
            let cleanupWork =
                flow {
                    let! exists = FileSystem.directoryExists workRoot
                    if exists then do! FileSystem.deleteDirectory workRoot true
                }
            Run.orRaise FileSystemError.describe $"Could not remove temporary directory {workRoot}" cleanupWork
