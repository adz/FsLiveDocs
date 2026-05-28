namespace FsLiveDocs.Renderer

open System
open System.IO
open System.Net
open System.Text.RegularExpressions
open Giraffe.ViewEngine
open FsLiveDocs.Core

/// <summary>The high-level site assembly engine.</summary>
/// <example name="BuildSummaryExample">
/// let package = { Version = "1.0"; Entities = []; Scenarios = [] }
/// let summary = SiteBuilder.generateLlmsTxt package
/// printfn "%s" summary
/// // EXPECTED: # API Reference for LLMs
/// </example>
module SiteBuilder =

    let private summarize (html: string) =
        let text =
            html
            |> fun value -> Regex.Replace(value, "<.*?>", String.Empty)
            |> WebUtility.HtmlDecode
            |> fun value -> Regex.Replace(value, @"\s+", " ").Trim()
        if String.IsNullOrWhiteSpace text then "No description available."
        else
            let sentence = Regex.Match(text, @"^(.+?[.!?])(?:\s|$)")
            if sentence.Success then sentence.Groups.[1].Value else text

    let private highlightSignatureHtml (text: string) =
        let encoded = WebUtility.HtmlEncode(text)
        Regex.Replace(
            encoded,
            @"\b(option|list|seq|array|map|set|unit|string|int|bool|byte|sbyte|int16|int32|int64|uint16|uint32|uint64|decimal|float|double|char|obj)\b",
            "<span class=\"text-secondary font-semibold\">$1</span>")

    let rec private flattenEntities (entities: EntityModel list) =
        entities
        |> List.collect (fun e -> e :: flattenEntities e.Entities)

    let private entityExamples (entity: EntityModel) =
        if isNull (box entity.Examples) then [] else entity.Examples

    /// <summary>Renders a single Markdown guide page.</summary>
    /// <param name="page">The processed content page to render.</param>
    /// <param name="allPages">All content pages used for navigation and version links.</param>
    /// <param name="package">The extracted package model.</param>
    /// <param name="versions">Known history versions for the version switcher.</param>
    /// <param name="theme">The active DaisyUI theme.</param>
    /// <param name="rootPath">The relative root path for generated links.</param>
    /// <returns>The rendered HTML document as a string.</returns>
    let renderPage (page: ContentPage) (allPages: ContentPage list) (package: PackageModel) (versions: string list) (theme: string) (rootPath: string) =
        let content = [
            div [] [ rawText page.ContentHtml ]
        ]
        View.layout page.Metadata.Title allPages package versions theme rootPath content
        |> fun node -> RenderView.AsString.htmlNode node

    /// <summary>Renders a single API entity page (Module or Type).</summary>
    /// <param name="e">The entity to render.</param>
    /// <param name="allPages">All guide pages used for navigation and the toc.</param>
    /// <param name="package">The extracted package model.</param>
    /// <param name="config">Build-time site configuration.</param>
    /// <param name="versions">Known history versions for the version switcher.</param>
    /// <param name="theme">The active DaisyUI theme.</param>
    /// <param name="rootPath">The relative root path for generated links.</param>
    /// <returns>The rendered HTML document as a string.</returns>
    let renderEntityPage (e: EntityModel) (allPages: ContentPage list) (package: PackageModel) (config: SiteConfig) (versions: string list) (theme: string) (rootPath: string) =
        let rec renderEntity (ent: EntityModel) isNested =
            div [ _class (if isNested then "mt-12 pl-8 border-l-4 border-base-200" else ""); _id ent.Id ] [
                h1 [
                    _class (
                        (if isNested then "text-2xl font-bold mb-4" else "text-4xl font-black mb-8 pb-4 border-b-8 border-primary/10 tracking-tight")
                        + " group scroll-mt-24 flex items-center gap-3")
                    attr "data-toc-title" ent.Name
                ] [ 
                    span [ _class "leading-tight" ] [ str ent.Name ]
                    span [ _class "badge badge-primary opacity-50 font-mono text-[10px]" ] [ str ent.Kind ]
                    a [
                        _href ("#" + ent.Id)
                        _class "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
                        attr "aria-label" $"Copy link to {ent.Name}"
                        attr "title" $"Copy link to {ent.Name}"
                    ] [ i [ _class "bi bi-link-45deg text-base" ] [] ]
                ]
                
                (if not (String.IsNullOrWhiteSpace(ent.SummaryHtml)) then
                    let cleanSummary = Regex.Replace(ent.SummaryHtml, "^<h1.*?>.*?<\/h1>", String.Empty, RegexOptions.Singleline).Trim()
                    if not (String.IsNullOrWhiteSpace(cleanSummary)) then
                        div [ _class "prose prose-lg max-w-none mb-12 bg-base-200/30 p-8 rounded-3xl border border-base-300" ] [ 
                            rawText cleanSummary 
                        ]
                    else emptyText
                else emptyText)

                // Render children as a directory if this is a namespace or has many entities
                (if not ent.Entities.IsEmpty then
                    div [ _class "mb-16" ] [
                        View.h2WithAnchor (ent.Id + "-contents") "Contents" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                        div [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ] (
                            ent.Entities |> List.map (fun ne ->
                                a [ _href (ne.Id + ".html")
                                    _class "flex items-center justify-between p-4 bg-base-100 border border-base-300 rounded-2xl hover:border-primary hover:shadow-md transition-all group" ] [
                                    span [ _class "font-bold group-hover:text-primary transition-colors" ] [ str ne.Name ]
                                    span [ _class "badge badge-sm opacity-40 font-mono text-[10px]" ] [ str ne.Kind ]
                                ]
                            )
                        )
                    ]
                else emptyText)

                if ent.Kind <> "Module" && not ent.Members.IsEmpty then
                    div [ _class "mb-16 not-prose" ] [
                        View.h2WithAnchor (ent.Id + "-spec") "Specification" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                        div [ _class "rounded-3xl border border-base-300 bg-base-100 shadow-sm overflow-hidden" ] [
                            div [ _class "grid grid-cols-1 md:grid-cols-3 gap-0 border-b border-base-300" ] [
                                div [ _class "p-5 md:p-6" ] [
                                    div [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ] [ str "Kind" ]
                                    div [ _class "text-lg font-black" ] [ str ent.Kind ]
                                ]
                                div [ _class "p-5 md:p-6 border-t md:border-t-0 md:border-l border-base-300" ] [
                                    div [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ] [ str "Members" ]
                                    div [ _class "text-lg font-black" ] [ str (string ent.Members.Length) ]
                                ]
                                div [ _class "p-5 md:p-6 border-t md:border-t-0 md:border-l border-base-300" ] [
                                    div [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ] [ str "Examples" ]
                                    div [ _class "text-lg font-black" ] [ str (string (entityExamples ent).Length) ]
                                ]
                            ]
                            div [ _class "p-5 md:p-6 space-y-3" ] (
                                ent.Members
                                |> List.take (min 5 ent.Members.Length)
                                |> List.map (fun m ->
                                    div [ _class "flex flex-col gap-2 rounded-2xl border border-base-300 bg-base-200/20 p-4" ] [
                                        div [ _class "flex items-center justify-between gap-4" ] [
                                            span [ _class "font-bold text-primary" ] [ str m.Name ]
                                            span [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 font-black" ] [ str "Signature" ]
                                        ]
                                        div [ _class "font-mono text-sm text-accent overflow-x-auto" ] [
                                            rawText (highlightSignatureHtml m.Signature)
                                        ]
                                    ]
                                )
                            )
                        ]
                    ]
                else emptyText

                if not ent.Members.IsEmpty then
                    div [ _class "mb-16 not-prose" ] [
                        View.h2WithAnchor (ent.Id + "-summary") "Summary" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                        div [ _class "overflow-x-auto rounded-2xl border border-base-300 shadow-sm" ] [
                            table [ _class "table table-zebra w-full" ] [
                                thead [ _class "bg-base-200/50" ] [
                                    tr [] [
                                        th [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Name" ]
                                        th [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Signature" ]
                                        th [ attr "style" "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Synopsis" ]
                                    ]
                                ]
                                tbody [] (
                                    ent.Members
                                    |> List.map (fun m ->
                                        tr [] [
                                            td [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                                a [ _href ("#" + m.Id); _class "font-bold text-primary hover:underline" ] [ str m.Name ]
                                            ]
                                            td [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                                span [ _class "font-mono text-xs text-secondary bg-secondary/5 px-2 py-0.5 rounded" ] [ rawText m.Signature ]
                                            ]
                                            td [ _class "text-sm opacity-80 leading-relaxed"; attr "style" "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                                str (summarize m.SummaryHtml)
                                            ]
                                        ]
                                    )
                                )
                            ]
                        ]
                    ]
                else emptyText

                div [ _class "space-y-12" ] (ent.Members |> List.map (View.apiCard config.RepoUrl))

                let examples = entityExamples ent
                (if not examples.IsEmpty then
                    div [ _class "mt-24 border-t border-base-300 pt-16" ] [
                        View.h2WithAnchor (ent.Id + "-examples") "Examples" "text-3xl font-black mb-10 tracking-tighter"
                        div [ _class "space-y-12" ] (
                            examples |> List.map (fun ex ->
                                let exampleId =
                                    let slug = Regex.Replace(ex.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')
                                    if String.IsNullOrWhiteSpace slug then "example" else slug
                                div [ _class "not-prose" ] [
                                    if ex.Name <> "Example" then
                                        View.h3WithAnchor (ent.Id + "-example-" + exampleId) ex.Name "text-sm font-black mb-4 opacity-40 tracking-[0.3em]"
                                    pre [ _class "bg-neutral text-neutral-content p-6 rounded-2xl text-sm font-mono overflow-x-auto border-0 shadow-md" ] [
                                        code [ _class "language-fsharp" ] [ str ex.Content ]
                                    ]
                                ])
                        )
                    ]
                else emptyText)
            ]

        let content = [ renderEntity e false ]
        View.layout e.Name allPages package versions theme rootPath content
        |> fun node -> RenderView.AsString.htmlNode node

    /// <summary>Generates a text-based summary of the API for LLM consumption.</summary>
    /// <param name="package">The package model to summarize.</param>
    /// <returns>A plaintext `llms.txt` document.</returns>
    let generateLlmsTxt (package: PackageModel) =
        let sb = System.Text.StringBuilder()
        sb.AppendLine("# API Reference for LLMs") |> ignore
        let rec walkEntity (e: EntityModel) indent =
            let pad = String.replicate indent "  "
            sb.AppendLine($"{pad}- {e.Kind}: {e.Name} ({e.Id})") |> ignore
            for m in e.Members do
                sb.AppendLine($"{pad}  * {m.Name}: {m.Signature}") |> ignore
            for ne in e.Entities do
                walkEntity ne (indent + 1)
        for e in package.Entities do
            walkEntity e 0
        sb.ToString()

    /// <summary>Builds the primary documentation site.</summary>
    /// <param name="package">The package model that drives the API pages.</param>
    /// <param name="pages">The guide pages to render.</param>
    /// <param name="config">Build-time site configuration.</param>
    /// <param name="versions">Known history versions for the version switcher.</param>
    /// <param name="theme">The active DaisyUI theme.</param>
    /// <param name="rootPath">The relative root path for generated links.</param>
    /// <param name="outputDir">The output directory that will receive the rendered site.</param>
    let build (package: PackageModel) (pages: ContentPage list) (config: SiteConfig) (versions: string list) (theme: string) (rootPath: string) (outputDir: string) =
        if Directory.Exists(outputDir) then Directory.Delete(outputDir, true)
        Directory.CreateDirectory(outputDir) |> ignore
        
        // LLMS Integration
        File.WriteAllText(Path.Combine(outputDir, "llms.txt"), generateLlmsTxt package)
        
        // Render content pages
        for page in pages do
            let (html: string) = renderPage page pages package versions theme rootPath
            let fileName = Path.GetFileNameWithoutExtension(page.FilePath).ToLower() + ".html"
            File.WriteAllText(Path.Combine(outputDir, fileName), html)

        // Render API docs - Multi-page approach
        let apiDir = Path.Combine(outputDir, "api")
        if not (Directory.Exists(apiDir)) then Directory.CreateDirectory(apiDir) |> ignore
        
        for e in flattenEntities package.Entities do
            let html = renderEntityPage e pages package config versions theme (rootPath + "../")
            File.WriteAllText(Path.Combine(apiDir, e.Id + ".html"), html)

        // Generate api.html (Overview / API Reference index)
        let apiOverview = [
            View.h1WithAnchor "api-reference" "API Reference" "text-5xl font-black mb-12 tracking-tighter"
            div [ _class "grid grid-cols-1 md:grid-cols-2 gap-6 not-prose" ] (
                let topLevel = 
                    match package.Entities with
                    | [ e ] when e.Kind = "Namespace" && e.Members.IsEmpty -> e.Entities
                    | _ -> package.Entities
                
                topLevel |> List.map (fun e ->
                    a [ _href (rootPath + "api/" + e.Id + ".html"); _class "card bg-base-100 border border-base-300 p-6 hover:shadow-xl hover:border-primary transition-all group" ] [
                        div [ _class "flex justify-between items-center" ] [
                            h3 [ _class "text-xl font-bold group-hover:text-primary transition-colors" ] [ str e.Name ]
                            span [ _class "badge badge-sm opacity-40" ] [ str e.Kind ]
                        ]
                        let summary = 
                            if String.IsNullOrEmpty(e.SummaryHtml) then "No description available."
                            else 
                                // Strip HTML tags for the card description
                                Regex.Replace(e.SummaryHtml, "<.*?>", String.Empty).Trim()
                        p [ _class "text-sm opacity-60 mt-2 line-clamp-2" ] [ str summary ]
                    ]
                )
            )
        ]
        let (apiHtml: string) = View.layout "API Reference" pages package versions theme rootPath apiOverview |> RenderView.AsString.htmlNode
        File.WriteAllText(Path.Combine(outputDir, "api.html"), apiHtml)

        // Generate index.html
        let indexPath = Path.Combine(outputDir, "index.html")
        let indexContent = [ 
            h1 [
                _id "home"
                attr "data-toc-title" "FsLiveDocs"
                _class "text-7xl font-black mb-8 tracking-tighter group scroll-mt-24 flex items-center gap-3"
            ] [
                span [ _class "text-primary italic" ] [ str "Fs" ]
                str "LiveDocs"
                a [
                    _href "#home"
                    _class "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
                    attr "aria-label" "Copy link to FsLiveDocs"
                    attr "title" "Copy link to FsLiveDocs"
                ] [ i [ _class "bi bi-link-45deg text-base" ] [] ]
            ]
            p [ _class "text-2xl opacity-60 leading-relaxed max-w-3xl mb-12" ] [ 
                str "Verified documentation for the F# ecosystem. Guaranteed to compile, guaranteed to run." 
            ]
            div [ _class "flex flex-wrap gap-6 not-prose" ] [
                a [ _href (rootPath + "api.html"); _class "btn btn-primary btn-lg rounded-2xl px-12 h-20 shadow-2xl shadow-primary/20 text-lg no-underline" ] [ str "Explore API" ]
                a [ _href (rootPath + "verified-examples.html"); _class "btn btn-outline btn-lg rounded-2xl px-12 h-20 text-lg hover:bg-base-300 no-underline" ] [ str "Read Guides" ]
            ]
            div [ _class "grid grid-cols-1 md:grid-cols-3 gap-8 mt-24 not-prose" ] [
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-3xl shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-primary/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-check2-circle text-3xl text-primary" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Verified" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Every example is a test. If it doesn't run, the build fails." ]
                ]
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-3xl shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-secondary/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-link-45deg text-3xl text-secondary" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Connected" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Live transclusion from your source code directly into your guides." ]
                ]
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-3xl shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-accent/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-clock-history text-3xl text-accent" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Versioned" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Zero-recompile documentation snapshots." ]
                ]
            ]
        ]
        let (html: string) = View.layout "Home" pages package versions theme rootPath indexContent |> RenderView.AsString.htmlNode
        File.WriteAllText(indexPath, html)

    /// <summary>Builds the current site and computes the version list from history snapshots.</summary>
    /// <param name="historyDir">The directory containing previous package snapshots.</param>
    /// <param name="currentPackage">The latest package model.</param>
    /// <param name="pages">The guide pages to render.</param>
    /// <param name="config">Build-time site configuration.</param>
    /// <param name="theme">The active DaisyUI theme.</param>
    /// <param name="outputDir">The output directory that will receive the rendered site.</param>
    let buildAll (historyDir: string) (currentPackage: PackageModel) (pages: ContentPage list) (config: SiteConfig) (theme: string) (outputDir: string) =
        let versions = 
            if Directory.Exists(historyDir) then
                Directory.GetFiles(historyDir, "*.json")
                |> Array.map Path.GetFileNameWithoutExtension
                |> Array.toList
            else []
        
        let allVersions = currentPackage.Version :: versions |> List.distinct
        
        build currentPackage pages config allVersions theme "" outputDir

        if Directory.Exists(historyDir) then
            for vJson in Directory.GetFiles(historyDir, "*.json") do
                let v = Path.GetFileNameWithoutExtension(vJson)
                let json = File.ReadAllText(vJson)
                let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(json)
                let vDir = Path.Combine(outputDir, "history", v)
                build package pages config allVersions theme "../../" vDir
