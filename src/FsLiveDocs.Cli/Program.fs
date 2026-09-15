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
                    let discovered = Workspace.initialize (results.Contains Discover_Projects)
                    match discovered with
                    | Some (count, configPath) ->
                        AnsiConsole.MarkupLine($"[green]✔ Recorded {count} project(s):[/] {Markup.Escape configPath}")
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
                    Actions.captureAction (results.Contains Warn_As_Error) (results.Contains Dry_Run) projectPaths version output

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
                    Actions.historyCheckAction
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
                    let mutable allPassed = Actions.auditAction (results.Contains Warn_As_Error) projectPaths = 0
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
                    let package, _ = Actions.getUnifiedPackage projectPaths |> Async.RunSynchronously

                    for page in Actions.documentationPages projectPaths package do
                        let externallyExecuted =
                            page.Blocks
                            |> List.choose (fun block ->
                                match block.Mode, block.Origin with
                                | (Run | Transcript), XmlExample -> Some block.Id
                                | _ -> None)
                            |> Set.ofList
                        let cases =
                            DocumentationDiscovery.generatedCases
                                page.SelectedProject
                                page.Prelude
                                page.Relative
                                page.Expanded
                                externallyExecuted

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
                    Actions.printBanner()
                    Actions.auditAction (results.Contains Warn_As_Error) (results.GetResult Audit |> Actions.resolveProjects "audit")

                elif results.Contains Build then
                    Actions.printBanner()
                    let projectPaths = results.GetResult Build |> Actions.resolveProjects "build"
                    Actions.buildAction (results.Contains Warn_As_Error) (results.Contains Drafts) projectPaths theme (results.TryGetResult Arguments.Version)
                    0

                elif results.Contains Build_History then
                    Actions.printBanner()
                    Actions.buildHistoryAction (results.GetResult Build_History) theme (results.GetResult(Retry, defaultValue = 3))
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
