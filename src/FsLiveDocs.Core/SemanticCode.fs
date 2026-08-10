namespace FsLiveDocs.Core

open System
open System.Net
open System.Text.RegularExpressions

/// Build-time semantic formatting for F# code embedded in documentation pages.
module SemanticCode =

    let htmlStartMarker = "<!--fslivedocs-semantic-start-->"
    let htmlEndMarker = "<!--fslivedocs-semantic-end-->"

    type Options = {
        Enabled: bool
        Artifact: SemanticDocumentationArtifact option
        Prelude: string
    }

    let defaults = { Enabled = true; Artifact = None; Prelude = "" }
    let disabled = { defaults with Enabled = false }

    let private fencePattern =
        Regex(
            @"(?ms)^```(?<info>fsharp(?:[ \t]+[^\r\n]*)?)[ \t]*\r?\n(?<code>.*?)^```[ \t]*$",
            RegexOptions.Compiled)

    let private tokenClass = function
        | PlainText -> "tok-plain" | Keyword -> "tok-keyword" | Identifier -> "tok-identifier"
        | TypeName -> "tok-type" | Function -> "tok-function" | Property -> "tok-property"
        | UnionCase -> "tok-union-case" | ActivePatternCase -> "tok-active-pattern"
        | Module -> "tok-module" | Namespace -> "tok-namespace" | Operator -> "tok-operator"
        | Number -> "tok-number" | String -> "tok-string" | Comment -> "tok-comment"
        | Punctuation -> "tok-punctuation" | Preprocessor -> "tok-preprocessor"

    let private safeId (value: string) = Regex.Replace(value, @"[^A-Za-z0-9_-]", "-")

    let private lexicalTokenPattern =
        Regex("""//.*$|@?"(?:""|\\.|[^"])*"|\d+(?:\.\d+)?|[A-Za-z_'\p{L}][\w'\p{L}]*|[!%&*+\-./<=>?@^|~:]+|\s+|.""", RegexOptions.Compiled)

    let private keywords =
        set [ "abstract"; "and"; "as"; "assert"; "base"; "begin"; "class"; "default"; "delegate"; "do"; "done"; "downcast"; "downto"; "elif"; "else"; "end"; "exception"; "extern"; "false"; "finally"; "fixed"; "for"; "fun"; "function"; "global"; "if"; "in"; "inherit"; "inline"; "interface"; "internal"; "lazy"; "let"; "match"; "member"; "module"; "mutable"; "namespace"; "new"; "null"; "of"; "open"; "or"; "override"; "private"; "public"; "rec"; "return"; "return!"; "select"; "static"; "struct"; "then"; "to"; "true"; "try"; "type"; "upcast"; "use"; "use!"; "val"; "void"; "when"; "while"; "with"; "yield"; "yield!" ]

    let private lexicalClass (text: string) =
        if String.IsNullOrWhiteSpace text then "tok-plain"
        elif text.StartsWith("//") then "tok-comment"
        elif text.StartsWith("\"") || text.StartsWith("@\"") then "tok-string"
        elif Char.IsDigit text.[0] then "tok-number"
        elif keywords.Contains text then "tok-keyword"
        elif Regex.IsMatch(text, @"^[!%&*+\-./<=>?@^|~:]+$") then "tok-operator"
        elif Char.IsUpper text.[0] then "tok-type"
        elif Regex.IsMatch(text, @"^[A-Za-z_'\p{L}][\w'\p{L}]*$") then "tok-identifier"
        else "tok-punctuation"

    let private renderLexicalLine (line: string) =
        let prompt = Regex.Match(line, @"^(?<indent>\s*)(?<prompt>iex\(\d+\)>|iex>|fsi>|\.{3}>|>)(?<space>\s?)")
        let prefix, source =
            if prompt.Success then
                let promptText = prompt.Groups.["indent"].Value + prompt.Groups.["prompt"].Value + prompt.Groups.["space"].Value
                $"<span class=\"prompt-unselectable\">{WebUtility.HtmlEncode promptText}</span>", line.Substring(prompt.Length)
            else "", line
        let tokens =
            lexicalTokenPattern.Matches(source)
            |> Seq.cast<Match>
            |> Seq.map (fun matched -> $"<span class=\"{lexicalClass matched.Value}\">{WebUtility.HtmlEncode matched.Value}</span>")
            |> String.concat ""
        prefix + tokens

    let private codeFrame extraClass lines tooltips =
        $"<div class=\"livedocs-code {extraClass} not-prose\"><pre class=\"code-frame\"><code class=\"language-fsharp\">{lines}</code></pre>{tooltips}</div>"

    let private renderLexicalSource source =
        let lines =
            DocumentationDiscovery.normalizeSource(source).TrimEnd('\n').Split('\n')
            |> Array.map renderLexicalLine
            |> String.concat "\n"
        codeFrame "livedocs-lexical-code" lines ""

    let private renderPersistedBlock (block: SemanticCodeBlock) =
        let tooltipId index = $"livedocs-tip-{safeId block.Id}-{index}"
        let lines =
            block.Lines
            |> List.map (fun line ->
                line.Tokens
                |> List.map (fun token ->
                    let encoded = WebUtility.HtmlEncode token.Text
                    match token.Tooltip with
                    | Some index ->
                        let id = tooltipId index
                        $"<span class=\"{tokenClass token.Kind}\" tabindex=\"0\" data-fsdocs-tip=\"{id}\" aria-describedby=\"{id}\">{encoded}</span>"
                    | None -> $"<span class=\"{tokenClass token.Kind}\">{encoded}</span>")
                |> String.concat "")
            |> String.concat "\n"
        let tooltips =
            block.Tooltips
            |> List.mapi (fun index tooltip ->
                let signature = tooltip.Signature |> Option.map (WebUtility.HtmlEncode >> fun value -> $"<code>{value}</code>") |> Option.defaultValue ""
                let documentation = tooltip.Documentation |> Option.map (WebUtility.HtmlEncode >> fun value -> $"<p>{value}</p>") |> Option.defaultValue ""
                let sections =
                    tooltip.Sections
                    |> List.map (fun section ->
                        let heading = section.Heading |> Option.map (WebUtility.HtmlEncode >> fun value -> $"<strong>{value}</strong>") |> Option.defaultValue ""
                        $"<div>{heading}<p>{WebUtility.HtmlEncode section.Content}</p></div>")
                    |> String.concat ""
                $"<div class=\"livedocs-semantic-tooltip fsdocs-tip\" id=\"{tooltipId index}\" role=\"tooltip\" popover>{signature}{documentation}{sections}</div>")
            |> String.concat ""
        codeFrame "livedocs-semantic-code" lines $"<div class=\"livedocs-tooltips\">{tooltips}</div>"

    let private renderPreparation (block: SemanticCodeBlock) =
        $"<details class=\"livedocs-shared-setup not-prose\"><summary>Shared setup</summary>{renderPersistedBlock block}</details>"

    let private renderPrelude (prelude: string) =
        $"<details class=\"livedocs-shared-setup livedocs-repository-setup not-prose\"><summary>Repository F# setup</summary>{renderLexicalSource prelude}</details>"

    let private formatFromArtifact options sourcePath markdown artifact =
        let blocks = DocumentationDiscovery.discoverMarkdown sourcePath None markdown
        let page = artifact.Pages |> List.tryFind (fun page -> page.SourcePath = sourcePath.Replace('\\', '/'))
        let persistedById = page |> Option.map (fun value -> value.Blocks |> List.map (fun block -> block.Id, block) |> Map.ofList) |> Option.defaultValue Map.empty
        let pageContextBlocks = blocks |> List.filter (fun block -> block.Mode <> Isolated && (match block.Mode with NoCheck _ | Transcript -> false | _ -> true))
        let pageContextHash = DocumentationDiscovery.contextHash options.Prelude pageContextBlocks
        let mutable ordinal = 0
        fencePattern.Replace(markdown, fun matched ->
            let block = blocks.[ordinal]
            ordinal <- ordinal + 1
            match block.Mode with
            | Prepare ->
                let persisted = persistedById |> Map.tryFind block.Id |> Option.defaultWith (fun () -> invalidOp $"Semantic artifact is missing block {block.Id}.")
                if persisted.SourceHash <> block.SourceHash then invalidOp $"Semantic source hash mismatch for {block.Id}."
                if persisted.ContextHash <> pageContextHash then invalidOp $"Semantic checking-context hash mismatch for {block.Id}."
                htmlStartMarker + renderPreparation persisted + htmlEndMarker
            | NoCheck _ | Transcript ->
                htmlStartMarker + renderLexicalSource matched.Groups.["code"].Value + htmlEndMarker
            | _ ->
                let persisted = persistedById |> Map.tryFind block.Id |> Option.defaultWith (fun () -> invalidOp $"Semantic artifact is missing block {block.Id}.")
                if persisted.SourceHash <> block.SourceHash then invalidOp $"Semantic source hash mismatch for {block.Id}."
                let expectedContext = if block.Mode = Isolated then DocumentationDiscovery.contextHash options.Prelude [ block ] else pageContextHash
                if persisted.ContextHash <> expectedContext then invalidOp $"Semantic checking-context hash mismatch for {block.Id}."
                htmlStartMarker + renderPersistedBlock persisted + htmlEndMarker)

    /// Replaces compilable F# fences with compiler-enriched HTML and appends their shared tooltip payload.
    /// Compiler-backed and lexical fallback F# fences share one HTML and styling contract.
    let formatFences (options: Options) (sourcePath: string) (markdown: string) =
        if not options.Enabled || not (fencePattern.IsMatch markdown) then markdown
        elif options.Artifact.IsSome then
            let formatted = formatFromArtifact options sourcePath markdown options.Artifact.Value
            if String.IsNullOrWhiteSpace options.Prelude then formatted
            else htmlStartMarker + renderPrelude options.Prelude + htmlEndMarker + "\n\n" + formatted
        else markdown
