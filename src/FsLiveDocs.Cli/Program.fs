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


module Program =

    let private apiModelArtifactCodec = Json.compile ApiSchema.apiModelArtifact
    let private semanticArtifactCodec = Json.compile SemanticSchema.semanticDocumentationArtifact

    /// <summary>CLI entry point.</summary>
    [<EntryPoint>]
    let main args =
        let parser = ArgumentParser.Create<Arguments>(programName = "livedocs")
        
        let printUsage (msg: string option) =
            Actions.printBanner()
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
                Timing.configure (results.Contains Timings)
                Timing.label (args |> Array.truncate 3 |> String.concat " ")
                ConsoleOutput.animateBanner <-
                    ConsoleOutput.interactive && ConsoleOutput.banner && (results.Contains Build || results.Contains Watch)
                let theme = results.GetResult(Theme, defaultValue = "light")
                
                if results.Contains Tool_Version then
                    let version = typeof<Arguments>.Assembly.GetName().Version.ToString(3)
                    Console.WriteLine($"FsLiveDocs {version}")
                    0

                elif results.Contains Init then
                    Actions.printBanner()
                    AnsiConsole.MarkupLine("[blue]Scaffolding new project...[/]")
                    let indexExists = File.Exists("docs/index.md")
                    let readmeExists = File.Exists("docs/README.md")
                    let useReadmeAsHome =
                        if Workspace.shouldAskForReadmeHome indexExists readmeExists then
                            if Console.IsInputRedirected || Console.IsOutputRedirected || not AnsiConsole.Profile.Capabilities.Interactive then
                                AnsiConsole.MarkupLine("[grey]Using docs/README.md as the home page because init cannot prompt in this console.[/]")
                                true
                            else
                                AnsiConsole.Confirm("Use docs/README.md as the site home page?", true)
                        else
                            false
                    let discovered = Workspace.initialize (results.Contains Discover_Projects) useReadmeAsHome
                    match discovered with
                    | Some (count, configPath, projects) ->
                        AnsiConsole.MarkupLine($"[green]✔ Recorded {count} project(s):[/] {Markup.Escape configPath}")
                        for project in Workspace.projectsWithoutGenerateDocumentationFile projects do
                            AnsiConsole.MarkupLine($"[yellow]⚠ Project {Markup.Escape project} does not enable XML documentation.[/]")
                            AnsiConsole.MarkupLine("  Add this to the project or Directory.Build.props:")
                            AnsiConsole.MarkupLine("  [grey]<GenerateDocumentationFile>true</GenerateDocumentationFile>[/]")
                    | None -> ()
                    AnsiConsole.MarkupLine("[green]✔ Done![/]")
                    0

                elif results.Contains Generate_CI then
                    Actions.printBanner()
                    match results.GetResult(Provider, defaultValue = "github").ToLowerInvariant() with
                    | "github" ->
                        AnsiConsole.MarkupLine("[blue]Generating GitHub Actions workflow...[/]")
                        let workflowPath = ".github/workflows/livedocs.yml"
                        let work =
                            flow {
                                do! FileSystem.createDirectory ".github/workflows"
                                let! exists = FileSystem.fileExists workflowPath
                                if exists then invalidOp $"{workflowPath} already exists. Delete it to regenerate."
                                do! FileSystem.writeAllText workflowPath Templates.GitHubWorkflow
                            }
                        Run.orRaise FileSystemError.describe $"Could not write {workflowPath}" work
                        AnsiConsole.MarkupLine("[green]✔ Done:[/] .github/workflows/livedocs.yml")
                        0
                    | other -> invalidOp $"Unknown --provider '{other}'. Supported: github. Other hosts follow the generic recipe in docs/guides/continuous-integration.md."

                elif results.Contains Generate_Tests then
                    let projectPaths = results.GetResult Generate_Tests |> Actions.resolveProjects "generate-tests"
                    Actions.generateSnapshotTests projectPaths

                elif results.Contains Capture then
                    Actions.printBanner()
                    let projectPaths = results.GetResult Capture |> Actions.resolveProjects "capture"
                    let version = results.TryGetResult Arguments.Version
                    let output = results.TryGetResult Output
                    let exitCode =
                        Actions.captureAction (results.Contains Warn_As_Error) (results.Contains Dry_Run) projectPaths version output
                    Timing.write Timing.DefaultPath |> ignore
                    exitCode

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
                    Actions.historyAddAction
                        indexPath
                        version
                        (results.TryGetResult Capsule)
                        (results.TryGetResult Url)
                        (results.TryGetResult Sha256)
                        (results.TryGetResult Sha256_File)

                elif results.Contains History_Check then
                    let indexPath = results.GetResult(Output, defaultValue = ".livedocs/history.json")
                    let exitCode =
                        Actions.historyCheckAction
                            indexPath
                            (results.TryGetResult Capsule)
                            (results.TryGetResult Arguments.Version)
                            theme
                            (results.GetResult(Retry, defaultValue = 3))
                    Timing.write Timing.DefaultPath |> ignore
                    exitCode

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
                    Actions.printBanner()
                    let projectPaths = results.GetResult Extract |> Actions.resolveProjects "extract"
                    let mutable extractDiagnostics = []
                    AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                        let packageRaw, apiDiagnostics = Actions.getUnifiedPackage projectPaths |> Async.RunSynchronously
                        extractDiagnostics <- apiDiagnostics
                        let version = results.GetResult(Arguments.Version, defaultValue = packageRaw.Version)
                        let package = { packageRaw with Version = version }
                        let artifact : ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
                        let json = Json.serialize apiModelArtifactCodec artifact
                        let fileName = results.GetResult(Output, defaultValue = $".livedocs/models/{version}.json")
                        let outputDirectory = Path.GetDirectoryName(fileName)
                        let semanticArtifact, _ = Actions.createSemanticArtifact projectPaths package
                        let semanticJson = Json.serialize semanticArtifactCodec semanticArtifact
                        let semanticDirectory = Path.GetDirectoryName(fileName) |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not) |> Option.defaultValue "."
                        let outputStem = Path.GetFileNameWithoutExtension(fileName)
                        let semanticStem = if outputStem.EndsWith(".api", StringComparison.OrdinalIgnoreCase) then outputStem.Substring(0, outputStem.Length - 4) else outputStem
                        let semanticFileName = Path.Combine(semanticDirectory, semanticStem + ".semantic.json")
                        let writeWork =
                            flow {
                                if not (String.IsNullOrWhiteSpace outputDirectory) then
                                    do! FileSystem.createDirectory outputDirectory
                                do! FileSystem.writeAllText fileName json
                                do! FileSystem.writeAllText semanticFileName semanticJson
                            }
                        Run.orRaise FileSystemError.describe $"Could not write {fileName}" writeWork
                    )
                    AnsiConsole.MarkupLine("[green]✔ API and semantic documentation extraction complete.[/]")
                    Actions.printApiDiagnostics (results.Contains Warn_As_Error) extractDiagnostics

                elif results.Contains Test then
                    Actions.printBanner()
                    let projectPaths = results.GetResult Test |> Actions.resolveProjects "test"
                    let site = Workspace.loadSiteConfig ()
                    let warnAsError = results.Contains Warn_As_Error
                    // One shared verification implementation selects and executes the same cases
                    // that capture does, so a change to case policy cannot make test and capture
                    // disagree about what the release verifies.
                    let verification =
                        Verification.run
                            { ProjectPaths = projectPaths
                              Version = results.TryGetResult Arguments.Version
                              DocsSets = Actions.configuredDocsSets projectPaths
                              Prelude = site.FSharpPrelude |> Option.defaultValue ""
                              WarnAsError = warnAsError
                              ReportProgress = (fun _ _ _ -> ())
                              ReportAudit = (fun analysis -> Actions.printAudit true analysis |> ignore)
                              ReportApiDiagnostics = (fun treatAsError diagnostics -> Actions.printApiDiagnostics treatAsError diagnostics |> ignore)
                              ExecuteExamples = true }

                    for outcome in verification.Outcomes do
                        let label = if outcome.Passed then "[green]pass[/]" else "[red]fail[/]"
                        let suffix = if outcome.Cached then " [grey](cached)[/]" else ""
                        AnsiConsole.MarkupLine($"  {label} {Markup.Escape outcome.Id}{suffix}")
                        match outcome.Message with
                        | Some message when not outcome.Passed -> AnsiConsole.MarkupLine($"       [grey]{Markup.Escape message}[/]")
                        | _ -> ()

                    let compilerFailed = DocAnalysis.compilerFailureCount verification.Analysis <> 0
                    let apiFailed = warnAsError && not verification.ApiDiagnostics.IsEmpty
                    let examplesFailed = verification.Outcomes |> List.exists (fun outcome -> not outcome.Passed)
                    Timing.write Timing.DefaultPath |> ignore

                    if compilerFailed || apiFailed || examplesFailed then
                        AnsiConsole.MarkupLine("\n[bold red]✖ Some doc-tests failed.[/]")
                        1
                    else
                        AnsiConsole.MarkupLine("\n[bold green]✔ All doc-tests passed successfully![/]")
                        0

                elif results.Contains Audit then
                    Actions.printBanner()
                    let exitCode = Actions.auditAction (results.Contains Warn_As_Error) (results.GetResult Audit |> Actions.resolveProjects "audit")
                    Timing.write Timing.DefaultPath |> ignore
                    exitCode

                elif results.Contains Build then
                    Actions.printBanner()
                    let projectPaths = results.GetResult Build |> Actions.resolveProjects "build"
                    Actions.buildAction (results.Contains Warn_As_Error) (results.Contains Drafts) projectPaths theme (results.TryGetResult Arguments.Version)
                    Timing.write Timing.DefaultPath |> ignore
                    0

                elif results.Contains Build_History then
                    Actions.printBanner()
                    Actions.buildHistoryAction (results.GetResult Build_History) theme (results.GetResult(Retry, defaultValue = 3))
                    Timing.write Timing.DefaultPath |> ignore
                    0

                elif results.Contains Watch then
                    Actions.printBanner()
                    let projectPaths = results.GetResult Watch |> Actions.resolveProjects "watch"
                    let version = results.TryGetResult Arguments.Version
                    let host = results.GetResult(Host, defaultValue = "0.0.0.0")
                    let port = results.GetResult(Port, defaultValue = 5000)
                    if String.IsNullOrWhiteSpace host then invalidArg "host" "Preview host must not be empty."
                    if port < 1 || port > 65535 then invalidArg "port" "Preview port must be between 1 and 65535."
                    let previewUrl = $"http://{host}:{port}"
                    let buildPreview () =
                        // buildAction owns documentation verification and diagnostic reporting. Running
                        // auditAction first duplicates both in watch output without adding coverage.
                        Actions.buildAction (results.Contains Warn_As_Error) (results.Contains Drafts) projectPaths theme version
                    buildPreview ()
                    
                    try
                        let builder = WebApplication.CreateBuilder()
                        builder.Logging.ClearProviders() |> ignore
                        builder.Logging.AddConsole() |> ignore
                        builder.Logging.SetMinimumLevel(LogLevel.Warning) |> ignore
                        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning) |> ignore

                        let app = builder.Build()
                        
                        app.UseDefaultFiles() |> ignore
                        let currentDirectory = Run.orRaise FileSystemError.describe "Could not determine the current directory" FileSystem.getCurrentDirectory
                        let outputDir = Path.Combine(currentDirectory, "output")
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
                                currentDirectory
                                (PreviewWatcher.parseIgnored (results.GetResults Ignore))
                                (PackageExtraction.projectOutputPaths projectPaths)
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
