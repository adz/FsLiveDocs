namespace FsLiveDocs.Core

open System
open System.Net
open System.Text
open System.Text.RegularExpressions
open System.Xml.Linq
open FSharp.Formatting.CodeFormat

/// Build-time semantic formatting for F# code embedded in documentation pages.
module SemanticCode =

    let htmlStartMarker = "<!--fslivedocs-semantic-start-->"
    let htmlEndMarker = "<!--fslivedocs-semantic-end-->"

    type Options = {
        Enabled: bool
        References: string list
        Opens: string list
        OnDiagnostic: string -> unit
    }

    let defaults = { Enabled = true; References = []; Opens = []; OnDiagnostic = ignore }
    let disabled = { defaults with Enabled = false }

    let private fencePattern =
        Regex(
            @"(?ms)^```(?<info>fsharp(?:[ \t]+[^\r\n]*)?)[ \t]*\r?\n(?<code>.*?)^```[ \t]*$",
            RegexOptions.Compiled)

    let private isSemanticFence (info: string) =
        let options = info.Split([| ' '; '\t'; ',' |], StringSplitOptions.RemoveEmptyEntries)
        options.Length > 0
        && options.[0].Equals("fsharp", StringComparison.OrdinalIgnoreCase)
        && not (options |> Array.exists (fun option ->
            option.Equals("no-check", StringComparison.OrdinalIgnoreCase)
            || option.Equals("transcript", StringComparison.OrdinalIgnoreCase)))

    let private compilerOptions references =
        references
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> List.distinct
        |> List.map (fun path ->
            let escapedPath = path.Replace("\"", "\\\"")
            $"-r:\"{escapedPath}\"")
        |> String.concat " "
        |> function
            | "" -> None
            | options -> Some options

    let private fallbackHtml (code: string) =
        $"<pre><code class=\"language-fsharp\">{WebUtility.HtmlEncode(code.TrimEnd('\r', '\n'))}</code></pre>"

    let private normalizeWhitespace (value: string) =
        Regex.Replace(value, @"\s+", " ").Trim()

    let private formatXmlDocumentation (tooltipHtml: string) =
        Regex.Replace(
            tooltipHtml,
            "(?s)<em>(?<documentation>.*?)</em>",
            fun matched ->
                let encoded = matched.Groups.["documentation"].Value
                let decoded = WebUtility.HtmlDecode(encoded)
                if not (decoded.Contains("<summary", StringComparison.OrdinalIgnoreCase)) then matched.Value
                else
                    try
                        let root = XElement.Parse("<root>" + decoded + "</root>")
                        let sections =
                            root.Elements()
                            |> Seq.choose (fun element ->
                                let content = element.Value |> normalizeWhitespace |> WebUtility.HtmlEncode
                                if String.IsNullOrWhiteSpace content then None
                                else
                                    let name = element.Name.LocalName.ToLowerInvariant()
                                    let attribute attributeName =
                                        element.Attribute(XName.Get attributeName)
                                        |> Option.ofObj
                                        |> Option.map (_.Value >> WebUtility.HtmlEncode)
                                    match name with
                                    | "summary" -> Some $"<div class=\"fsdocs-tip-summary\">{content}</div>"
                                    | "param" ->
                                        let label = defaultArg (attribute "name") "Parameter"
                                        Some $"<div class=\"fsdocs-tip-detail\"><strong>{label}:</strong> {content}</div>"
                                    | "typeparam" ->
                                        let label = defaultArg (attribute "name") "Type parameter"
                                        Some $"<div class=\"fsdocs-tip-detail\"><strong>{label}:</strong> {content}</div>"
                                    | "returns" -> Some $"<div class=\"fsdocs-tip-detail\"><strong>Returns:</strong> {content}</div>"
                                    | "remarks" -> Some $"<div class=\"fsdocs-tip-detail\"><strong>Remarks:</strong> {content}</div>"
                                    | "platforms" -> Some $"<div class=\"fsdocs-tip-detail\"><strong>Platforms:</strong> {content}</div>"
                                    | "exception" -> Some $"<div class=\"fsdocs-tip-detail\"><strong>Exception:</strong> {content}</div>"
                                    | "example" -> Some $"<div class=\"fsdocs-tip-detail\"><strong>Example:</strong> {content}</div>"
                                    | _ -> None)
                            |> String.concat ""
                        if String.IsNullOrWhiteSpace sections then matched.Value
                        else $"<div class=\"fsdocs-tip-docs\">{sections}</div>"
                    with _ ->
                        matched.Value)

    let private removeRecoveryTooltips (tooltipHtml: string) (snippetHtml: string) =
        let tooltipPattern = Regex("(?s)<div popover class=\"fsdocs-tip\" id=\"(?<id>[^\"]+)\">(?<body>.*?)</div>")
        let recoveryIds =
            tooltipPattern.Matches(tooltipHtml)
            |> Seq.cast<Match>
            |> Seq.filter (fun matched -> Regex.IsMatch(WebUtility.HtmlDecode(matched.Groups.["body"].Value), @"\bobj\b"))
            |> Seq.map (fun matched -> matched.Groups.["id"].Value)
            |> Seq.toList
        let cleanTooltips =
            tooltipPattern.Replace(tooltipHtml, fun matched ->
                if recoveryIds |> List.contains matched.Groups.["id"].Value then "" else matched.Value)
        let cleanSnippet =
            recoveryIds
            |> List.fold (fun html id ->
                Regex.Replace(
                    html,
                    $" data-fsdocs-tip=\"{Regex.Escape(id)}\" data-fsdocs-tip-unique=\"\d+\"",
                    "")) snippetHtml
        cleanTooltips, cleanSnippet

    let private removeAllTooltipAttributes (snippetHtml: string) =
        Regex.Replace(
            snippetHtml,
            " data-fsdocs-tip=\"[^\"]+\" data-fsdocs-tip-unique=\"\d+\"",
            "")

    /// Replaces compilable F# fences with compiler-enriched HTML and appends their shared tooltip payload.
    /// Fences marked `fsharp no-check` or `fsharp transcript` remain available to the normal Markdown renderer.
    let formatFences (options: Options) (sourcePath: string) (markdown: string) =
        if not options.Enabled || not (fencePattern.IsMatch markdown) then markdown
        else
            let tooltips = ResizeArray<string>()
            let mutable semanticIndex = 0
            let result =
                fencePattern.Replace(markdown, fun matched ->
                    if not (isSemanticFence matched.Groups.["info"].Value) then matched.Value
                    else
                        let index = semanticIndex
                        semanticIndex <- semanticIndex + 1
                        let code = matched.Groups.["code"].Value
                        try
                            let checkingSource = StringBuilder()
                            options.Opens
                            |> List.filter (String.IsNullOrWhiteSpace >> not)
                            |> List.distinct
                            |> List.iter (fun namespaceName -> checkingSource.AppendLine($"open {namespaceName}") |> ignore)
                            checkingSource.AppendLine("// [snippet:livedocs]") |> ignore
                            checkingSource.Append(code) |> ignore
                            checkingSource.AppendLine().AppendLine("// [/snippet]") |> ignore
                            let snippets, diagnostics =
                                CodeFormatter.ParseAndCheckSource(
                                    $"{sourcePath}.{index}.fsx",
                                    checkingSource.ToString(),
                                    compilerOptions options.References,
                                    None,
                                    options.OnDiagnostic)
                            let formatted = CodeFormat.FormatHtml(snippets, $"livedocs{index}-", addErrors = false)
                            let displayed = formatted.Snippets |> Array.tryFind (fun snippet -> snippet.Key = "livedocs")
                            match displayed with
                            | None -> fallbackHtml code
                            | Some snippet when String.IsNullOrWhiteSpace snippet.Content -> fallbackHtml code
                            | Some snippet when diagnostics |> Array.exists (fun (SourceError(_, _, kind, _)) -> kind = ErrorKind.Error) ->
                                htmlStartMarker + removeAllTooltipAttributes snippet.Content + htmlEndMarker
                            | Some snippet ->
                                    let formattedTooltips = formatXmlDocumentation formatted.ToolTip
                                    let cleanTooltips, cleanSnippet = removeRecoveryTooltips formattedTooltips snippet.Content
                                    if not (String.IsNullOrWhiteSpace cleanTooltips) then
                                        tooltips.Add(cleanTooltips.Replace("\r", "").Replace("\n", "<br />"))
                                    htmlStartMarker + cleanSnippet + htmlEndMarker
                        with error ->
                            options.OnDiagnostic $"Semantic F# formatting failed for {sourcePath}: {error.Message}"
                            fallbackHtml code)

            if tooltips.Count = 0 then result
            else
                result
                + "\n"
                + htmlStartMarker
                + "<div class=\"livedocs-tooltips not-prose\">"
                + String.concat "\n" tooltips
                + "</div>"
                + htmlEndMarker
                + "\n"
