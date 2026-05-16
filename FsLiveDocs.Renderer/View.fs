namespace FsLiveDocs.Renderer

open Giraffe.ViewEngine
open FsLiveDocs.Core

module Components =

    let internalRenderNavbar (siteTitle: string) (versions: string list) =
        nav [ _class "navbar navbar-expand-lg navbar-dark bg-primary" ] [
            div [ _class "container-fluid" ] [
                a [ _class "navbar-brand"; _href "/" ] [ str siteTitle ]
                div [ _class "collapse navbar-collapse" ] [
                    ul [ _class "navbar-nav me-auto" ] [
                        li [ _class "nav-item dropdown" ] [
                            a [ _class "nav-link dropdown-toggle"; _href "#"; _id "versionDropdown"; attr "role" "button"; attr "data-bs-toggle" "dropdown" ] [ str "Version" ]
                            ul [ _class "dropdown-menu" ] (versions |> List.map (fun v -> li [] [ a [ _class "dropdown-item"; _href $"/{v}" ] [ str v ] ]))
                        ]
                    ]
                    form [ _class "d-flex" ] [
                        input [ _class "form-control me-2"; _type "search"; _id "search-input"; _placeholder "Search..." ]
                    ]
                ]
            ]
        ]

    let sidebar (pages: ContentPage list) =
        div [ _class "sidebar p-3 bg-light" ] [
            ul [ _class "list-unstyled" ] (
                pages 
                |> List.sortBy (fun p -> p.Metadata.Weight)
                |> List.map (fun p -> 
                    li [] [ 
                        a [ _href $"/{p.Metadata.Title.ToLower()}.html"; _class "nav-link" ] [ str p.Metadata.Title ] 
                    ]
                )
            )
        ]

    let apiCard (memberModel: MemberModel) =
        div [ _class "card mb-3" ] [
            div [ _class "card-header bg-light" ] [
                code [] [ str memberModel.Signature ]
            ]
            div [ _class "card-body" ] [
                h5 [ _class "card-title" ] [ str memberModel.Name ]
                div [ _class "card-text" ] [ rawText memberModel.SummaryHtml ]
                (if not memberModel.Examples.IsEmpty then
                    div [] [
                        h6 [] [ str "Examples" ]
                        (memberModel.Examples |> List.map (fun ex -> 
                            pre [ _class "bg-dark text-white p-2" ] [ code [] [ str ex.Content ] ]
                        ) |> div [])
                    ]
                else str "")
            ]
        ]

    let layout (pageTitle: string) (pages: ContentPage list) (content: XmlNode list) =
        html [ _lang "en" ] [
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css" ]
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism.min.css" ]
                style [] [ str ".sidebar { height: 100vh; position: sticky; top: 0; }" ]
                title [] [ str pageTitle ]
            ]
            body [] [
                internalRenderNavbar pageTitle ["v0.1.0"]
                div [ _class "container-fluid" ] [
                    div [ _class "row" ] [
                        div [ _class "col-md-3 d-none d-md-block" ] [ sidebar pages ]
                        div [ _class "col-md-9 p-4" ] content
                    ]
                ]
                script [ _src "https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-fsharp.min.js" ] []
            ]
        ]
