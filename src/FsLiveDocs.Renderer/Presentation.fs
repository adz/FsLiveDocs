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

    let rec flattenEntities (entities: EntityModel list) =
        entities
        |> List.collect (fun e -> e :: flattenEntities e.Entities)

    let private referenceSlug (entityId: string) =
        Regex.Replace(entityId.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')

    /// Resolves links emitted by FSharp.Formatting for compiler XML references to FsLiveDocs API pages.
    /// References to symbols outside the generated public model retain their label without a broken link.
    let resolveApiReferenceLinks (package: PackageModel) (html: string) =
        if String.IsNullOrWhiteSpace html then html
        else
            let targets =
                flattenEntities package.Entities
                |> List.groupBy (fun entity -> referenceSlug entity.Id)
                |> List.map (fun (slug, entities) ->
                    match entities with
                    | [ entity ] -> slug, entity.Id
                    | _ -> invalidOp $"API reference slug '{slug}' maps to multiple entities.")
                |> Map.ofList

            let resolved =
                Regex.Replace(
                    html,
                    "<a href=\"/reference/(?<slug>[^\"]+)\\.html\">(?<label>.*?)</a>",
                    (fun (link: Match) ->
                        let label = link.Groups.["label"].Value
                        match targets |> Map.tryFind link.Groups.["slug"].Value with
                        | Some entityId -> $"<a href=\"{entityId}.html\">{label}</a>"
                        | None -> label),
                    RegexOptions.Singleline)

            if resolved.Contains("href=\"/reference/", StringComparison.Ordinal) then
                invalidOp $"FSharp.Formatting emitted an API reference link that FsLiveDocs could not normalize: {resolved}"
            resolved

    let normalizeEntityReferenceLinks (package: PackageModel) (entity: EntityModel) =
        let resolve = resolveApiReferenceLinks package
        let normalizeMember (memberModel: MemberModel) =
            {
                memberModel with
                    Signature = resolve memberModel.Signature
                    Parameters =
                        memberModel.Parameters
                        |> List.map (fun parameter ->
                            { parameter with
                                Type = resolve parameter.Type
                                DescriptionHtml = resolve parameter.DescriptionHtml })
                    ReturnType = resolve memberModel.ReturnType
                    SummaryHtml = resolve memberModel.SummaryHtml
                    RemarksHtml = resolve memberModel.RemarksHtml
            }
        let rec normalize current =
            {
                current with
                    SummaryHtml = resolve current.SummaryHtml
                    Members = current.Members |> List.map normalizeMember
                    Entities = current.Entities |> List.map normalize
            }
        normalize entity

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

    let entityExamples (entity: EntityModel) =
        if isNull (box entity.Examples) then [] else entity.Examples
