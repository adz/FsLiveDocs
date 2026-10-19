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
        /// <summary>Maps a fully-qualified F# symbol name to a relative API URL, or None when it is not documented.</summary>
        LinkResolver: string -> string option
        ExampleContext: bool
        StatusBadges: bool
    }

    let defaults = { Enabled = true; Artifact = None; Prelude = ""; LinkResolver = (fun _ -> None); ExampleContext = true; StatusBadges = true }
    let disabled = { defaults with Enabled = false }

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

    let private transcriptPromptPattern =
        Regex(@"^(?<indent>[ \t]*)(?<prompt>iex\(\d+\)>|iex>|fsi>|\.{3}>|>|-)(?<space>[ \t]?)(?<rest>.*)$", RegexOptions.Compiled)

    let private renderTranscriptInputLine (line: string) =
        let prompt = transcriptPromptPattern.Match(line)
        if prompt.Success then
            let indent = prompt.Groups.["indent"].Value
            let promptText = indent + prompt.Groups.["prompt"].Value + prompt.Groups.["space"].Value
            let source = prompt.Groups.["rest"].Value
            let tokens =
                lexicalTokenPattern.Matches(source)
                |> Seq.cast<Match>
                |> Seq.map (fun matched -> $"<span class=\"{lexicalClass matched.Value}\">{WebUtility.HtmlEncode matched.Value}</span>")
                |> String.concat ""
            $"<span class=\"prompt-unselectable\">{WebUtility.HtmlEncode promptText}</span>{tokens}"
        else
            renderLexicalLine line

    let private tooltipId (block: SemanticCodeBlock) index = $"livedocs-tip-{safeId block.Id}-{index}"

    let private renderToken resolveLink (block: SemanticCodeBlock) (token: SemanticToken) =
        let encoded = WebUtility.HtmlEncode token.Text

        let inner =
            match token.Tooltip with
            | Some index ->
                let id = tooltipId block index
                $"<span class=\"{tokenClass token.Kind}\" tabindex=\"0\" data-fsdocs-tip=\"{id}\" aria-describedby=\"{id}\">{encoded}</span>"
            | None -> $"<span class=\"{tokenClass token.Kind}\">{encoded}</span>"

        match token.Tooltip |> Option.bind (fun index -> block.Tooltips |> List.tryItem index |> Option.bind _.Link |> Option.bind resolveLink) with
        | Some url -> $"<a href=\"{WebUtility.HtmlEncode url}\" class=\"livedocs-token-link\">{inner}</a>"
        | None -> inner

    let private renderPersistedLine resolveLink (block: SemanticCodeBlock) (line: SemanticLine) =
        line.Tokens |> List.map (renderToken resolveLink block) |> String.concat ""

    let private renderTooltips (block: SemanticCodeBlock) =
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
            $"<div class=\"livedocs-semantic-tooltip fsdocs-tip\" id=\"{tooltipId block index}\" role=\"tooltip\" popover>{signature}{documentation}{sections}</div>")
        |> String.concat ""

    /// <summary>Renders a transcript as a faithful FSI session: prompts in a non-selectable
    /// gutter, the runnable interactions in a code frame, and the result in its own tinted band.</summary>
    /// A no-check reason is usually a sentence; drop its closing punctuation so "; not compiled" reads cleanly.
    let private reasonClause (reason: string) = reason.Trim().TrimEnd('.', ';', ':', ',')

    let private badgeText = function
        | NoCheck reason -> "Fragment", $"Fragment: {reasonClause reason}; not compiled"
        | Isolated -> "Isolated", "Isolated: compiled independently of the page"
        | Prepare -> "Setup", "Setup: compiled as page preparation"
        | Transcript -> "Verified", "Verified: compared by docs build"
        | Run -> "Verified", "Verified: compiled by docs build; execution requires test or capture"
        | Page -> "Verified", "Verified by docs build"

    let private badge mode =
        let label, description = badgeText mode
        $"<span class=\"livedocs-status\" title=\"{WebUtility.HtmlEncode description}\" aria-label=\"{WebUtility.HtmlEncode description}\">{label}</span>"

    let private badgeHeader mode = $"<div class=\"livedocs-code-header\">{badge mode}</div>"

    let private statusComment mode =
        match mode with
        | NoCheck reason ->
            let safeReason = (reasonClause reason).Replace('\n', ' ').Replace('\r', ' ')
            $"// [fragment: {safeReason}; not compiled]"
        | Isolated -> "// [isolated: compiled independently by docs build]"
        | Prepare -> "// [setup: compiled by docs build]"
        | Transcript -> "// [verified: transcript compared by docs build]"
        | Run -> "// [verified: compiled by docs build; run by test or capture]"
        | Page -> "// [verified by docs build]"

    let private renderTranscriptSource (resolveLink: string -> string option) (persisted: SemanticCodeBlock option) (source: string) =
        let parsed = ExampleTranscript.parse source
        let normalized = DocumentationDiscovery.normalizeSource source
        let rawInputLines = ResizeArray<string>()
        let resultLines = ResizeArray<string>()

        for line in normalized.Split('\n') do
            let trimmed = line.TrimStart()
            if trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed.StartsWith("- ", StringComparison.Ordinal) then
                rawInputLines.Add line
            elif not (String.IsNullOrWhiteSpace line) then
                resultLines.Add line

        let input =
            match persisted with
            | Some block ->
                let semanticLines =
                    block.Lines |> List.filter (fun line -> not (List.isEmpty line.Tokens))

                if semanticLines.Length = rawInputLines.Count then
                    (List.ofSeq rawInputLines, semanticLines)
                    ||> List.map2 (fun raw semanticLine ->
                        let prompt = transcriptPromptPattern.Match raw
                        let promptText =
                            if prompt.Success then
                                prompt.Groups.["indent"].Value + prompt.Groups.["prompt"].Value + prompt.Groups.["space"].Value
                            else ""
                        $"<span class=\"prompt-unselectable\">{WebUtility.HtmlEncode promptText}</span>{renderPersistedLine resolveLink block semanticLine}")
                    |> String.concat "\n"
                else
                    rawInputLines |> Seq.map renderTranscriptInputLine |> String.concat "\n"
            | None ->
                rawInputLines |> Seq.map renderTranscriptInputLine |> String.concat "\n"

        let result = resultLines |> Seq.map renderLexicalLine |> String.concat "\n"
        let copySource = WebUtility.HtmlEncode parsed.Script

        let toolbar =
            $"<div class=\"livedocs-code-toolbar\"><button class=\"livedocs-copy\" type=\"button\" aria-label=\"Copy the runnable F# transcript\">Copy</button><span class=\"livedocs-copy-source\" hidden>{copySource}</span></div>"

        let tooltips =
            persisted
            |> Option.map (fun block -> $"<div class=\"livedocs-tooltips\">{renderTooltips block}</div>")
            |> Option.defaultValue ""

        let resultFrame =
            if resultLines.Count = 0 then
                ""
            else
                $"<pre class=\"code-frame livedocs-result\"><code class=\"language-fsharp\">{result}</code></pre>"

        $"<div class=\"livedocs-code livedocs-transcript not-prose\">{toolbar}<pre class=\"code-frame\"><code class=\"language-fsharp\">{input}</code></pre>{resultFrame}{tooltips}</div>"

    let private codeFrame extraClass lines tooltips =
        $"<div class=\"livedocs-code {extraClass} not-prose\"><pre class=\"code-frame\"><code class=\"language-fsharp\">{lines}</code></pre>{tooltips}</div>"

    let private renderLexicalSource source =
        let lines =
            DocumentationDiscovery.normalizeSource(source).TrimEnd('\n').Split('\n')
            |> Array.map renderLexicalLine
            |> String.concat "\n"
        codeFrame "livedocs-lexical-code" lines ""

    let contextLines (context: ExampleContext) =
        [ if not context.Requires.IsEmpty then
              let opens = context.Requires |> List.choose (fun requirement -> if requirement.Open = "" then None else Some("open " + requirement.Open)) |> List.distinct |> String.concat "; "
              let packages = context.Requires |> List.choose _.Package |> List.distinct |> List.sort
              let suffix = if packages.IsEmpty then "" else "(packages: " + String.concat ", " packages + ")"
              yield "Requires: " + opens + (if opens = "" || suffix = "" then suffix else " " + suffix)
          for signature in context.Types do yield signature ]

    let private renderPersistedBlock (resolveLink: string -> string option) showContext (block: SemanticCodeBlock) =
        let lines = block.Lines |> List.map (renderPersistedLine resolveLink block) |> String.concat "\n"
        let context =
            if not showContext then "" else
                block.ExampleContext
                |> Option.map (fun value -> contextLines value |> List.map (fun line -> $"<div><code>{WebUtility.HtmlEncode line}</code></div>") |> String.concat "")
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
                |> Option.map (fun value -> $"<details class=\"livedocs-shared-setup livedocs-example-context not-prose\"><summary>Requires &amp; types</summary><div>{value}</div></details>")
                |> Option.defaultValue ""
        codeFrame "livedocs-semantic-code" lines $"<div class=\"livedocs-tooltips\">{renderTooltips block}</div>" + context

    let private openPattern = Regex(@"^\s*open\s+([\w.]+)\s*$")

    /// Select setup using compiler-resolved opens and references in the checked examples.
    /// Keep unrecognised setup constructs rather than silently making a runnable example invalid.
    let private usedSetup (blocks: DocumentationBlock list) (byId: Map<string, SemanticCodeBlock>) =
        let checkedBlocks =
            blocks |> List.filter (fun block -> block.Mode = Page || block.Mode = Run)
            |> List.choose (fun block -> Map.tryFind block.Id byId)
        let checkedOpens =
            checkedBlocks
            |> List.choose _.ExampleContext
            |> List.collect _.Requires
            |> List.map _.Open |> Set.ofList
        let references =
            checkedBlocks
            |> List.collect (fun block -> block.Lines |> List.collect (fun line -> line.Tokens))
            |> List.filter (fun token -> token.Tooltip.IsSome)
            |> List.map _.Text |> Set.ofList
        let preparations = blocks |> List.filter (fun block -> block.Mode = Prepare)
        let lines =
            preparations
            |> List.collect (fun block -> DocumentationDiscovery.normalizeSource(block.ExpandedSource).TrimEnd('\n').Split('\n') |> Array.toList)
        let definition = Regex(@"^\s*(?:let|type|module)\s+(?:rec\s+|private\s+|mutable\s+)*([\w']+)")
        let named =
            lines |> List.choose (fun line -> let matched = definition.Match line in if matched.Success then Some matched.Groups.[1].Value else None)
            |> Set.ofList
        let mutable needed = references |> Set.intersect named
        let mutable changed = true
        while changed do
            let expanded =
                lines
                |> List.fold (fun (owner, found) line ->
                    let matched = definition.Match line
                    let owner = if matched.Success then Some matched.Groups.[1].Value else owner
                    let found =
                        if owner |> Option.exists needed.Contains then
                            named |> Set.fold (fun state name -> if Regex.IsMatch(line, @"\b" + Regex.Escape name + @"\b") then Set.add name state else state) found
                        else found
                    owner, found) (None, needed)
                |> snd
            changed <- expanded <> needed
            needed <- expanded
        let preparations = blocks |> List.filter (fun block -> block.Mode = Prepare) |> List.choose (fun block -> Map.tryFind block.Id byId)
        let preparationOpens =
            if needed.IsEmpty then Set.empty
            else
                preparations |> List.choose _.ExampleContext |> List.collect _.Requires |> List.map _.Open |> Set.ofList
        // Schema-2 capsules migrate to absent example context. Without compiler-derived
        // requirements, retain authored opens rather than producing broken setup code.
        let opens =
            if checkedBlocks |> List.exists (fun block -> block.ExampleContext.IsNone) then
                lines
                |> List.choose (fun line -> let matched = openPattern.Match line in if matched.Success then Some matched.Groups.[1].Value else None)
                |> Set.ofList
            else Set.union checkedOpens preparationOpens
        let selected =
            lines
            |> List.fold (fun (owner, kept) line ->
                let matched = definition.Match line
                let owner = if matched.Success then Some matched.Groups.[1].Value else owner
                let opened = openPattern.Match line
                let keep =
                    if opened.Success then opens.Contains opened.Groups.[1].Value
                    elif matched.Success then needed.Contains matched.Groups.[1].Value
                    elif owner.IsSome then owner |> Option.exists needed.Contains
                    else not (String.IsNullOrWhiteSpace line)
                owner, if keep then line :: kept else kept) (None, [])
            |> snd |> List.rev
        String.concat "\n" selected

    let private renderPreparation source =
        $"<details class=\"livedocs-shared-setup livedocs-page-setup not-prose\"><summary>Show page setup</summary>{renderLexicalSource source}</details>"

    let private renderPrelude (prelude: string) =
        $"<details class=\"livedocs-shared-setup livedocs-repository-setup not-prose\"><summary>Repository F# setup</summary>{renderLexicalSource prelude}</details>"

    let private formatFromArtifact options sourcePath markdown (artifact: SemanticDocumentationArtifact) =
        let fences = DocumentationDiscovery.scanFsharpFences markdown
        let blocks : DocumentationBlock list = DocumentationDiscovery.discoverMarkdown sourcePath None markdown
        let page : SemanticPage option = artifact.Pages |> List.tryFind (fun page -> page.SourcePath = sourcePath.Replace('\\', '/'))
        let persistedById : Map<string, SemanticCodeBlock> =
            page |> Option.map (fun value -> value.Blocks |> List.map (fun block -> block.Id, block) |> Map.ofList) |> Option.defaultValue Map.empty
        let setup = usedSetup blocks persistedById
        let mutable setupShown = false
        let pageContextBlocks = blocks |> List.filter (fun block -> block.Mode <> Isolated && (match block.Mode with NoCheck _ | Transcript -> false | _ -> true))
        let pageContextHash = DocumentationDiscovery.contextHash options.Prelude pageContextBlocks
        let builder = Text.StringBuilder()
        let mutable cursor = 0
        List.iter2
            (fun (fence: DocumentationDiscovery.FenceMatch) (block: DocumentationBlock) ->
                builder.Append(markdown.Substring(cursor, fence.Start - cursor)) |> ignore
                let replacement =
                    match block.Mode with
                    | Prepare ->
                        let persisted = persistedById |> Map.tryFind block.Id |> Option.defaultWith (fun () -> invalidOp $"Semantic artifact is missing block {block.Id}.")
                        if persisted.SourceHash <> block.SourceHash then invalidOp $"Semantic source hash mismatch for {block.Id}: expected {persisted.SourceHash}, got {block.SourceHash}."
                        if persisted.ContextHash <> pageContextHash then invalidOp $"Semantic checking-context hash mismatch for {block.Id}."
                        ""
                    | NoCheck _ ->
                        htmlStartMarker + renderLexicalSource fence.Code + htmlEndMarker
                    | Transcript ->
                        let persisted = persistedById |> Map.tryFind block.Id
                        match persisted with
                        | Some value when value.SourceHash <> block.SourceHash ->
                            invalidOp $"Semantic source hash mismatch for {block.Id}: expected {value.SourceHash}, got {block.SourceHash}."
                        | _ -> ()
                        htmlStartMarker + renderTranscriptSource options.LinkResolver persisted fence.Code + htmlEndMarker
                    | _ ->
                        let persisted = persistedById |> Map.tryFind block.Id |> Option.defaultWith (fun () -> invalidOp $"Semantic artifact is missing block {block.Id}.")
                        if persisted.SourceHash <> block.SourceHash then invalidOp $"Semantic source hash mismatch for {block.Id}: expected {persisted.SourceHash}, got {block.SourceHash}."
                        let expectedContext = if block.Mode = Isolated then DocumentationDiscovery.contextHash options.Prelude [ block ] else pageContextHash
                        if persisted.ContextHash <> expectedContext then invalidOp $"Semantic checking-context hash mismatch for {block.Id}."
                        let showContext = options.ExampleContext && not (fence.Info.Split(' ') |> Array.exists (fun token -> token.Equals("no-context", StringComparison.OrdinalIgnoreCase)))
                        htmlStartMarker + renderPersistedBlock options.LinkResolver showContext persisted + htmlEndMarker
                let replacement =
                    if options.StatusBadges then
                        let index = replacement.IndexOf("<div class=\"livedocs-code ", StringComparison.Ordinal)
                        if index < 0 then replacement
                        else replacement.Insert(replacement.IndexOf('>', index) + 1, badgeHeader block.Mode)
                    else replacement
                if not setupShown && not (String.IsNullOrWhiteSpace setup) && (block.Mode = Page || block.Mode = Run) then
                    builder.Append(htmlStartMarker + renderPreparation setup + htmlEndMarker + "\n\n") |> ignore
                    setupShown <- true
                builder.Append(replacement) |> ignore
                cursor <- fence.Start + fence.Length)
            fences
            blocks
        builder.Append(markdown.Substring(cursor)) |> ignore
        builder.ToString()

    /// Adds compiler-derived context after checked fences for plain Markdown and LLM readers.
    let formatMarkdownFences (options: Options) (sourcePath: string) (markdown: string) =
        match options.Artifact with
        | None -> markdown
        | Some artifact when not options.Enabled -> markdown
        | Some artifact ->
            let page = artifact.Pages |> List.tryFind (fun page -> page.SourcePath = sourcePath.Replace('\\', '/'))
            let byId = page |> Option.map (fun page -> page.Blocks |> List.map (fun block -> block.Id, block) |> Map.ofList) |> Option.defaultValue Map.empty
            let fences = DocumentationDiscovery.scanFsharpFences markdown
            let blocks = DocumentationDiscovery.discoverMarkdown sourcePath None markdown
            let setup = usedSetup blocks byId
            let builder = Text.StringBuilder()
            let mutable cursor = 0
            let mutable setupShown = false
            for fence, block in List.zip fences blocks do
                let endOffset = fence.Start + fence.Length
                builder.Append(markdown.Substring(cursor, fence.Start - cursor)) |> ignore
                if block.Mode = Prepare then ()
                else
                    if not setupShown && not (String.IsNullOrWhiteSpace setup) && (block.Mode = Page || block.Mode = Run) then
                        builder.AppendLine("```fsharp").AppendLine("// Page setup (hidden on the site; needed to run the examples below)").AppendLine(setup).AppendLine("```") |> ignore
                        setupShown <- true
                    // Put status inside the fence so blog prose (excerpts, RSS and reading
                    // time) never treats it as a paragraph of its own.
                    let original = markdown.Substring(fence.Start, fence.Length)
                    let firstLineEnd = original.IndexOf('\n')
                    if firstLineEnd < 0 then builder.Append(original) |> ignore
                    else
                        builder.Append(original.Substring(0, firstLineEnd + 1))
                            .Append(statusComment block.Mode).Append('\n')
                            .Append(original.Substring(firstLineEnd + 1)) |> ignore
                if options.ExampleContext && not (fence.Info.Split(' ') |> Array.exists (fun flag -> flag.Equals("no-context", StringComparison.OrdinalIgnoreCase))) then
                    match block.Mode, Map.tryFind block.Id byId with
                    | (Page | Run), Some persisted when persisted.SourceHash = block.SourceHash ->
                        match persisted.ExampleContext with
                        | Some context ->
                            let lines = contextLines context
                            if not lines.IsEmpty then
                                builder.AppendLine("```fsharp") |> ignore
                                for line in lines do builder.AppendLine("// " + line) |> ignore
                                builder.AppendLine("```") |> ignore
                        | None -> ()
                    | _ -> ()
                cursor <- endOffset
            builder.Append(markdown.Substring cursor) |> ignore
            builder.ToString()

    /// Replaces compilable F# fences with compiler-enriched HTML and appends their shared tooltip payload.
    /// Compiler-backed and lexical fallback F# fences share one HTML and styling contract.
    let formatFences (options: Options) (sourcePath: string) (markdown: string) =
        if not options.Enabled || (DocumentationDiscovery.scanFsharpFences markdown).IsEmpty then markdown
        elif options.Artifact.IsSome then
            let formatted = formatFromArtifact options sourcePath markdown options.Artifact.Value
            if String.IsNullOrWhiteSpace options.Prelude then formatted
            else htmlStartMarker + renderPrelude options.Prelude + htmlEndMarker + "\n\n" + formatted
        else markdown
