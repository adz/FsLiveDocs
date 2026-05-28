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
    /// <summary>Extracts symbol metadata from projects into JSON snapshots.</summary>
    | [<CliPrefix(CliPrefix.None)>] Extract of projectPaths:string list
    /// <summary>Runs verified code examples found in docstrings.</summary>
    | [<CliPrefix(CliPrefix.None)>] Test of projectPaths:string list
    /// <summary>Builds the full static documentation site.</summary>
    | [<CliPrefix(CliPrefix.None)>] Build of projectPaths:string list
    /// <summary>Starts a development server with live-rebuild capabilities.</summary>
    | [<CliPrefix(CliPrefix.None)>] Watch of projectPaths:string list
    /// <summary>Sets the DaisyUI visual theme.</summary>
    | [<Inherit; AltCommandLine("-t")>] Theme of string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Init -> "Scaffold a new LiveDocs project."
            | CI -> "Generate CI/CD templates (GitHub Actions)."
            | Extract _ -> "Extract symbols from one or more projects into a JSON blob."
            | Test _ -> "Run all verified docstrings and snippets for the given projects."
            | Build _ -> "Render the final static site for the given projects."
            | Watch _ -> "Start a dev server with file watching."
            | Theme _ -> "Set the visual theme (default: light)."

/// <summary>The main entry point module for the CLI application.</summary>
module Program =

    let printBanner () =
        AnsiConsole.Write(
            new FigletText("LiveDocs")
                .LeftAligned()
                .Color(Color.Blue))
        AnsiConsole.MarkupLine("[grey]Verified Documentation for F#[/]\n")

    /// <summary>Loads and merges multiple project models into a unified package.</summary>
    let getUnifiedPackage (projectPaths: string list) = async {
        let packages = ResizeArray()
        for projectPath in projectPaths do
            let! package = SymbolLister.extractFromProject projectPath
            packages.Add(package)
        return SymbolLister.merge (Seq.toList packages)
    }

    /// <summary>Orchestrates the build process for one or more projects.</summary>
    let buildAction (projectPaths: string list) (theme: string) =
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[blue]Building documentation site...[/]", fun ctx ->
                let packageRaw = getUnifiedPackage projectPaths |> Async.RunSynchronously
                let sourceDir = Directory.GetCurrentDirectory() 
                let package = ContentProvider.applyApiDocs "docs" sourceDir packageRaw
                let pages = ContentProvider.scanDocs "docs" sourceDir package ""
                
                let historyDir = ".livedocs/history"
                if not (Directory.Exists(historyDir)) then Directory.CreateDirectory(historyDir) |> ignore
                
                SiteBuilder.buildAll historyDir package pages theme "output"
                
                let psi = System.Diagnostics.ProcessStartInfo("npx", "-y pagefind --site output")
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                let proc = System.Diagnostics.Process.Start(psi)
                proc.WaitForExit()
            )
        AnsiConsole.MarkupLine("[green]✔ Build complete:[/] output/")

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
                    if not (Directory.Exists("docs")) then Directory.CreateDirectory("docs") |> ignore
                    if not (File.Exists("docs/index.md")) then
                        File.WriteAllText("docs/index.md", "---\ntitle: Home\nweight: 1\n---\n# Welcome to FsLiveDocs\n\nEdit this file to get started.")
                    AnsiConsole.MarkupLine("[green]✔ Done![/]")
                    0

                elif results.Contains CI then
                    printBanner()
                    AnsiConsole.MarkupLine("[blue]Generating GitHub Actions workflow...[/]")
                    if not (Directory.Exists(".github/workflows")) then Directory.CreateDirectory(".github/workflows") |> ignore
                    let workflow = """
name: LiveDocs
on:
  push:
    branches: [ main ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: jdx/mise-action@v2
      - name: Build Docs
        run: |
          dotnet build
          ./scripts/publish.sh
          ./artifacts/livedocs build src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj
      - name: Deploy to GitHub Pages
        uses: peaceiris/actions-gh-pages@v3
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: ./output
"""
                    File.WriteAllText(".github/workflows/livedocs.yml", workflow)
                    AnsiConsole.MarkupLine("[green]✔ Done:[/] .github/workflows/livedocs.yml")
                    0

                elif results.Contains Extract then
                    printBanner()
                    let projectPaths = results.GetResult Extract
                    AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                        let package = getUnifiedPackage projectPaths |> Async.RunSynchronously
                        let json = Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented)
                        if not (Directory.Exists(".livedocs/history")) then Directory.CreateDirectory(".livedocs/history") |> ignore
                        let fileName = $".livedocs/history/{package.Version}.json"
                        File.WriteAllText(fileName, json)
                    )
                    AnsiConsole.MarkupLine("[green]✔ Extraction complete:[/] .livedocs/history/")
                    0

                elif results.Contains Test then
                    printBanner()
                    let projectPaths = results.GetResult Test
                    let mutable allPassed = true
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

                elif results.Contains Build then
                    printBanner()
                    let projectPaths = results.GetResult Build
                    buildAction projectPaths theme
                    0

                elif results.Contains Watch then
                    printBanner()
                    let projectPaths = results.GetResult Watch
                    buildAction projectPaths theme
                    
                    try
                        let watcher = new FileSystemWatcher(Directory.GetCurrentDirectory())
                        watcher.IncludeSubdirectories <- true
                        watcher.EnableRaisingEvents <- true
                        watcher.Changed.Add(fun e -> 
                            if e.Name.EndsWith(".fs") || e.Name.EndsWith(".fsproj") || e.Name.EndsWith(".md") || e.Name.EndsWith(".css") then
                                if not (e.Name.Contains("bin") || e.Name.Contains("obj") || e.Name.Contains("output")) then
                                    AnsiConsole.MarkupLine($"[yellow]⚡ Change detected in {e.Name}, rebuilding...[/]")
                                    try buildAction projectPaths theme with e -> AnsiConsole.WriteException(e)
                        )

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
                            RequestPath = ""
                        )) |> ignore
                        
                        app.Use(fun (context: HttpContext) (next: Func<Threading.Tasks.Task>) ->
                            if context.Request.Path.Value = "/" then
                                context.Response.Redirect("/index.html")
                                Threading.Tasks.Task.CompletedTask
                            else
                                next.Invoke()
                        ) |> ignore

                        AnsiConsole.MarkupLine("[bold blue]🚀 Preview server is live![/]")
                        AnsiConsole.MarkupLine("   [grey]URL:[/] http://localhost:5000")
                        AnsiConsole.MarkupLine("   [grey]Watching for changes...[/]\n")
                        
                        app.Run("http://localhost:5000")
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
