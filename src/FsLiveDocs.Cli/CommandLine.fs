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
