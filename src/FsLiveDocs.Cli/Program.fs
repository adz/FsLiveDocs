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

type Arguments =
    | [<CliPrefix(CliPrefix.None)>] Init
    | [<CliPrefix(CliPrefix.None)>] CI
    | [<CliPrefix(CliPrefix.None)>] Extract of projectPath:string
    | [<CliPrefix(CliPrefix.None)>] Test of projectPath:string
    | [<CliPrefix(CliPrefix.None)>] Build of projectPath:string
    | [<CliPrefix(CliPrefix.None)>] Watch of projectPath:string
    | [<CliPrefix(CliPrefix.None)>] Theme of string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Init -> "Scaffold a new LiveDocs project."
            | CI -> "Generate CI/CD templates (GitHub Actions)."
            | Extract _ -> "Extract symbols and docstrings into a JSON blob."
            | Test _ -> "Run all verified docstrings and snippets."
            | Build _ -> "Render the final static site."
            | Watch _ -> "Start a dev server with file watching."
            | Theme _ -> "Set the visual theme (default: light)."

module Program =

    let buildAction (projectPath: string) (theme: string) =
        AnsiConsole.Status().Start("Building site...", fun ctx ->
            let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            let pages = ContentProvider.scanDocs "docs" (Path.GetDirectoryName(projectPath)) package
            if not (Directory.Exists(".livedocs/history")) then Directory.CreateDirectory(".livedocs/history") |> ignore
            SiteBuilder.buildAll ".livedocs/history" package pages theme "output"
            
            try
                let psi = System.Diagnostics.ProcessStartInfo("npx", "-y pagefind --site output")
                psi.RedirectStandardOutput <- true
                psi.UseShellExecute <- false
                let proc = System.Diagnostics.Process.Start(psi)
                proc.WaitForExit()
            with _ -> AnsiConsole.MarkupLine("[yellow]Warning: pagefind indexing failed.[/]")
        )
        AnsiConsole.MarkupLine("[green]Build complete: output/[/]")

    [<EntryPoint>]
    let main args =
        let parser = ArgumentParser.Create<Arguments>(programName = "livedocs")
        try
            let results = parser.Parse(args)
            let theme = results.GetResult(Theme, defaultValue = "light")
            
            if results.Contains Init then
                AnsiConsole.MarkupLine("[green]Initializing LiveDocs...[/]")
                if not (Directory.Exists(".livedocs/history")) then Directory.CreateDirectory(".livedocs/history") |> ignore
                if not (Directory.Exists("docs")) then Directory.CreateDirectory("docs") |> ignore
                if not (File.Exists("docs/index.md")) then
                    File.WriteAllText("docs/index.md", "---\ntitle: Home\nweight: 1\n---\n# Welcome to FsLiveDocs\n\nEdit this file to get started.")
                AnsiConsole.MarkupLine("[green]Done![/]")

            if results.Contains CI then
                AnsiConsole.MarkupLine("[green]Generating GitHub Actions workflow...[/]")
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
          ./artifacts/livedocs build YourProject.fsproj
      - name: Deploy to GitHub Pages
        uses: peaceiris/actions-gh-pages@v3
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: ./output
"""
                File.WriteAllText(".github/workflows/livedocs.yml", workflow)
                AnsiConsole.MarkupLine("[green]Done: .github/workflows/livedocs.yml[/]")

            if results.Contains Extract then
                let projectPath = results.GetResult Extract
                AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    let json = Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented)
                    if not (Directory.Exists(".livedocs/history")) then Directory.CreateDirectory(".livedocs/history") |> ignore
                    let fileName = $".livedocs/history/{package.Version}.json"
                    File.WriteAllText(fileName, json)
                )
                AnsiConsole.MarkupLine("[green]Extraction complete: .livedocs/history/[/]")

            elif results.Contains Test then
                let projectPath = results.GetResult Test
                AnsiConsole.Status().Start("Running tests...", fun ctx ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    let results = DocTestRunner.verifyExamples package projectPath [] |> Async.RunSynchronously
                    for (name, success, output) in results do
                        if success then AnsiConsole.MarkupLine($"[green]PASS:[/] {Markup.Escape(name)}")
                        else AnsiConsole.MarkupLine($"[red]FAIL:[/] {Markup.Escape(name)} - {Markup.Escape(output)}")
                )

            elif results.Contains Build then
                let projectPath = results.GetResult Build
                buildAction projectPath theme

            elif results.Contains Watch then
                let projectPath = results.GetResult Watch
                buildAction projectPath theme
                
                let watcher = new FileSystemWatcher(Path.GetDirectoryName(projectPath))
                watcher.IncludeSubdirectories <- true
                watcher.EnableRaisingEvents <- true
                watcher.Changed.Add(fun _ -> 
                    AnsiConsole.MarkupLine("[yellow]Change detected, rebuilding...[/]")
                    try buildAction projectPath theme with e -> AnsiConsole.WriteException(e)
                )

                let builder = WebApplication.CreateBuilder()
                let app = builder.Build()
                app.UseDefaultFiles() |> ignore
                app.UseStaticFiles(StaticFileOptions(
                    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "output")),
                    RequestPath = ""
                )) |> ignore
                AnsiConsole.MarkupLine("[blue]Starting dev server at http://localhost:5000[/]")
                app.Run("http://localhost:5000")

            0
        with e ->
            AnsiConsole.WriteException(e)
            1
