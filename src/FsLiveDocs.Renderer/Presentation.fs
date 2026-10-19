namespace FsLiveDocs.Renderer

open System
open System.Net
open System.Text.RegularExpressions
open FsLiveDocs.Core
open Markdig

/// <summary>Shared HTML shaping helpers for renderer pages and cards.</summary>
module Presentation =

    let rec flattenEntities (entities: EntityModel list) =
        entities
        |> List.collect (fun e -> e :: flattenEntities e.Entities)

    let private markdownPipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build()

    let private normalizedSymbolId (target: string) =
        let withoutPrefix =
            if target.Length > 2 && target.[1] = ':' then target.Substring(2) else target
        let signature = withoutPrefix.IndexOf('(')
        if signature >= 0 then withoutPrefix.Substring(0, signature) else withoutPrefix

    let private symbolTargetsWith (entityHref: EntityModel -> string option) package =
        let rec walk entities =
            [ for entity in entities do
                  match entityHref entity with
                  | Some href ->
                      yield entity.Id, href

                      for memberModel in entity.Members do
                          yield memberModel.Id, href + "#" + memberModel.Id
                  | None -> ()

                  yield! walk entity.Entities ]

        walk package.Entities |> Map.ofList

    let private safeExternalUri target =
        match Uri.TryCreate(target, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps || uri.Scheme = Uri.UriSchemeMailto -> Some uri.AbsoluteUri
        | _ -> None

    /// Renders owned documentation semantics with the current renderer.
    let private renderDocumentationHtmlCore targets nodes =
        let encode (value: string) = WebUtility.HtmlEncode value
        let rec render nodes =
            nodes
            |> List.map (fun node ->
                let children = render node.Children
                let text = node.Text |> Option.defaultValue "" |> encode
                match node.Kind with
                | DocumentationNodeKind.Text -> text
                | DocumentationNodeKind.Paragraph -> "<p>" + children + "</p>"
                | DocumentationNodeKind.InlineCode -> "<code>" + text + "</code>"
                | DocumentationNodeKind.CodeBlock ->
                    let language = node.Language |> Option.map encode |> Option.defaultValue "text"
                    $"<pre><code class=\"language-{language}\">{text}</code></pre>"
                | DocumentationNodeKind.SymbolReference ->
                    let target = node.Target |> Option.defaultValue "" |> normalizedSymbolId
                    let label = if String.IsNullOrWhiteSpace children then encode (target.Split('.') |> Array.last) else children
                    match targets |> Map.tryFind target with
                    | Some href -> $"<a href=\"{encode href}\">{label}</a>"
                    | None -> label
                | DocumentationNodeKind.ExternalLink ->
                    let label = if String.IsNullOrWhiteSpace children then text else children
                    match node.Target |> Option.bind safeExternalUri with
                    | Some href -> $"<a href=\"{encode href}\">{label}</a>"
                    | None -> label
                | DocumentationNodeKind.UnorderedList -> "<ul>" + children + "</ul>"
                | DocumentationNodeKind.OrderedList -> "<ol>" + children + "</ol>"
                | DocumentationNodeKind.ListItem -> "<li>" + children + "</li>"
                | DocumentationNodeKind.LineBreak -> "<br>"
                | DocumentationNodeKind.Markdown -> Markdown.ToHtml(node.Text |> Option.defaultValue "", markdownPipeline))
            |> String.concat ""
        render nodes

    /// Renders documentation with explicit per-entity destinations, used by cross-set API pages.
    let renderDocumentationHtmlWithTargets package (entityTargets: Map<string, string>) nodes =
        symbolTargetsWith (fun entity -> entityTargets |> Map.tryFind entity.Id) package
        |> fun targets -> renderDocumentationHtmlCore targets nodes

    /// Renders owned documentation semantics with API links relative to the current API directory.
    let renderDocumentationHtml package nodes =
        symbolTargetsWith (fun entity -> Some(entity.Id + ".html")) package
        |> fun targets -> renderDocumentationHtmlCore targets nodes

    let highlightSignatureHtml (text: string) =
        let encoded = WebUtility.HtmlEncode text
        Regex.Replace(
            encoded,
            @"\b(option|list|seq|array|map|set|unit|string|int|bool|byte|sbyte|int16|int32|int64|uint16|uint32|uint64|decimal|float|double|char|obj)\b",
            "<span class=\"text-secondary font-semibold\">$1</span>")

    let synopsis nodes =
        let nonEmpty value =
            if String.IsNullOrWhiteSpace value then None else Some value

        let cleanRenderedHtml html =
            html
            // Keep separate list items and block lines readable before removing markup.
            |> fun value -> Regex.Replace(value, @"</(?:li|p|pre|h[1-6]|div|blockquote)>", " ", RegexOptions.IgnoreCase)
            |> fun value -> Regex.Replace(value, "<[^>]+>", "")
            // Disabled authored HTML is emitted as encoded source. Remove familiar authored tags,
            // but preserve intentionally encoded values such as &lt;value&gt; as display text.
            |> fun value -> Regex.Replace(value, @"&lt;/?(?:a|abbr|b|blockquote|br|code|del|div|em|i|kbd|mark|p|pre|small|span|strong|sub|sup|u)(?:\s[^&]*?)?&gt;", "", RegexOptions.IgnoreCase)
            |> WebUtility.HtmlDecode
            |> fun value -> Regex.Replace(value, @"[ \t\r\n]+", " ").Trim()

        let markdownText (markdown: string) =
            Markdown.ToHtml(markdown, markdownPipeline) |> cleanRenderedHtml

        let rec displayText nodes =
            nodes
            |> List.map (fun node ->
                match node.Kind with
                | DocumentationNodeKind.Markdown -> markdownText (node.Text |> Option.defaultValue "")
                | DocumentationNodeKind.UnorderedList
                | DocumentationNodeKind.OrderedList -> node.Children |> List.map (fun child -> displayText [ child ]) |> String.concat " "
                | DocumentationNodeKind.SymbolReference when node.Children.IsEmpty && node.Text.IsNone ->
                    node.Target
                    |> Option.map normalizedSymbolId
                    |> Option.map (fun target -> target.Split('.') |> Array.last |> fun name -> Regex.Replace(name, @"`\d+$", ""))
                    |> Option.defaultValue ""
                | DocumentationNodeKind.ExternalLink when node.Children.IsEmpty && node.Text.IsNone -> node.Target |> Option.defaultValue ""
                | DocumentationNodeKind.LineBreak -> " "
                | _ -> (node.Text |> Option.defaultValue "") + displayText node.Children)
            |> String.concat ""
            |> fun value -> Regex.Replace(value, @"[ \t\r\n]+", " ").Trim()

        let rec firstParagraph nodes =
            nodes
            |> List.tryPick (fun node ->
                match node.Kind with
                | DocumentationNodeKind.Markdown ->
                    let html = Markdown.ToHtml(node.Text |> Option.defaultValue "", markdownPipeline)
                    let paragraph = Regex.Match(html, @"<p(?:\s[^>]*)?>(.*?)</p>", RegexOptions.Singleline ||| RegexOptions.IgnoreCase)
                    if paragraph.Success then cleanRenderedHtml paragraph.Groups.[1].Value |> nonEmpty else None
                | DocumentationNodeKind.Paragraph -> displayText node.Children |> nonEmpty
                | _ -> firstParagraph node.Children)

        let text =
            firstParagraph nodes
            |> Option.defaultWith (fun () -> displayText nodes)
            |> fun value -> Regex.Replace(value, @"`([^`]+)`", "$1")
        if String.IsNullOrWhiteSpace text then "No description available."
        else
            let matchResult = Regex.Match(text.Trim(), @"^(.+?[.!?])(?:\s|$)")
            if matchResult.Success then matchResult.Groups.[1].Value else text.Trim()

    let entityExamples (entity: EntityModel) =
        if isNull (box entity.Examples) then [] else entity.Examples
