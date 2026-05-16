namespace FsLiveDocs.Renderer

open System.IO
open Giraffe.ViewEngine
open FsLiveDocs.Core

module SiteBuilder =

    let renderPage (page: ContentPage) (allPages: ContentPage list) (versions: string list) =
        let content = [
            div [] [ rawText page.ContentHtml ]
        ]
        Components.layout page.Metadata.Title allPages versions content
        |> fun node -> RenderView.AsString.htmlNode node

    let renderApi (package: PackageModel) (allPages: ContentPage list) (versions: string list) =
        let rec renderEntity (e: EntityModel) =
            div [ _class "entity mb-5" ] [
                h2 [ _class "border-bottom pb-2 mb-4"; _id e.Id ] [ str e.Name ]
                div [ _class "members" ] (e.Members |> List.map Components.apiCard)
                div [ _class "nested ps-4 border-start" ] (e.Entities |> List.map renderEntity)
            ]

        let content = package.Entities |> List.map renderEntity
        Components.layout "API Reference" allPages versions content
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

    let build (package: PackageModel) (pages: ContentPage list) (versions: string list) (outputDir: string) =
        if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore
        
        // LLMS Integration
        File.WriteAllText(Path.Combine(outputDir, "llms.txt"), generateLlmsTxt package)
        
        // Render content pages
        for page in pages do
            let (html: string) = renderPage page pages versions
            let fileName = Path.GetFileNameWithoutExtension(page.FilePath).ToLower() + ".html"
            File.WriteAllText(Path.Combine(outputDir, fileName), html)

        // Render API docs
        let (apiHtml: string) = renderApi package pages versions
        File.WriteAllText(Path.Combine(outputDir, "api.html"), apiHtml)

        // Generate index.html if not present
        if not (File.Exists(Path.Combine(outputDir, "index.html"))) then
            let indexContent = [ h1 [] [ str "Welcome to LiveDocs" ]; p [] [ str "Select a page from the sidebar to get started." ] ]
            let (html: string) = Components.layout "Home" pages versions indexContent |> RenderView.AsString.htmlNode
            File.WriteAllText(Path.Combine(outputDir, "index.html"), html)

    let buildAll (historyDir: string) (currentPackage: PackageModel) (pages: ContentPage list) (outputDir: string) =
        let versions = 
            if Directory.Exists(historyDir) then
                Directory.GetFiles(historyDir, "*.json")
                |> Array.map Path.GetFileNameWithoutExtension
                |> Array.toList
            else []
        
        let allVersions = currentPackage.Version :: versions |> List.distinct
        
        // Build current version at root
        build currentPackage pages allVersions outputDir

        // Build historical versions
        if Directory.Exists(historyDir) then
            for vJson in Directory.GetFiles(historyDir, "*.json") do
                let v = Path.GetFileNameWithoutExtension(vJson)
                let json = File.ReadAllText(vJson)
                let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(json)
                let vDir = Path.Combine(outputDir, "history", v)
                build package pages allVersions vDir
