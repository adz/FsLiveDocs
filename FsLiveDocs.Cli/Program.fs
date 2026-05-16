namespace FsLiveDocs.Cli

open System.IO
open Argu
open Spectre.Console
open FsLiveDocs.Core
open FsLiveDocs.Runner
open FsLiveDocs.Renderer

type Arguments =
    | [<CliPrefix(CliPrefix.None)>] Init
    | [<CliPrefix(CliPrefix.None)>] Extract of projectPath:string
    | [<CliPrefix(CliPrefix.None)>] Test of projectPath:string
    | [<CliPrefix(CliPrefix.None)>] Build of projectPath:string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Init -> "Scaffold a new LiveDocs project."
            | Extract _ -> "Extract symbols and docstrings into a JSON blob."
            | Test _ -> "Run all verified docstrings and snippets."
            | Build _ -> "Render the final static site."

module Program =

    [<EntryPoint>]
    let main args =
        let parser = ArgumentParser.Create<Arguments>(programName = "livedocs")
        try
            let results = parser.Parse(args)
            
            if results.Contains Init then
                AnsiConsole.MarkupLine("[green]Initializing LiveDocs...[/]")
                if not (Directory.Exists(".livedocs")) then Directory.CreateDirectory(".livedocs") |> ignore
                if not (Directory.Exists("docs")) then Directory.CreateDirectory("docs") |> ignore
                File.WriteAllText("docs/index.md", "---\ntitle: Home\nweight: 1\n---\n# Welcome to FsLiveDocs\n\nEdit this file to get started.")
                AnsiConsole.MarkupLine("[green]Done![/]")

            elif results.Contains Extract then
                let projectPath = results.GetResult Extract
                AnsiConsole.Status().Start("Extracting symbols...", fun ctx ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    let json = Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented)
                    File.WriteAllText(".livedocs/snapshot.json", json)
                )
                AnsiConsole.MarkupLine("[green]Extraction complete: .livedocs/snapshot.json[/]")

            elif results.Contains Test then
                let projectPath = results.GetResult Test
                AnsiConsole.Status().Start("Running tests...", fun ctx ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    // In a real scenario, we'd need to find the DLL references
                    let results = DocTestRunner.verifyExamples package [] |> Async.RunSynchronously
                    for (name, success, output) in results do
                        if success then AnsiConsole.MarkupLine($"[green]PASS:[/] {name}")
                        else AnsiConsole.MarkupLine($"[red]FAIL:[/] {name} - {output}")
                )

            elif results.Contains Build then
                let projectPath = results.GetResult Build
                AnsiConsole.Status().Start("Building site...", fun ctx ->
                    let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
                    let pages = ContentProvider.scanDocs "docs" (Path.GetDirectoryName(projectPath))
                    SiteBuilder.build package pages "output"
                )
                AnsiConsole.MarkupLine("[green]Build complete: output/[/]")

            0
        with e ->
            AnsiConsole.WriteException(e)
            1
