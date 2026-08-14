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

    let private symbolTargets package =
        let rec walk entities =
            [ for entity in entities do
                yield entity.Id, entity.Id + ".html"
                for memberModel in entity.Members do
                    yield memberModel.Id, entity.Id + ".html#" + memberModel.Id
                yield! walk entity.Entities ]
        walk package.Entities |> Map.ofList

    let private safeExternalUri target =
        match Uri.TryCreate(target, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps || uri.Scheme = Uri.UriSchemeMailto -> Some uri.AbsoluteUri
        | _ -> None

    /// Renders owned documentation semantics with the current renderer.
    let renderDocumentationHtml package nodes =
        let targets = symbolTargets package
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

    let highlightSignatureHtml (text: string) =
        let encoded = WebUtility.HtmlEncode text
        Regex.Replace(
            encoded,
            @"\b(option|list|seq|array|map|set|unit|string|int|bool|byte|sbyte|int16|int32|int64|uint16|uint32|uint64|decimal|float|double|char|obj)\b",
            "<span class=\"text-secondary font-semibold\">$1</span>")

    let synopsis nodes =
        let text = Documentation.plainText nodes
        if String.IsNullOrWhiteSpace text then "No description available."
        else
            let matchResult = Regex.Match(text, @"^(.+?[.!?])(?:\s|$)")
            if matchResult.Success then matchResult.Groups.[1].Value else text

    let entityExamples (entity: EntityModel) =
        if isNull (box entity.Examples) then [] else entity.Examples
