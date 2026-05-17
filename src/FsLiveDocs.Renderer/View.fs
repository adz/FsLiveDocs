namespace FsLiveDocs.Renderer

open Giraffe.ViewEngine
open FsLiveDocs.Core
open System.IO

module View =

    let navItem (title: string) (url: string) =
        li [] [ a [ _href url; _class "hover:text-primary transition-colors px-4 py-2" ] [ str title ] ]

    let sidebarPageLink (rootPath: string) (p: ContentPage) =
        let fileName = Path.GetFileNameWithoutExtension(p.FilePath).ToLower() + ".html"
        li [] [ a [ _href (rootPath + fileName); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm" ] [ str p.Metadata.Title ] ]

    let rec sidebarEntityLink (rootPath: string) (e: EntityModel) =
        li [] [
            details [ _class "group"; attr "open" "false" ] [
                summary [ _class "flex items-center justify-between py-2 px-4 hover:bg-base-300 rounded-lg cursor-pointer list-none" ] [
                    span [ _class "text-sm font-medium truncate" ] [ 
                        a [ _href (rootPath + "api.html#" + e.Id); _class "hover:link" ] [ str e.Name ]
                    ]
                    (if not e.Entities.IsEmpty then i [ _class "bi bi-chevron-down text-[10px] transition-transform group-open:rotate-180" ] [] else emptyText)
                ]
                (if not e.Entities.IsEmpty then
                    ul [ _class "menu menu-sm pl-4 pt-1" ] (e.Entities |> List.map (sidebarEntityLink rootPath))
                else emptyText)
            ]
        ]

    let sidebar (rootPath: string) (pages: ContentPage list) (package: PackageModel) =
        // Group API by Functional Area (Top-level namespace)
        let apiGroups = 
            package.Entities 
            |> List.groupBy (fun e -> 
                let parts = e.Id.Split('.')
                if parts.Length > 1 then parts.[0] else "Default"
            )

        div [ _class "flex flex-col gap-6" ] [
            // Guides Section
            div [] [
                h3 [ _class "text-[10px] font-black uppercase opacity-40 px-4 mb-2 tracking-widest" ] [ str "Guides" ]
                ul [ _class "menu menu-sm p-0 gap-1" ] (pages |> List.sortBy (fun p -> p.Metadata.Weight) |> List.map (sidebarPageLink rootPath))
            ]
            // API Reference Section
            div [] [
                h3 [ _class "text-[10px] font-black uppercase opacity-40 px-4 mb-2 tracking-widest" ] [ str "API Reference" ]
                ul [ _class "menu menu-sm p-0 gap-1" ] (
                    apiGroups |> List.map (fun (area, entities) ->
                        li [] [
                            details [ _class "group"; attr "open" "true" ] [
                                summary [ _class "flex items-center justify-between py-2 px-4 text-primary font-bold hover:bg-base-300 rounded-lg cursor-pointer list-none" ] [
                                    span [ _class "text-xs" ] [ str area ]
                                    i [ _class "bi bi-chevron-down text-[8px] transition-transform group-open:rotate-180" ] []
                                ]
                                ul [ _class "menu menu-sm p-0 mt-1" ] (entities |> List.map (sidebarEntityLink rootPath))
                            ]
                        ]
                    )
                )
            ]
        ]

    let apiCard (memberModel: MemberModel) =
        div [ _class "card bg-base-100 shadow-sm border border-base-300 overflow-hidden"; _id memberModel.Id ] [
            div [ _class "bg-base-200/50 px-6 py-3 border-b border-base-300 flex justify-between items-center" ] [
                code [ _class "text-xs font-mono text-primary bg-primary/10 px-2 py-1 rounded" ] [ str memberModel.Signature ]
                span [ _class "text-[10px] font-black uppercase opacity-40" ] [ str "Member" ]
            ]
            div [ _class "p-6" ] [
                h2 [ _class "text-2xl font-black mb-4" ] [ str memberModel.Name ]
                div [ _class "prose prose-sm max-w-none mb-6 opacity-80" ] [ rawText memberModel.SummaryHtml ]

                (if not memberModel.Parameters.IsEmpty then
                    div [ _class "mb-6" ] [
                        h4 [ _class "text-[10px] font-black uppercase opacity-40 mb-3 tracking-widest" ] [ str "Parameters" ]
                        div [ _class "overflow-x-auto rounded-lg border border-base-300" ] [
                            table [ _class "table table-sm table-zebra w-full" ] [
                                thead [ _class "bg-base-200" ] [ tr [] [ th [] [ str "Name" ]; th [] [ str "Type" ]; th [] [ str "Description" ] ] ]
                                tbody [] (memberModel.Parameters |> List.map (fun p ->
                                    tr [] [
                                        td [ _class "font-bold font-mono text-xs" ] [ str p.Name ]
                                        td [] [ code [ _class "text-secondary text-[10px]" ] [ str p.Type ] ]
                                        td [ _class "text-xs opacity-70" ] [ rawText p.DescriptionHtml ]
                                    ]
                                ))
                            ]
                        ]
                    ]
                else emptyText)

                div [ _class "mb-6" ] [
                    h4 [ _class "text-[10px] font-black uppercase opacity-40 mb-2 tracking-widest" ] [ str "Returns" ]
                    code [ _class "text-accent font-mono text-xs" ] [ str memberModel.ReturnType ]
                ]

                (if not memberModel.Examples.IsEmpty then
                    div [] [
                        h4 [ _class "text-[10px] font-black uppercase opacity-40 mb-3 tracking-widest" ] [ str "Verified Examples" ]
                        (memberModel.Examples |> List.map (fun ex ->
                            div [ _class "mb-4" ] [
                                if ex.Name <> "Example" then p [ _class "text-[10px] font-bold mb-1 opacity-50 uppercase" ] [ str ex.Name ]
                                pre [ _class "bg-neutral text-neutral-content p-4 rounded-xl text-xs font-mono overflow-x-auto border-0" ] [
                                    code [ _class "language-fsharp" ] [ str ex.Content ]
                                ]
                            ]
                        ) |> div [])
                    ]
                else emptyText)
            ]
        ]

    let layout (pageTitle: string) (pages: ContentPage list) (package: PackageModel) (versions: string list) (theme: string) (rootPath: string) (content: XmlNode list) =
        html [ _lang "en"; attr "data-theme" theme; _class "scroll-smooth" ] [
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/daisyui@4.10.2/dist/full.min.css" ]
                script [ _src "https://cdn.tailwindcss.com?plugins=typography" ] []
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css" ]
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css" ]
                style [] [ str """
                    .drawer-side::-webkit-scrollbar { width: 4px; }
                    .drawer-side::-webkit-scrollbar-thumb { background: rgba(0,0,0,0.1); border-radius: 10px; }
                    .prose pre { background: transparent !important; padding: 0 !important; }
                    .prose pre code { background: transparent !important; border: 0 !important; }
                    code { font-family: 'Fira Code', monospace; }
                    summary::-webkit-details-marker { display: none; }
                """ ]
                title [] [ str $"{pageTitle} - FsLiveDocs" ]
            ]
            body [ _class "min-h-screen bg-base-200/50 flex flex-col" ] [
                // Navbar
                div [ _class "navbar bg-base-100 border-b border-base-300 sticky top-0 z-[100] h-16 shadow-sm" ] [
                    div [ _class "flex-none lg:hidden" ] [
                        label [ _for "my-drawer-2"; _class "btn btn-square btn-ghost" ] [
                            i [ _class "bi bi-list text-xl" ] []
                        ]
                    ]
                    div [ _class "flex-1 px-4" ] [
                        a [ _href (rootPath + "index.html"); _class "flex items-center gap-2 no-underline" ] [ 
                            div [ _class "bg-primary text-primary-content w-8 h-8 rounded-lg flex items-center justify-center font-black" ] [ str "Fs" ]
                            span [ _class "text-lg font-black tracking-tight" ] [ str "LiveDocs" ] 
                        ]
                    ]
                    div [ _class "flex-none hidden lg:flex px-4" ] [
                        ul [ _class "menu menu-horizontal px-1 gap-2 font-bold" ] [
                            navItem "Home" (rootPath + "index.html")
                            navItem "API" (rootPath + "api.html")
                            li [] [
                                details [ _class "dropdown dropdown-end" ] [
                                    summary [ _class "px-4 py-2 hover:bg-base-200 rounded-lg cursor-pointer transition-colors" ] [ 
                                        str (if versions.IsEmpty then "v0.1.0" else versions |> List.head) 
                                    ]
                                    ul [ _class "p-2 bg-base-100 rounded-box shadow-xl border border-base-300 w-40 mt-2" ] (
                                        versions |> List.map (fun v -> 
                                            // history links are special, they go to root/history/v/index.html
                                            li [] [ a [ _href (rootPath + "history/" + v + "/index.html") ] [ str v ] ]
                                        )
                                    )
                                ]
                            ]
                        ]
                        div [ _class "divider divider-horizontal mx-0" ] []
                        div [ _class "dropdown dropdown-end ml-2" ] [
                            label [ attr "tabindex" "0"; _class "btn btn-ghost btn-sm btn-circle" ] [
                                i [ _class "bi bi-palette text-lg" ] []
                            ]
                            ul [ attr "tabindex" "0"; _class "dropdown-content z-[110] menu p-2 shadow-2xl bg-base-100 rounded-box w-52 mt-4 border border-base-300" ] [
                                li [] [ a [ attr "data-set-theme" "light" ] [ str "☀️ Light" ] ]
                                li [] [ a [ attr "data-set-theme" "dark" ] [ str "🌙 Dark" ] ]
                                li [] [ a [ attr "data-set-theme" "cupcake" ] [ str "🧁 Cupcake" ] ]
                                li [] [ a [ attr "data-set-theme" "dracula" ] [ str "🧛 Dracula" ] ]
                                li [] [ a [ attr "data-set-theme" "emerald" ] [ str "✳️ Emerald" ] ]
                            ]
                        ]
                    ]
                ]

                div [ _class "flex-1 flex overflow-hidden" ] [
                    div [ _class "drawer lg:drawer-open" ] [
                        input [ _id "my-drawer-2"; _type "checkbox"; _class "drawer-toggle" ]
                        div [ _class "drawer-content flex flex-col h-full overflow-y-auto" ] [
                            div [ _class "max-w-7xl w-full mx-auto p-6 lg:p-12" ] [
                                div [ _class "grid grid-cols-1 xl:grid-cols-4 gap-12" ] [
                                    main [ _class "xl:col-span-3 prose prose-sm md:prose-base max-w-none bg-base-100 p-8 md:p-12 rounded-3xl shadow-sm border border-base-300 min-h-[70vh]" ] [
                                        div [ _id "search-ui"; _class "not-prose mb-12" ] []
                                        yield! content
                                    ]
                                    aside [ _class "hidden xl:block" ] [
                                        div [ _class "sticky top-28" ] [
                                            h4 [ _class "text-[10px] font-black uppercase mb-4 opacity-40 tracking-widest" ] [ str "On This Page" ]
                                            ul [ _id "on-this-page"; _class "menu menu-xs opacity-70 border-l-2 border-base-300 gap-1 ps-4" ] []
                                        ]
                                    ]
                                ]
                            ]
                        ] 
                        div [ _class "drawer-side z-50 h-full sticky top-16" ] [
                            label [ _for "my-drawer-2"; _class "drawer-overlay" ] []
                            div [ _class "bg-base-100 w-72 h-[calc(100vh-4rem)] border-r border-base-300 overflow-y-auto p-6 shadow-sm" ] [
                                sidebar rootPath pages package
                            ]
                        ]
                    ]
                ]

                script [ _src (rootPath + "_pagefind/pagefind-ui.js") ] []
                script [] [ rawText "window.addEventListener('DOMContentLoaded', (event) => { if(typeof PagefindUI !== 'undefined') new PagefindUI({ element: '#search-ui', showSubResults: true }); });" ]
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-fsharp.min.js" ] []
                script [] [ rawText "document.querySelectorAll('[data-set-theme]').forEach(el => el.addEventListener('click', () => { document.documentElement.setAttribute('data-theme', el.getAttribute('data-set-theme')); localStorage.setItem('theme', el.getAttribute('data-set-theme')); }))" ]
                script [] [ rawText "const t = localStorage.getItem('theme'); if(t) document.documentElement.setAttribute('data-theme', t);" ]
                // Dynamic On-This-Page Generator
                script [] [ rawText """
                    window.addEventListener('DOMContentLoaded', () => {
                        const headings = Array.from(document.querySelectorAll('main h2, main h3'));
                        const toc = document.getElementById('on-this-page');
                        if (toc && headings.length > 0) {
                            headings.forEach(h => {
                                if (!h.id) h.id = h.innerText.toLowerCase().replace(/\s+/g, '-');
                                const li = document.createElement('li');
                                const a = document.createElement('a');
                                a.href = '#' + h.id;
                                a.innerText = h.innerText;
                                if (h.tagName === 'H3') a.className = 'pl-4';
                                li.appendChild(a);
                                toc.appendChild(li);
                            });
                        }
                    });
                """ ]
            ]
        ]
