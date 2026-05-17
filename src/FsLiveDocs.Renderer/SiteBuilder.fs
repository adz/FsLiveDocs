namespace FsLiveDocs.Renderer

open System.IO
open Giraffe.ViewEngine
open FsLiveDocs.Core

module SiteBuilder =

    let renderPage (page: ContentPage) (allPages: ContentPage list) (package: PackageModel) (versions: string list) (theme: string) (rootPath: string) =
        let content = [
            div [] [ rawText page.ContentHtml ]
        ]
        View.layout page.Metadata.Title allPages package versions theme rootPath content
        |> fun node -> RenderView.AsString.htmlNode node

    let renderApi (package: PackageModel) (allPages: ContentPage list) (versions: string list) (theme: string) (rootPath: string) =
        let rec renderEntity (e: EntityModel) =
            div [ _class "entity mb-16 pt-8"; _id e.Id ] [
                h2 [ _class "text-3xl font-black mb-8 pb-4 border-b-4 border-primary/20 w-full flex items-center justify-between" ] [ 
                    str e.Name 
                    span [ _class "badge badge-outline opacity-30 text-xs font-mono" ] [ str e.Kind ]
                ]
                div [ _class "space-y-12" ] (e.Members |> List.map View.apiCard)
                (if not e.Entities.IsEmpty then
                    div [ _class "nested mt-12 pl-6 border-l-2 border-base-300" ] (e.Entities |> List.map renderEntity)
                else emptyText)
            ]

        let content = package.Entities |> List.map renderEntity
        View.layout "API Reference" allPages package versions theme rootPath content
        |> fun node -> RenderView.AsString.htmlNode node

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

    let build (package: PackageModel) (pages: ContentPage list) (versions: string list) (theme: string) (rootPath: string) (outputDir: string) =
        if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore
        
        // LLMS Integration
        File.WriteAllText(Path.Combine(outputDir, "llms.txt"), generateLlmsTxt package)
        
        // Render content pages
        for page in pages do
            let (html: string) = renderPage page pages package versions theme rootPath
            let fileName = Path.GetFileNameWithoutExtension(page.FilePath).ToLower() + ".html"
            File.WriteAllText(Path.Combine(outputDir, fileName), html)

        // Render API docs
        let (apiHtml: string) = renderApi package pages versions theme rootPath
        File.WriteAllText(Path.Combine(outputDir, "api.html"), apiHtml)

        // Generate index.html if not present
        if not (File.Exists(Path.Combine(outputDir, "index.html"))) then
            let indexContent = [ 
                h1 [ _class "text-6xl font-black mb-8 tracking-tighter" ] [ 
                    span [ _class "text-primary" ] [ str "Fs" ]; str "LiveDocs" 
                ]
                p [ _class "text-xl opacity-60 leading-relaxed max-w-2xl" ] [ 
                    str "The next generation documentation engine for F#. Verified docstrings, live snippet transclusion, and solution-wide API tracking." 
                ]
                div [ _class "flex gap-4 mt-10" ] [
                    a [ _href (rootPath + "api.html"); _class "btn btn-primary btn-lg rounded-2xl px-10" ] [ str "Explore API" ]
                    a [ _href (rootPath + "verified-examples.html"); _class "btn btn-outline btn-lg rounded-2xl px-10" ] [ str "Learn Guides" ]
                ]
            ]
            let (html: string) = View.layout "Home" pages package versions theme rootPath indexContent |> RenderView.AsString.htmlNode
            File.WriteAllText(Path.Combine(outputDir, "index.html"), html)

    let buildAll (historyDir: string) (currentPackage: PackageModel) (pages: ContentPage list) (theme: string) (outputDir: string) =
        let versions = 
            if Directory.Exists(historyDir) then
                Directory.GetFiles(historyDir, "*.json")
                |> Array.map Path.GetFileNameWithoutExtension
                |> Array.toList
            else []
        
        let allVersions = currentPackage.Version :: versions |> List.distinct
        
        // Build current version at root
        build currentPackage pages allVersions theme "" outputDir

        // Build historical versions
        if Directory.Exists(historyDir) then
            for vJson in Directory.GetFiles(historyDir, "*.json") do
                let v = Path.GetFileNameWithoutExtension(vJson)
                let json = File.ReadAllText(vJson)
                let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(json)
                let vDir = Path.Combine(outputDir, "history", v)
                build package pages allVersions theme "../../" vDir
