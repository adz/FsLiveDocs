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
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Init -> "Scaffold a new LiveDocs project."
            | CI -> "Generate CI/CD templates (GitHub Actions)."
            | GenerateTests _ -> "Generate a Verify-based snapshot test project for the given projects."
            | Extract _ -> "Extract symbols from one or more projects into a JSON blob."
            | Test _ -> "Run the legacy direct docstring verifier for the given projects."
            | Build _ -> "Render the final static site for the given projects."
            | BuildHistory _ -> "Render all versions from a verified local history manifest."
            | Watch _ -> "Start a dev server with file watching."
            | Theme _ -> "Set the visual theme (default: light)."
            | Version _ -> "Set the version stored by API model extraction."
            | Output _ -> "Set the API model extraction output path."
            | Host _ -> "Set the preview bind host (default: 0.0.0.0)."
            | Port _ -> "Set the preview port (default: 5000)."

/// <summary>The main entry point module for the CLI application.</summary>
module Program =

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None }

    let printBanner () =
        let figlet = FigletText("LiveDocs")
        figlet.Color <- Color.Blue
        AnsiConsole.Write(figlet)
        AnsiConsole.MarkupLine("[grey]Verified Documentation for F#[/]\n")

    /// <summary>Loads and merges multiple project models into a unified package.</summary>
    let getUnifiedPackage (projectPaths: string list) = async {
        let packages = ResizeArray()
        for projectPath in projectPaths do
            let! package = SymbolLister.extractFromProject projectPath
            packages.Add(package)
        return SymbolLister.merge (Seq.toList packages)
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
                    "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />"
                    "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\" />"
                    "    <PackageReference Include=\"Verify.Xunit\" Version=\"31.12.5\" />"
                    "  </ItemGroup>"
                    ""
                    "  <ItemGroup>"
                    projectRefs
                    "  </ItemGroup>"
                    ""
                    "</Project>"
                ]
                |> String.concat eol

            let testBodies =
                List.zip resolvedProjects relativeProjects
                |> List.map (fun (projectPath, relativeProjectPath) ->
                    let projectName = Path.GetFileNameWithoutExtension(projectPath)
                    let escapedProjectPath = relativeProjectPath.Replace("\"", "\"\"")
                    [
                        "    [<Fact>]"
                        $"    let ``{projectName} snapshot examples`` () ="
                        "        task {"
                        $"            let projectPath = @\"{escapedProjectPath}\""
                        "            let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously"
                        "            let! snapshot = DocTestRunner.collectSnapshots package projectPath []"
                        "            return! Verifier.Verify(snapshot)"
                        "        }"
                    ]
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
                ]
                |> String.concat eol

            let fsprojPath = Path.Combine(outputDir, "FsLiveDocs.SnapshotTests.fsproj")
            let testsPath = Path.Combine(outputDir, "SnapshotTests.fs")

            writeIfChanged fsprojPath fsproj
            writeIfChanged testsPath testsFs

            AnsiConsole.MarkupLine($"[green]✔ Snapshot test project generated:[/] {outputDir}")
            0

    /// <summary>Orchestrates the build process for one or more projects.</summary>
    let buildAction (projectPaths: string list) (theme: string) (version: string option) =
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[blue]Building documentation site...[/]", fun ctx ->
                let extracted = getUnifiedPackage projectPaths |> Async.RunSynchronously
                let packageRaw = { extracted with Version = version |> Option.defaultValue extracted.Version }
                let sourceDir = Directory.GetCurrentDirectory() 
                let package = ContentProvider.applyApiDocs "docs" sourceDir packageRaw
                let pages = ContentProvider.scanDocs "docs" sourceDir package ""
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
                let package = ContentProvider.applyApiDocs docsDir sourceDir packageRaw
                let rootPath = if entry.Version = manifest.CurrentVersion then "" else "../../"
                let pages = ContentProvider.scanDocs docsDir sourceDir package rootPath
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

                elif results.Contains GenerateTests then
                    let projectPaths = results.GetResult GenerateTests
                    generateSnapshotTests projectPaths

                elif results.Contains Extract then
                    printBanner()
                    let projectPaths = results.GetResult Extract
                    AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                        let packageRaw = getUnifiedPackage projectPaths |> Async.RunSynchronously
                        let version = results.GetResult(Version, defaultValue = packageRaw.Version)
                        let package = { packageRaw with Version = version }
                        let artifact : ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
                        let json = Newtonsoft.Json.JsonConvert.SerializeObject(artifact, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings)
                        let fileName = results.GetResult(Output, defaultValue = $".livedocs/models/{version}.json")
                        let outputDirectory = Path.GetDirectoryName(fileName)
                        if not (String.IsNullOrWhiteSpace outputDirectory) && not (Directory.Exists(outputDirectory)) then
                            Directory.CreateDirectory(outputDirectory) |> ignore
                        File.WriteAllText(fileName, json)
                    )
                    AnsiConsole.MarkupLine("[green]✔ API model extraction complete.[/]")
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
                    buildAction projectPaths theme (results.TryGetResult Version)
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
                    buildAction projectPaths theme version
                    
                    try
                        let watcher = new FileSystemWatcher(Directory.GetCurrentDirectory())
                        watcher.IncludeSubdirectories <- true
                        watcher.EnableRaisingEvents <- true
                        watcher.Changed.Add(fun e -> 
                            if e.Name.EndsWith(".fs") || e.Name.EndsWith(".fsproj") || e.Name.EndsWith(".md") || e.Name.EndsWith(".css") then
                                if not (e.Name.Contains("bin") || e.Name.Contains("obj") || e.Name.Contains("output")) then
                                    AnsiConsole.MarkupLine($"[yellow]⚡ Change detected in {e.Name}, rebuilding...[/]")
                                    try buildAction projectPaths theme version with e -> AnsiConsole.WriteException(e)
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
                        AnsiConsole.MarkupLine($"   [grey]Listening:[/] {Markup.Escape(previewUrl)}")
                        if host = "0.0.0.0" then
                            AnsiConsole.MarkupLine($"   [grey]Browse locally:[/] http://localhost:{port}")
                        AnsiConsole.MarkupLine("   [grey]Watching for changes...[/]\n")
                        
                        app.Run(previewUrl)
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
