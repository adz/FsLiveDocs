namespace FsLiveDocs.Core

open System
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// Where a documentation block was authored before deterministic expansion.
type DocumentationBlockOrigin =
    | MarkdownFence
    | SourceSnippet
    | XmlExample
    | ApiEnrichment

/// The author-selected checking and execution policy for an F# block.
type DocumentationBlockMode =
    | Page
    | Prepare
    | Isolated
    | Run
    | Transcript
    | NoCheck of reason: string

/// One expanded F# block shared by verification, extraction, and rendering.
type DocumentationBlock = {
    Id: string
    Origin: DocumentationBlockOrigin
    SourcePath: string
    Ordinal: int
    ExpandedSource: string
    SourceHash: string
    Mode: DocumentationBlockMode
    Project: string option
}

/// A synthetic script checked in one evaluated project context.
type CompilationUnit = {
    Id: string
    ProjectPath: string
    Prelude: string
    Blocks: DocumentationBlock list
}

/// A deterministic action exposed to generated documentation tests.
type VerificationCase =
    | Compile of CompilationUnit
    | Execute of DocumentationBlock
    | ExecuteTranscript of DocumentationBlock

/// Canonical discovery of expanded documentation code.
module DocumentationDiscovery =

    let private fencePattern =
        Regex(@"(?ms)^```(?<info>fsharp(?:[ \t]+[^\r\n]*)?)[ \t]*\r?\n(?<code>.*?)^```[ \t]*\r?$", RegexOptions.Compiled)

    let normalizeSource (source: string) = source.Replace("\r\n", "\n").Replace("\r", "\n")

    let private sha256 (value: string) =
        value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

    let private modeKey = function
        | Page -> "page"
        | Prepare -> "prepare"
        | Isolated -> "isolated"
        | Run -> "run"
        | Transcript -> "transcript"
        | NoCheck reason -> "no-check:" + reason

    let sourceHash mode source = sha256 (modeKey mode + "\n" + normalizeSource source)

    /// Hashes every input that can change the meaning of a block without changing its displayed source.
    let contextHash prelude (blocks: DocumentationBlock list) =
        [ yield normalizeSource prelude
          for block in blocks do
              match block.Mode with
              | NoCheck _ | Transcript -> ()
              | _ -> yield block.Id + "\n" + block.SourceHash + "\n" + normalizeSource block.ExpandedSource ]
        |> String.concat "\n--livedocs-context--\n"
        |> sha256

    let private tokenizeOptions (info: string) =
        Regex.Matches(info, "(?:[^\\s\"]+|\"[^\"]*\")+")
        |> Seq.cast<Match>
        |> Seq.map _.Value
        |> Seq.toList

    /// Parses and validates a fenced-code info string. Contradictions fail before compiler work begins.
    let parseMode (info: string) =
        let tokens = tokenizeOptions info
        match tokens with
        | language :: options when language.Equals("fsharp", StringComparison.OrdinalIgnoreCase) ->
            let flags =
                options
                |> List.filter (fun value ->
                    not (value.StartsWith("reason=", StringComparison.OrdinalIgnoreCase))
                    && not (value.StartsWith("origin=", StringComparison.OrdinalIgnoreCase)))
            let known = set [ "prepare"; "isolated"; "run"; "transcript"; "no-check" ]
            let normalizedFlags = flags |> List.map _.ToLowerInvariant()
            match normalizedFlags |> List.tryFind (known.Contains >> not) with
            | Some unknown -> invalidOp $"Unknown F# fence mode '{unknown}'."
            | None -> ()
            if normalizedFlags.Length > 1 then
                let selected = String.concat " " normalizedFlags
                invalidOp $"Contradictory F# fence modes: {selected}. Choose exactly one mode."
            let reason =
                options
                |> List.tryPick (fun value ->
                    if value.StartsWith("reason=", StringComparison.OrdinalIgnoreCase) then
                        Some(value.Substring("reason=".Length).Trim().Trim('"'))
                    else None)
            match normalizedFlags with
            | [] when reason.IsSome -> invalidOp "A reason is only valid with the no-check mode."
            | [] -> Page
            | [ "prepare" ] -> Prepare
            | [ "isolated" ] -> Isolated
            | [ "run" ] -> Run
            | [ "transcript" ] -> Transcript
            | [ "no-check" ] ->
                match reason with
                | Some value when not (String.IsNullOrWhiteSpace value) -> NoCheck value
                | _ -> invalidOp "An F# no-check fence requires a non-empty reason=\"...\"."
            | _ -> invalidOp "Invalid F# fence options."
        | _ -> invalidArg "info" "Expected an fsharp fenced-code info string."

    /// Discovers blocks from Markdown after shortcode/API expansion has completed.
    let discoverMarkdown (sourcePath: string) (project: string option) (expandedMarkdown: string) =
        let normalizedPath = sourcePath.Replace('\\', '/').TrimStart('/')
        fencePattern.Matches(expandedMarkdown)
        |> Seq.cast<Match>
        |> Seq.mapi (fun ordinal matched ->
            let info = matched.Groups.["info"].Value
            let mode = parseMode info
            let origin =
                if info.Contains("origin=source-snippet", StringComparison.OrdinalIgnoreCase) then SourceSnippet
                elif info.Contains("origin=xml-example", StringComparison.OrdinalIgnoreCase) then XmlExample
                elif sourcePath.Replace('\\', '/').Contains("/api/", StringComparison.OrdinalIgnoreCase) || sourcePath.Replace('\\', '/').StartsWith("api/", StringComparison.OrdinalIgnoreCase) then ApiEnrichment
                else MarkdownFence
            let source = normalizeSource matched.Groups.["code"].Value
            {
                Id = $"{normalizedPath}#fsharp-{ordinal}"
                Origin = origin
                SourcePath = normalizedPath
                Ordinal = ordinal
                ExpandedSource = source
                SourceHash = sourceHash mode source
                Mode = mode
                Project = project
            })
        |> Seq.toList

    /// Ensures every discovered block has exactly the checking/execution coverage promised by its mode.
    let validateCoverage (blocks: DocumentationBlock list) =
        blocks
        |> List.countBy _.Id
        |> List.tryFind (fun (_, count) -> count <> 1)
        |> Option.iter (fun (id, _) -> invalidOp $"Duplicate documentation block id: {id}")
        blocks
        |> List.iter (fun block ->
            match block.Mode with
            | NoCheck reason when String.IsNullOrWhiteSpace reason -> invalidOp $"{block.Id} has no no-check reason."
            | _ -> ())

    /// Builds page-scoped and isolated compiler units without executing ordinary examples.
    let compilationUnits projectPath prelude (blocks: DocumentationBlock list) =
        validateCoverage blocks
        let compilable = blocks |> List.filter (fun b -> match b.Mode with NoCheck _ | Transcript -> false | _ -> true)
        let pageBlocks = compilable |> List.filter (fun b -> b.Mode <> Isolated)
        [ if not pageBlocks.IsEmpty then
              yield { Id = (List.head pageBlocks).SourcePath + "#page"; ProjectPath = projectPath; Prelude = prelude; Blocks = pageBlocks }
          for block in compilable do
              if block.Mode = Isolated then
                  yield { Id = block.Id; ProjectPath = projectPath; Prelude = prelude; Blocks = [ block ] } ]

    let verificationCases projectPath prelude blocks =
        let cases =
            [ for unit in compilationUnits projectPath prelude blocks -> Compile unit
              for block in blocks do
                  match block.Mode with
                  | Run -> yield Execute block
                  | Transcript -> yield ExecuteTranscript block
                  | _ -> () ]
        for block in blocks do
            let compileCount = cases |> List.sumBy (function Compile unit when unit.Blocks |> List.exists (fun item -> item.Id = block.Id) -> 1 | _ -> 0)
            let executeCount = cases |> List.sumBy (function Execute item | ExecuteTranscript item when item.Id = block.Id -> 1 | _ -> 0)
            match block.Mode with
            | Page | Prepare | Isolated when compileCount <> 1 || executeCount <> 0 -> invalidOp $"Coverage invariant failed for {block.Id}."
            | Run when compileCount <> 1 || executeCount <> 1 -> invalidOp $"Coverage invariant failed for {block.Id}."
            | Transcript when compileCount <> 0 || executeCount <> 1 -> invalidOp $"Coverage invariant failed for {block.Id}."
            | NoCheck _ when compileCount <> 0 || executeCount <> 0 -> invalidOp $"Coverage invariant failed for {block.Id}."
            | _ -> ()
        cases
