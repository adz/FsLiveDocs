namespace FsLiveDocs.Renderer

open Giraffe.ViewEngine
open FsLiveDocs.Core
open System
open System.IO

/// <summary>Centralized URL and path resolution helpers.</summary>
module Url =
    /// <summary>Ensures the root path has a trailing slash if not empty.</summary>
    let ensureTrailing (path: string) =
        if String.IsNullOrEmpty(path) then ""
        elif path.EndsWith("/") then path
        else path + "/"

    /// <summary>Resolves a link relative to the provided rootPath.</summary>
    let resolve (rootPath: string) (target: string) =
        (ensureTrailing rootPath) + target

/// <summary>Contains the view components and layout templates for the documentation site.</summary>
module View =

    let navItem (title: string) (url: string) =
        li [] [ a [ _href url; _class "hover:text-primary transition-colors px-4 py-2 font-bold" ] [ str title ] ]

    let sidebarPageLink (rootPath: string) (p: ContentPage) =
        let fileName = Path.GetFileNameWithoutExtension(p.FilePath).ToLower() + ".html"
        li [] [ a [ _href (Url.resolve rootPath fileName); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm transition-all" ] [ str p.Metadata.Title ] ]

    let rec sidebarEntityLink (rootPath: string) (e: EntityModel) =
        li [] [
            if e.Entities.IsEmpty then
                a [ _href (Url.resolve rootPath ("api/" + e.Id + ".html")); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm" ] [ str e.Name ]
            else
                details [ _class "group"; attr "open" "false" ] [
                    summary [ _class "flex items-center justify-between py-2 px-4 hover:bg-base-300 rounded-lg cursor-pointer list-none" ] [
                        span [ _class "text-sm font-medium truncate" ] [ 
                            a [ _href (Url.resolve rootPath ("api/" + e.Id + ".html")); _class "hover:link" ] [ str e.Name ]
                        ]
                        i [ _class "bi bi-chevron-right text-[10px] transition-transform group-open:rotate-90" ] []
                    ]
                    ul [ _class "menu menu-sm p-0 mt-1 ml-2 border-l border-base-300" ] (e.Entities |> List.map (sidebarEntityLink rootPath))
                ]
        ]

    let sidebar (rootPath: string) (pages: ContentPage list) (package: PackageModel) =
        let apiGroups = 
            package.Entities 
            |> List.groupBy (fun e -> 
                let parts = e.Id.Split('.')
                if parts.Length > 1 then parts.[0] else "Default"
            )

        div [ _class "flex flex-col gap-10 pb-32" ] [
            // Guides Section
            div [] [
                h3 [ _class "text-[11px] font-black uppercase text-base-content px-4 mb-4 tracking-[0.2em] opacity-50" ] [ str "Guides" ]
                ul [ _class "menu menu-sm p-0 gap-1" ] (pages |> List.sortBy (fun p -> p.Metadata.Weight) |> List.map (sidebarPageLink rootPath))
            ]
            // API Reference Section
            div [] [
                h3 [ _class "text-[11px] font-black uppercase text-base-content px-4 mb-4 tracking-[0.2em] opacity-50" ] [ str "API Reference" ]
                ul [ _class "menu menu-sm p-0 gap-2" ] (
                    apiGroups |> List.map (fun (area, entities) ->
                        li [] [
                            details [ _class "group"; attr "open" "true" ] [
                                summary [ _class "flex items-center justify-between py-2 px-4 text-primary font-black hover:bg-base-300 rounded-lg cursor-pointer list-none uppercase tracking-widest text-[10px]" ] [
                                    str area
                                    i [ _class "bi bi-chevron-down text-[8px] transition-transform group-open:rotate-180" ] []
                                ]
                                ul [ _class "menu menu-sm p-0 mt-2 gap-1 border-l-2 border-primary/10 ml-4" ] (entities |> List.map (sidebarEntityLink rootPath))
                            ]
                        ]
                    )
                )
            ]
        ]

    let apiCard (memberModel: MemberModel) =
        div [ _class "card bg-base-100 shadow-sm border border-base-300 overflow-hidden mb-12 hover:shadow-lg transition-all duration-300 group/card"; _id memberModel.Id ] [
            div [ _class "bg-base-200/30 px-6 py-4 border-b border-base-300 flex justify-between items-center group-hover/card:bg-base-200/50 transition-colors" ] [
                code [ _class "text-xs font-mono text-primary bg-primary/5 px-3 py-1.5 rounded-full border border-primary/10 shadow-inner" ] [ str memberModel.Signature ]
                span [ _class "text-[10px] font-black uppercase opacity-30 tracking-widest" ] [ str "Member" ]
            ]
            div [ _class "p-8 md:p-12" ] [
                h2 [ _class "text-3xl font-black mb-6 tracking-tighter group-hover/card:text-primary transition-colors h-anchor" ] [ str memberModel.Name ]
                
                div [ _class "prose prose-sm md:prose-base max-w-none mb-10 opacity-80 leading-relaxed" ] [ rawText memberModel.SummaryHtml ]

                (if not memberModel.Parameters.IsEmpty then
                    div [ _class "mb-10" ] [
                        h4 [ _class "text-[11px] font-black uppercase opacity-40 mb-4 tracking-widest flex items-center gap-2" ] [ 
                            i [ _class "bi bi-list-nested" ] []; str "Parameters" 
                        ]
                        div [ _class "overflow-x-auto rounded-3xl border border-base-300 shadow-2xl shadow-base-300/20" ] [
                            table [ _class "table table-md table-zebra w-full" ] [
                                thead [ _class "bg-base-200/50" ] [ tr [] [ th [ _class "px-6" ] [ str "Name" ]; th [] [ str "Type" ]; th [] [ str "Description" ] ] ]
                                tbody [] (memberModel.Parameters |> List.map (fun p ->
                                    tr [] [
                                        td [ _class "font-bold font-mono text-sm text-primary px-6" ] [ str p.Name ]
                                        td [] [ code [ _class "text-secondary text-xs bg-secondary/5 px-2 py-0.5 rounded" ] [ str p.Type ] ]
                                        td [ _class "text-sm opacity-80 leading-relaxed" ] [ rawText p.DescriptionHtml ]
                                    ]
                                ))
                            ]
                        ]
                    ]
                else emptyText)

                div [ _class "mb-10 flex items-center gap-6 p-6 bg-base-200/20 rounded-3xl border border-base-300 shadow-inner" ] [
                    h4 [ _class "text-[11px] font-black uppercase opacity-40 tracking-widest" ] [ str "Returns" ]
                    code [ _class "text-accent font-mono text-sm font-black bg-accent/5 px-4 py-1.5 rounded-xl border border-accent/10" ] [ str memberModel.ReturnType ]
                ]

                (if not memberModel.Examples.IsEmpty then
                    div [] [
                        h4 [ _class "text-[11px] font-black uppercase opacity-40 mb-4 tracking-widest flex items-center gap-2" ] [ 
                             i [ _class "bi bi-play-circle-fill text-primary/60" ] []; str "Verification Examples" 
                        ]
                        (memberModel.Examples |> List.map (fun ex ->
                            div [ _class "mb-8" ] [
                                if ex.Name <> "Example" then p [ _class "text-[10px] font-black mb-2 opacity-50 uppercase tracking-[0.2em]" ] [ str ex.Name ]
                                pre [ _class "bg-neutral text-neutral-content p-8 rounded-[2rem] text-sm font-mono overflow-x-auto border-0 shadow-2xl shadow-black/20" ] [
                                    code [ _class "language-fsharp" ] [ str ex.Content ]
                                ]
                            ]
                        ) |> div [])
                    ]
                else emptyText)
            ]
        ]

    let layout (pageTitle: string) (pages: ContentPage list) (package: PackageModel) (versions: string list) (theme: string) (rootPath: string) (content: XmlNode list) =
        let safeRoot = Url.ensureTrailing rootPath
        html [ _lang "en"; attr "data-theme" theme; _class "scroll-smooth" ] [
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/daisyui@4.10.2/dist/full.min.css" ]
                script [ _src "https://cdn.tailwindcss.com?plugins=typography" ] []
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css" ]
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css" ]
                style [] [ str """
                    @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;700;900&family=Fira+Code:wght@400;700&display=swap');
                    body { font-family: 'Inter', sans-serif; }
                    .drawer-side::-webkit-scrollbar { width: 4px; }
                    .drawer-side::-webkit-scrollbar-thumb { background: rgba(0,0,0,0.1); border-radius: 10px; }
                    .prose pre { background: transparent !important; padding: 0 !important; }
                    .prose pre code { background: transparent !important; border: 0 !important; }
                    code { font-family: 'Fira Code', monospace; }
                    summary::-webkit-details-marker { display: none; }
                """ ]
                title [] [ str $"{pageTitle} - FsLiveDocs" ]
            ]
            body [ _class "min-h-screen bg-base-200/30 flex flex-col" ] [
                // Navbar
                div [ _class "navbar bg-base-100 border-b border-base-300 sticky top-0 z-[100] h-20 shadow-sm px-4 md:px-8" ] [
                    div [ _class "flex-none lg:hidden" ] [
                        label [ _for "my-drawer-2"; _class "btn btn-square btn-ghost" ] [
                            i [ _class "bi bi-list text-2xl" ] []
                        ]
                    ]
                    div [ _class "flex-1" ] [
                        a [ _href (Url.resolve safeRoot "index.html"); _class "flex items-center gap-3 no-underline group" ] [ 
                            div [ _class "bg-primary text-primary-content w-12 h-12 rounded-2xl flex items-center justify-center font-black text-xl shadow-xl shadow-primary/20 group-hover:rotate-12 transition-transform" ] [ str "Fs" ]
                            span [ _class "text-2xl font-black tracking-tighter" ] [ str "LiveDocs" ] 
                        ]
                    ]
                    div [ _class "flex-none hidden lg:flex" ] [
                        ul [ _class "menu menu-horizontal px-1 gap-6" ] [
                            navItem "Home" (Url.resolve safeRoot "index.html")
                            navItem "API" (Url.resolve safeRoot "api.html")
                            li [] [
                                details [ _class "dropdown dropdown-end" ] [
                                    summary [ _class "px-8 py-3 bg-base-200 hover:bg-base-300 rounded-2xl cursor-pointer transition-all font-black text-xs uppercase tracking-widest" ] [ 
                                        str (if versions.IsEmpty then "v0.1.0" else versions |> List.head) 
                                    ]
                                    ul [ _class "p-3 bg-base-100 rounded-[2rem] shadow-2xl border border-base-300 w-56 mt-4" ] (
                                        versions |> List.map (fun v -> 
                                            li [] [ a [ _href (Url.resolve safeRoot ("history/" + v + "/index.html")); _class "py-4 rounded-xl font-bold" ] [ str v ] ]
                                        )
                                    )
                                ]
                            ]
                        ]
                        div [ _class "divider divider-horizontal mx-6 opacity-30" ] []
                        div [ _class "dropdown dropdown-end" ] [
                            label [ attr "tabindex" "0"; _class "btn btn-ghost btn-md btn-circle bg-base-200 hover:bg-base-300 shadow-sm" ] [
                                i [ _class "bi bi-palette-fill text-lg" ] []
                            ]
                            ul [ attr "tabindex" "0"; _class "dropdown-content z-[110] menu p-4 shadow-2xl bg-base-100 rounded-[2rem] w-64 mt-4 border border-base-300 grid grid-cols-2 gap-2" ] [
                                [ "light"; "dark"; "cupcake"; "dracula"; "emerald"; "corporate"; "retro"; "cyberpunk" ]
                                |> List.map (fun t -> li [] [ a [ attr "data-set-theme" t; _class "text-[10px] font-black uppercase tracking-wider h-10 flex items-center justify-center rounded-xl hover:bg-base-200" ] [ str t ] ])
                                |> div [ _class "contents" ]
                            ]
                        ]
                    ]
                ]

                div [ _class "flex-1 flex overflow-hidden" ] [
                    div [ _class "drawer lg:drawer-open" ] [
                        input [ _id "my-drawer-2"; _type "checkbox"; _class "drawer-toggle" ]
                        div [ _class "drawer-content flex flex-col h-full overflow-y-auto" ] [
                            div [ _class "max-w-7xl w-full mx-auto p-6 md:p-12 lg:p-16" ] [
                                div [ _class "grid grid-cols-1 xl:grid-cols-4 gap-16" ] [
                                    main [ _class "xl:col-span-3 prose prose-base md:prose-lg max-w-none bg-base-100 p-10 md:p-20 rounded-[3rem] shadow-2xl shadow-base-300/40 border border-base-300 min-h-[85vh]" ] [
                                        div [ _id "search-ui"; _class "not-prose mb-20 shadow-inner rounded-3xl overflow-hidden" ] []
                                        yield! content
                                    ]
                                    aside [ _class "hidden xl:block" ] [
                                        div [ _class "sticky top-36" ] [
                                            h4 [ _class "text-[10px] font-black uppercase mb-8 opacity-30 tracking-[0.3em]" ] [ str "On This Page" ]
                                            ul [ _id "on-this-page"; _class "menu menu-sm opacity-80 border-l-4 border-primary/10 gap-3 ps-8" ] []
                                        ]
                                    ]
                                ]
                            ]
                        ] 
                        div [ _class "drawer-side z-50 h-full sticky top-20" ] [
                            label [ _for "my-drawer-2"; _class "drawer-overlay" ] []
                            div [ _class "bg-base-100 w-80 h-[calc(100vh-5rem)] border-r border-base-300 overflow-y-auto p-10 shadow-sm transition-all" ] [
                                sidebar safeRoot pages package
                            ]
                        ]
                    ]
                ]

                script [ _src (Url.resolve safeRoot "_pagefind/pagefind-ui.js") ] []
                script [] [ rawText "window.addEventListener('DOMContentLoaded', (event) => { if(typeof PagefindUI !== 'undefined') new PagefindUI({ element: '#search-ui', showSubResults: true, bundlePath: '/' }); });" ]
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-fsharp.min.js" ] []
                script [] [ rawText "document.querySelectorAll('[data-set-theme]').forEach(el => el.addEventListener('click', () => { const t = el.getAttribute('data-set-theme'); document.documentElement.setAttribute('data-theme', t); localStorage.setItem('theme', t); }))" ]
                script [] [ rawText "const t = localStorage.getItem('theme'); if(t) document.documentElement.setAttribute('data-theme', t);" ]
                // Dynamic On-This-Page Generator
                script [] [ rawText """
                    window.addEventListener('DOMContentLoaded', () => {
                        const headings = Array.from(document.querySelectorAll('main h1, main h2, main h3')).filter(h => h.id !== 'search-ui');
                        const toc = document.getElementById('on-this-page');
                        if (toc && headings.length > 0) {
                            headings.forEach(h => {
                                // Extract clean text ignoring badges/metadata
                                let cleanText = Array.from(h.childNodes)
                                    .filter(node => node.nodeType === Node.TEXT_NODE)
                                    .map(node => node.textContent.trim())
                                    .join(' ');
                                if (!cleanText) cleanText = h.innerText;

                                if (!h.id) h.id = cleanText.toLowerCase().replace(/[^\w]+/g, '-');
                                
                                const li = document.createElement('li');
                                const a = document.createElement('a');
                                a.href = '#' + h.id;
                                a.innerText = cleanText;
                                a.className = 'hover:text-primary transition-colors py-1 block text-sm opacity-60 hover:opacity-100';
                                if (h.tagName === 'H3') a.className += ' pl-4 text-xs';
                                if (h.tagName === 'H2') a.className += ' font-bold';
                                if (h.tagName === 'H1') a.className += ' font-black text-base text-primary mb-4 border-b-2 border-primary/10 pb-2';
                                li.appendChild(a);
                                toc.appendChild(li);
                            });
                        }
                    });
                """ ]
            ]
        ]
