namespace FsLiveDocs.Renderer

open System.IO
open Giraffe.ViewEngine
open FsLiveDocs.Core

module SiteBuilder =

    let renderPage (page: ContentPage) (allPages: ContentPage list) =
        let content = [
            div [] [ rawText page.ContentHtml ]
        ]
        Components.layout page.Metadata.Title allPages content
        |> fun node -> RenderView.AsString.htmlNode node

    let renderApi (package: PackageModel) (allPages: ContentPage list) =
        let rec renderEntity (e: EntityModel) =
            div [ _class "entity mb-5" ] [
                h2 [ _id e.Id ] [ str e.Name ]
                div [ _class "members" ] (e.Members |> List.map Components.apiCard)
                div [ _class "nested" ] (e.Entities |> List.map renderEntity)
            ]

        let content = package.Entities |> List.map renderEntity
        Components.layout "API Reference" allPages content
        |> fun node -> RenderView.AsString.htmlNode node

    let build (package: PackageModel) (pages: ContentPage list) (outputDir: string) =
        if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore
        
        // Render content pages
        for page in pages do
            let (html: string) = renderPage page pages
            let fileName = Path.GetFileNameWithoutExtension(page.FilePath).ToLower() + ".html"
            File.WriteAllText(Path.Combine(outputDir, fileName), html)

        // Render API docs
        let (apiHtml: string) = renderApi package pages
        File.WriteAllText(Path.Combine(outputDir, "api.html"), apiHtml)

        // Generate index.html if not present
        if not (File.Exists(Path.Combine(outputDir, "index.html"))) then
            let indexContent = [ h1 [] [ str "Welcome to LiveDocs" ]; p [] [ str "Select a page from the sidebar to get started." ] ]
            let (html: string) = Components.layout "Home" pages indexContent |> RenderView.AsString.htmlNode
            File.WriteAllText(Path.Combine(outputDir, "index.html"), html)
