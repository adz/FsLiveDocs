namespace FsLiveDocs.Renderer

open System
open System.IO
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

    let rec private flattenEntities (entities: EntityModel list) =
        entities
        |> List.collect (fun e -> e :: flattenEntities e.Entities)

    let private entityExamples (entity: EntityModel) =
        if isNull (box entity.Examples) then [] else entity.Examples

    /// <summary>Renders a single Markdown guide page.</summary>
    let renderPage (page: ContentPage) (allPages: ContentPage list) (package: PackageModel) (versions: string list) (theme: string) (rootPath: string) =
        let content = [
            div [] [ rawText page.ContentHtml ]
        ]
        View.layout page.Metadata.Title allPages package versions theme rootPath content
        |> fun node -> RenderView.AsString.htmlNode node

    /// <summary>Renders a single API entity page (Module or Type).</summary>
    let renderEntityPage (e: EntityModel) (allPages: ContentPage list) (package: PackageModel) (versions: string list) (theme: string) (rootPath: string) =
        let rec renderEntity (ent: EntityModel) isNested =
            div [ _class (if isNested then "mt-12 pl-8 border-l-4 border-base-200" else ""); _id ent.Id ] [
                h1 [ _class (if isNested then "text-2xl font-bold mb-4" else "text-4xl font-black mb-8 pb-4 border-b-8 border-primary/10 tracking-tight flex justify-between items-center") ] [ 
                    str ent.Name 
                    span [ _class "badge badge-primary opacity-50 font-mono text-[10px]" ] [ str ent.Kind ]
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
                        h2 [ _class "text-xl font-black mb-6 opacity-30 uppercase tracking-widest" ] [ str "Contents" ]
                        div [ _class "grid grid-cols-1 md:grid-cols-2 gap-4" ] (
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

                div [ _class "space-y-12" ] (ent.Members |> List.map View.apiCard)

                let examples = entityExamples ent
                (if not examples.IsEmpty then
                    div [ _class "mt-24 border-t border-base-300 pt-16" ] [
                        h2 [ _class "text-3xl font-black mb-10 tracking-tighter" ] [ str "Examples" ]
                        div [ _class "space-y-12" ] (
                            examples |> List.map (fun ex ->
                                div [ _class "not-prose" ] [
                                    if ex.Name <> "Example" then
                                        h3 [ _class "text-sm font-black mb-4 opacity-40 tracking-[0.3em]" ] [ str ex.Name ]
                                    pre [ _class "bg-neutral text-neutral-content p-8 rounded-[2rem] text-sm font-mono overflow-x-auto border-0 shadow-2xl shadow-black/20" ] [
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
    let build (package: PackageModel) (pages: ContentPage list) (versions: string list) (theme: string) (rootPath: string) (outputDir: string) =
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
            let html = renderEntityPage e pages package versions theme (rootPath + "../")
            File.WriteAllText(Path.Combine(apiDir, e.Id + ".html"), html)

        // Generate api.html (Overview / API Reference index)
        let apiOverview = [
            h1 [ _class "text-5xl font-black mb-12 tracking-tighter" ] [ str "API Reference" ]
            div [ _class "grid grid-cols-1 md:grid-cols-2 gap-6" ] (
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
            h1 [ _class "text-7xl font-black mb-8 tracking-tighter" ] [ 
                span [ _class "text-primary italic" ] [ str "Fs" ]; str "LiveDocs" 
            ]
            p [ _class "text-2xl opacity-60 leading-relaxed max-w-3xl mb-12" ] [ 
                str "Verified documentation for the F# ecosystem. Guaranteed to compile, guaranteed to run." 
            ]
            div [ _class "flex flex-wrap gap-6" ] [
                a [ _href (rootPath + "api.html"); _class "btn btn-primary btn-lg rounded-2xl px-12 h-20 shadow-2xl shadow-primary/20 text-lg" ] [ str "Explore API" ]
                a [ _href (rootPath + "verified-examples.html"); _class "btn btn-outline btn-lg rounded-2xl px-12 h-20 text-lg hover:bg-base-300" ] [ str "Read Guides" ]
            ]
            div [ _class "grid grid-cols-1 md:grid-cols-3 gap-8 mt-24" ] [
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-[2rem] shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-primary/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-check2-circle text-3xl text-primary" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Verified" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Every example is a test. If it doesn't run, the build fails." ]
                ]
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-[2rem] shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-secondary/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-link-45deg text-3xl text-secondary" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Connected" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Live transclusion from your source code directly into your guides." ]
                ]
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-[2rem] shadow-sm hover:shadow-xl transition-all group" ] [
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

    let buildAll (historyDir: string) (currentPackage: PackageModel) (pages: ContentPage list) (theme: string) (outputDir: string) =
        let versions = 
            if Directory.Exists(historyDir) then
                Directory.GetFiles(historyDir, "*.json")
                |> Array.map Path.GetFileNameWithoutExtension
                |> Array.toList
            else []
        
        let allVersions = currentPackage.Version :: versions |> List.distinct
        
        build currentPackage pages allVersions theme "" outputDir

        if Directory.Exists(historyDir) then
            for vJson in Directory.GetFiles(historyDir, "*.json") do
                let v = Path.GetFileNameWithoutExtension(vJson)
                let json = File.ReadAllText(vJson)
                let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(json)
                let vDir = Path.Combine(outputDir, "history", v)
                build package pages allVersions theme "../../" vDir
