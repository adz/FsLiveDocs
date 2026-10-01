namespace FsLiveDocs.Cli

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Spectre.Console

/// <summary>
/// Process-wide timing recorder for verification phases and individual examples.
/// </summary>
/// <remarks>
/// Disabled by default so ordinary runs pay nothing. When enabled with <c>--timings</c>, every
/// verification command writes a machine-readable <c>.livedocs/timings.json</c> and prints a
/// summary, so a duplicate pass or a regression in worker startup is visible rather than guessed.
/// </remarks>
module internal Timing =

    type Phase = { Name: string; DurationMs: float; Detail: string option }

    type Case =
        { Id: string
          Project: string
          Mode: string
          Kind: string
          DurationMs: float
          Outcome: string
          Detail: string option }

    [<Literal>]
    let SchemaVersion = 1

    [<Literal>]
    let DefaultPath = ".livedocs/timings.json"

    let private gate = obj ()
    let private phases = ResizeArray<Phase>()
    let private cases = ResizeArray<Case>()
    let mutable private enabled = false
    let mutable private command = ""
    let mutable private startedAt = DateTimeOffset.UtcNow
    let private total = Stopwatch()

    let configure (value: bool) =
        enabled <- value
        if value then
            startedAt <- DateTimeOffset.UtcNow
            total.Restart()

    let isEnabled () = enabled

    /// Names the command the report belongs to; safe to call even when timing is disabled.
    let label (value: string) = command <- value

    let reset () =
        lock gate (fun () ->
            phases.Clear()
            cases.Clear())

    type internal ActivePhase = { Name: string; Detail: string option; Stopwatch: Stopwatch }

    /// Runs <paramref name="body" /> and records one phase. The phase is recorded even when the body throws.
    let measure (name: string) (detail: string option) (body: unit -> 'a) =
        if not enabled then
            body ()
        else
            let stopwatch = Stopwatch.StartNew()
            try
                body ()
            finally
                lock gate (fun () ->
                    phases.Add
                        { Name = name
                          DurationMs = stopwatch.Elapsed.TotalMilliseconds
                          Detail = detail })

    /// Opens a phase whose body spans a large block. Pair with <see cref="endPhase" />.
    let beginPhase (name: string) (detail: string option) =
        if enabled then
            Some { Name = name; Detail = detail; Stopwatch = Stopwatch.StartNew() }
        else
            None

    let endPhase (active: ActivePhase option) =
        active
        |> Option.iter (fun phase ->
            lock gate (fun () ->
                phases.Add
                    { Name = phase.Name
                      DurationMs = phase.Stopwatch.Elapsed.TotalMilliseconds
                      Detail = phase.Detail }))

    /// Records one example execution. Cheap no-op when timing is disabled.
    let case (id: string) (project: string) (mode: string) (kind: string) (durationMs: float) (outcome: string) (detail: string option) =
        if enabled then
            lock gate (fun () ->
                cases.Add
                    { Id = id
                      Project = project
                      Mode = mode
                      Kind = kind
                      DurationMs = durationMs
                      Outcome = outcome
                      Detail = detail })

    let private writeJson (path: string) (phaseSnapshot: Phase[]) (caseSnapshot: Case[]) =
        try
            let phases = JsonArray()
            for phase in phaseSnapshot do
                let node = JsonObject()
                node["Name"] <- JsonValue.Create phase.Name
                node["DurationMs"] <- JsonValue.Create phase.DurationMs
                phase.Detail |> Option.iter (fun value -> node["Detail"] <- JsonValue.Create value)
                phases.Add node

            let cases = JsonArray()
            for item in caseSnapshot do
                let node = JsonObject()
                node["Id"] <- JsonValue.Create item.Id
                node["Project"] <- JsonValue.Create item.Project
                node["Mode"] <- JsonValue.Create item.Mode
                node["Kind"] <- JsonValue.Create item.Kind
                node["DurationMs"] <- JsonValue.Create item.DurationMs
                node["Outcome"] <- JsonValue.Create item.Outcome
                item.Detail |> Option.iter (fun value -> node["Detail"] <- JsonValue.Create value)
                cases.Add node

            let report = JsonObject()
            report["SchemaVersion"] <- JsonValue.Create SchemaVersion
            report["Tool"] <- JsonValue.Create(Reflection.Assembly.GetExecutingAssembly().GetName().Version |> string)
            report["Command"] <- JsonValue.Create command
            report["StartedAtUtc"] <- JsonValue.Create(startedAt.ToString("o"))
            report["TotalMs"] <- JsonValue.Create total.Elapsed.TotalMilliseconds
            report["Phases"] <- phases
            report["Cases"] <- cases
            File.WriteAllText(path, report.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        with _ ->
            ()

    let private phaseLabel (phase: Phase) =
        let detail = phase.Detail |> Option.map (fun value -> $" ({Markup.Escape value})") |> Option.defaultValue ""
        $"{Markup.Escape phase.Name}{detail}"

    /// Writes the report and prints a compact summary. Returns the report path, or None when writing failed.
    let write (path: string) =
        if not enabled then
            None
        else
            let snapshot =
                lock gate (fun () -> phases |> Seq.toArray, cases |> Seq.toArray)

            let phaseSnapshot, caseSnapshot = snapshot
            total.Stop()

            let directory = Path.GetDirectoryName(Path.GetFullPath path)
            if not (String.IsNullOrWhiteSpace directory) then
                Directory.CreateDirectory directory |> ignore

            writeJson path phaseSnapshot caseSnapshot

            let phaseTotal = phaseSnapshot |> Array.sumBy _.DurationMs
            AnsiConsole.MarkupLine("")
            AnsiConsole.MarkupLine("[bold]Timings[/]")
            AnsiConsole.MarkupLine($"  Total: [blue]{total.Elapsed.TotalSeconds:N1}s[/] ({phaseSnapshot.Length} phases, {caseSnapshot.Length} cases)")
            for phase in phaseSnapshot |> Array.sortByDescending _.DurationMs do
                AnsiConsole.MarkupLine($"  [grey]{phase.DurationMs / 1000.0,8:N1}s[/] {phaseLabel phase}")

            if caseSnapshot.Length > 0 then
                let slowest = caseSnapshot |> Array.sortByDescending _.DurationMs |> Array.truncate 10

                AnsiConsole.MarkupLine("  Slowest cases:")
                for item in slowest do
                    let detail = item.Detail |> Option.map (fun value -> $" [grey]{Markup.Escape value}[/]") |> Option.defaultValue ""
                    AnsiConsole.MarkupLine($"    [grey]{item.DurationMs / 1000.0,7:N2}s[/] {Markup.Escape item.Id} [grey]({Markup.Escape item.Kind}, {Markup.Escape item.Outcome})[/]{detail}")

            AnsiConsole.MarkupLine($"  Report: [grey]{Markup.Escape(Path.GetFullPath path)}[/] [grey](phase sum {phaseTotal / 1000.0:N1}s)[/]")
            Some(Path.GetFullPath path)
