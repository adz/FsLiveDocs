namespace FsLiveDocs.Renderer

open System
open System.Net
open System.Text.RegularExpressions
open FsLiveDocs.Core

/// <summary>Shared HTML shaping helpers for renderer pages and cards.</summary>
module Presentation =

    let private stripHtml (html: string) =
        html
        |> fun text -> Regex.Replace(text, "<.*?>", String.Empty)
        |> WebUtility.HtmlDecode
        |> fun text -> Regex.Replace(text, @"\s+", " ").Trim()

    let highlightSignatureHtml (text: string) =
        let encoded = WebUtility.HtmlEncode(stripHtml text)
        Regex.Replace(
            encoded,
            @"\b(option|list|seq|array|map|set|unit|string|int|bool|byte|sbyte|int16|int32|int64|uint16|uint32|uint64|decimal|float|double|char|obj)\b",
            "<span class=\"text-secondary font-semibold\">$1</span>")

    let synopsisFromHtml (html: string) =
        let text = stripHtml html
        if String.IsNullOrWhiteSpace text then "No description available."
        else
            let matchResult = Regex.Match(text, @"^(.+?[.!?])(?:\s|$)")
            if matchResult.Success then matchResult.Groups.[1].Value else text

    let rec flattenEntities (entities: EntityModel list) =
        entities
        |> List.collect (fun e -> e :: flattenEntities e.Entities)

    let entityExamples (entity: EntityModel) =
        if isNull (box entity.Examples) then [] else entity.Examples
