namespace FsLiveDocs.Renderer

open Giraffe.ViewEngine
open FsLiveDocs.Core

module Components =

    let internalRenderNavbar (siteTitle: string) (versions: string list) =
        nav [ _class "navbar navbar-expand-lg navbar-dark bg-primary mb-4" ] [
            div [ _class "container-fluid" ] [
                a [ _class "navbar-brand"; _href "/" ] [ str siteTitle ]
                div [ _class "collapse navbar-collapse" ] [
                    ul [ _class "navbar-nav me-auto" ] [
                        li [ _class "nav-item dropdown" ] [
                            a [ _class "nav-link dropdown-toggle"; _href "#"; _id "versionDropdown"; attr "role" "button"; attr "data-bs-toggle" "dropdown" ] [ 
                                str (if versions.IsEmpty then "Version" else versions |> List.head) 
                            ]
                            ul [ _class "dropdown-menu" ] (versions |> List.map (fun v -> li [] [ a [ _class "dropdown-item"; _href $"/history/{v}/index.html" ] [ str v ] ]))
                        ]
                        li [ _class "nav-item" ] [ a [ _class "nav-link"; _href "/api.html" ] [ str "API" ] ]
                    ]
                    div [ _id "search" ] [
                        input [ _class "form-control me-2"; _type "search"; _id "search-input"; _placeholder "Search..." ]
                    ]
                ]
            ]
        ]

    let sidebar (pages: ContentPage list) =
        let grouped = pages |> List.groupBy (fun p -> System.IO.Path.GetDirectoryName(p.FilePath))
        div [ _class "sidebar p-3 bg-light shadow-sm" ] [
            h5 [] [ str "Documentation" ]
            ul [ _class "list-unstyled ps-0" ] (
                grouped 
                |> List.map (fun (dir, dirPages) ->
                    let dirName = if dir = "" || dir = "docs" then "General" else System.IO.Path.GetFileName(dir)
                    li [ _class "mb-1" ] [
                        button [ _class "btn btn-toggle d-inline-flex align-items-center rounded border-0 collapsed"; attr "data-bs-toggle" "collapse"; attr "data-bs-target" $"#{dirName}-collapse" ] [ str dirName ]
                        div [ _class "collapse show"; _id $"{dirName}-collapse" ] [
                            ul [ _class "btn-toggle-nav list-unstyled fw-normal pb-1 small ps-3" ] (
                                dirPages 
                                |> List.sortBy (fun p -> p.Metadata.Weight)
                                |> List.map (fun p -> 
                                    li [] [ 
                                        a [ _href $"/{System.IO.Path.GetFileNameWithoutExtension(p.FilePath).ToLower()}.html"; _class "link-dark d-inline-flex text-decoration-none rounded" ] [ str p.Metadata.Title ] 
                                    ]
                                )
                            )
                        ]
                    ]
                )
            )
        ]

    let apiCard (memberModel: MemberModel) =
        div [ _class "card mb-4 shadow-sm"; _id memberModel.Id ] [
            div [ _class "card-header bg-white border-bottom-0 d-flex justify-content-between align-items-center" ] [
                code [ _class "fs-6 text-primary" ] [ str memberModel.Signature ]
                span [ _class "badge bg-light text-dark border" ] [ str "Member" ]
            ]
            div [ _class "card-body pt-0" ] [
                h4 [ _class "card-title fw-bold" ] [ str memberModel.Name ]
                div [ _class "card-text text-muted mb-3" ] [ rawText memberModel.SummaryHtml ]
                
                (if not memberModel.Parameters.IsEmpty then
                    div [ _class "mb-3" ] [
                        h6 [ _class "fw-bold text-uppercase small" ] [ str "Parameters" ]
                        table [ _class "table table-sm table-borderless" ] [
                            tbody [] (memberModel.Parameters |> List.map (fun p -> 
                                tr [] [
                                    td [ _class "fw-bold pe-3" ] [ str p.Name ]
                                    td [ _class "text-muted pe-3" ] [ code [] [ str p.Type ] ]
                                    td [] [ rawText p.DescriptionHtml ]
                                ]
                            ))
                        ]
                    ]
                else emptyText)

                div [ _class "mb-3" ] [
                    h6 [ _class "fw-bold text-uppercase small" ] [ str "Returns" ]
                    code [] [ str memberModel.ReturnType ]
                ]

                (if not memberModel.Examples.IsEmpty then
                    div [] [
                        h6 [ _class "fw-bold text-uppercase small" ] [ str "Examples" ]
                        (memberModel.Examples |> List.map (fun ex -> 
                            div [ _class "mb-2" ] [
                                if ex.Name <> "Example" then p [ _class "small text-muted mb-1" ] [ str ex.Name ]
                                pre [ _class "bg-dark text-white p-3 rounded" ] [ code [ _class "language-fsharp" ] [ str ex.Content ] ]
                            ]
                        ) |> div [])
                    ]
                else emptyText)
            ]
        ]

    let layout (pageTitle: string) (pages: ContentPage list) (versions: string list) (content: XmlNode list) =
        html [ _lang "en" ] [
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.0/font/bootstrap-icons.css" ]
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css" ]
                link [ _rel "stylesheet"; _href "/_pagefind/pagefind-ui.css" ]
                style [] [ str """
                    .sidebar { height: 100vh; position: sticky; top: 0; overflow-y: auto; }
                    .btn-toggle-nav a:hover { background-color: #e9ecef; }
                    pre { margin-bottom: 0; }
                    code[class*="language-"], pre[class*="language-"] { font-size: 0.9em; }
                    #search { min-width: 250px; }
                """ ]
                title [] [ str $"{pageTitle} - FsLiveDocs" ]
            ]
            body [] [
                internalRenderNavbar "FsLiveDocs" versions
                div [ _class "container-fluid" ] [
                    div [ _class "row" ] [
                        div [ _class "col-md-3 d-none d-md-block" ] [ sidebar pages ]
                        div [ _class "col-md-9 p-4" ] [
                            div [ _id "search-ui"; _class "mb-4" ] []
                            yield! content
                        ]
                    ]
                ]
                script [ _src "/_pagefind/pagefind-ui.js" ] []
                script [] [ rawText "window.addEventListener('DOMContentLoaded', (event) => { new PagefindUI({ element: '#search-ui', showSubResults: true }); });" ]
                script [ _src "https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-fsharp.min.js" ] []
            ]
        ]
