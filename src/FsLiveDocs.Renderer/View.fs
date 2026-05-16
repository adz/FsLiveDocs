namespace FsLiveDocs.Renderer

open Giraffe.ViewEngine
open FsLiveDocs.Core

module View =

    let navItem (title: string) (url: string) =
        li [] [ a [ _href url; _class "hover:text-primary transition-colors" ] [ str title ] ]

    let sidebarSection (title: string) (pages: ContentPage list) =
        li [ _class "mb-4" ] [
            h3 [ _class "font-bold text-lg mb-2 px-2" ] [ str title ]
            ul [ _class "menu menu-sm bg-base-100 rounded-box w-full" ] (
                pages |> List.map (fun p ->
                    li [] [ a [ _href $"/{System.IO.Path.GetFileNameWithoutExtension(p.FilePath).ToLower()}.html" ] [ str p.Metadata.Title ] ]
                )
            )
        ]

    let apiCard (memberModel: MemberModel) =
        div [ _class "card bg-base-100 shadow-xl mb-8 border border-base-300"; _id memberModel.Id ] [
            div [ _class "card-body" ] [
                div [ _class "flex justify-between items-start" ] [
                    h2 [ _class "card-title text-2xl font-bold" ] [ str memberModel.Name ]
                    span [ _class "badge badge-primary" ] [ str "Member" ]
                ]
                div [ _class "badge badge-ghost font-mono mb-4" ] [ str memberModel.Signature ]
                
                div [ _class "prose max-w-none mb-6" ] [ rawText memberModel.SummaryHtml ]

                (if not memberModel.Parameters.IsEmpty then
                    div [ _class "mb-4" ] [
                        h4 [ _class "font-bold text-sm uppercase text-base-content/70 mb-2" ] [ str "Parameters" ]
                        div [ _class "overflow-x-auto" ] [
                            table [ _class "table table-sm table-zebra w-full" ] [
                                thead [] [ tr [] [ th [] [ str "Name" ]; th [] [ str "Type" ]; th [] [ str "Description" ] ] ]
                                tbody [] (memberModel.Parameters |> List.map (fun p ->
                                    tr [] [
                                        td [ _class "font-bold" ] [ str p.Name ]
                                        td [] [ code [ _class "text-primary" ] [ str p.Type ] ]
                                        td [] [ rawText p.DescriptionHtml ]
                                    ]
                                ))
                            ]
                        ]
                    ]
                else emptyText)

                div [ _class "mb-4" ] [
                    h4 [ _class "font-bold text-sm uppercase text-base-content/70 mb-2" ] [ str "Returns" ]
                    code [ _class "text-secondary font-mono" ] [ str memberModel.ReturnType ]
                ]

                (if not memberModel.Examples.IsEmpty then
                    div [] [
                        h4 [ _class "font-bold text-sm uppercase text-base-content/70 mb-2" ] [ str "Examples" ]
                        (memberModel.Examples |> List.map (fun ex ->
                            div [ _class "mb-4" ] [
                                if ex.Name <> "Example" then p [ _class "text-xs italic mb-1 opacity-60" ] [ str ex.Name ]
                                pre [ _class "mockup-code bg-neutral text-neutral-content p-4 rounded-lg overflow-x-auto" ] [
                                    code [ _class "language-fsharp" ] [ str ex.Content ]
                                ]
                            ]
                        ) |> div [])
                    ]
                else emptyText)
            ]
        ]

    let layout (pageTitle: string) (pages: ContentPage list) (versions: string list) (theme: string) (content: XmlNode list) =
        html [ _lang "en"; attr "data-theme" theme ] [
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
                // Using full Tailwind + DaisyUI bundle for zero-node dev experience
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/daisyui@4.10.2/dist/full.min.css" ]
                script [ _src "https://cdn.tailwindcss.com?plugins=typography" ] []
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css" ]
                style [] [ str """
                    .drawer-content { scroll-behavior: smooth; }
                    .prose :where(code):not(:where([class~="not-prose"] *))::before { content: ""; }
                    .prose :where(code):not(:where([class~="not-prose"] *))::after { content: ""; }
                """ ]
                title [] [ str $"{pageTitle} - FsLiveDocs" ]
            ]
            body [ _class "min-h-screen bg-base-200" ] [
                // Navbar
                div [ _class "navbar bg-base-100 shadow-md sticky top-0 z-50 px-4" ] [
                    div [ _class "flex-1" ] [
                        a [ _href "/"; _class "btn btn-ghost text-xl normal-case font-black tracking-tight" ] [ 
                            span [ _class "text-primary" ] [ str "Fs" ]; str "LiveDocs" 
                        ]
                    ]
                    div [ _class "flex-none gap-4" ] [
                        ul [ _class "menu menu-horizontal px-1 font-medium hidden md:flex" ] [
                            navItem "API" "/api.html"
                            li [] [
                                details [] [
                                    summary [] [ str (if versions.IsEmpty then "v0.1.0" else versions |> List.head) ]
                                    ul [ _class "p-2 bg-base-100 rounded-t-none shadow-lg z-[60]" ] (
                                        versions |> List.map (fun v -> li [] [ a [ _href $"/history/{v}/index.html" ] [ str v ] ])
                                    )
                                ]
                            ]
                        ]
                        // Theme Toggle
                        div [ _class "dropdown dropdown-end" ] [
                            label [ attr "tabindex" "0"; _class "btn btn-ghost btn-circle" ] [
                                i [ _class "bi bi-palette text-xl" ] []
                            ]
                            ul [ attr "tabindex" "0"; _class "dropdown-content z-[60] menu p-2 shadow bg-base-100 rounded-box w-52" ] [
                                li [] [ a [ attr "data-set-theme" "light" ] [ str "☀️ Light" ] ]
                                li [] [ a [ attr "data-set-theme" "dark" ] [ str "🌙 Dark" ] ]
                                li [] [ a [ attr "data-set-theme" "cupcake" ] [ str "🧁 Cupcake" ] ]
                                li [] [ a [ attr "data-set-theme" "dracula" ] [ str "🧛 Dracula" ] ]
                            ]
                        ]
                    ]
                ]

                div [ _class "container mx-auto" ] [
                    div [ _class "drawer lg:drawer-open" ] [
                        input [ _id "my-drawer-2"; _type "checkbox"; _class "drawer-toggle" ]
                        div [ _class "drawer-content flex flex-col p-6 lg:p-10" ] [
                            // Main Content (Prose)
                            div [ _class "grid grid-cols-1 xl:grid-cols-4 gap-10" ] [
                                div [ _class "xl:col-span-3 prose prose-lg max-w-none bg-base-100 p-8 rounded-2xl shadow-sm border border-base-300" ] [
                                    yield! content
                                ]
                                // Table of Contents (Right Sidebar)
                                div [ _class "hidden xl:block" ] [
                                    div [ _class "sticky top-24" ] [
                                        h4 [ _class "text-sm font-bold uppercase mb-4 opacity-50 tracking-widest" ] [ str "On This Page" ]
                                        ul [ _class "menu menu-sm opacity-80 border-l border-base-300" ] [
                                            li [] [ a [ _href "#" ] [ str "Introduction" ] ]
                                            li [] [ a [ _href "#examples" ] [ str "Examples" ] ]
                                        ]
                                    ]
                                ]
                            ]
                        ] 
                        // Sidebar (Left)
                        div [ _class "drawer-side z-40" ] [
                            label [ _for "my-drawer-2"; _class "drawer-overlay" ] []
                            div [ _class "bg-base-200 w-80 min-h-full p-4" ] [
                                ul [ _class "menu w-full" ] [
                                    sidebarSection "Guides" pages
                                ]
                            ]
                        ]
                    ]
                ]

                script [ _src "https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-fsharp.min.js" ] []
                script [] [ rawText "document.querySelectorAll('[data-set-theme]').forEach(el => el.addEventListener('click', () => document.documentElement.setAttribute('data-theme', el.getAttribute('data-set-theme'))))" ]
            ]
        ]
