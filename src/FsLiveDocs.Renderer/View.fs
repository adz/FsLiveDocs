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

    let private escapeJs (value: string) =
        value.Replace("\\", "\\\\").Replace("'", "\\'")

    let sourceLinkHref (repoUrl: string option) (location: SourceLink) =
        match repoUrl with
        | Some repo when not (String.IsNullOrWhiteSpace repo) && not (String.IsNullOrWhiteSpace location.File) && location.Line > 0 ->
            Some $"{repo.TrimEnd('/')}/blob/main/{location.File}#L{location.Line}"
        | _ -> None

    let private anchorIcon (href: string) (label: string) =
        a [
            _href href
            _class "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
            attr "aria-label" label
            attr "title" label
        ] [
            i [ _class "bi bi-link-45deg text-base" ] []
        ]

    let h1WithAnchor (id: string) (title: string) (classes: string) =
        h1 [ _id id; attr "data-toc-title" title; _class (classes + " group scroll-mt-24 flex items-center gap-3") ] [
            span [] [ str title ]
            anchorIcon ("#" + id) $"Copy link to {title}"
        ]

    let h2WithAnchor (id: string) (title: string) (classes: string) =
        h2 [ _id id; attr "data-toc-title" title; _class (classes + " group scroll-mt-24 flex items-center gap-3") ] [
            span [] [ str title ]
            anchorIcon ("#" + id) $"Copy link to {title}"
        ]

    let h3WithAnchor (id: string) (title: string) (classes: string) =
        h3 [ _id id; attr "data-toc-title" title; _class (classes + " group scroll-mt-24 flex items-center gap-3") ] [
            span [] [ str title ]
            anchorIcon ("#" + id) $"Copy link to {title}"
        ]

    let navItem (title: string) (url: string) =
        li [] [ a [ _href url; _class "hover:text-primary transition-colors px-4 py-2 font-bold" ] [ str title ] ]

    let private docsSectionKey (page: ContentPage) =
        let directory = Path.GetDirectoryName(page.OutputPath)
        if String.IsNullOrWhiteSpace directory then
            "overview"
        else
            directory.Replace('\\', '/').TrimStart('/').Split('/').[0]

    let private docsSectionLabel (groupKey: string) (items: ContentPage list) =
        items
        |> List.tryFind (fun page -> Path.GetFileName(page.FilePath).Equals("_index.md", StringComparison.OrdinalIgnoreCase))
        |> Option.map (fun page -> page.Metadata.Title)
        |> Option.defaultWith (fun () ->
            groupKey.Split('-')
            |> Array.map (fun part -> part.Substring(0, 1).ToUpperInvariant() + part.Substring(1))
            |> String.concat " ")

    let sidebarPageLink (rootPath: string) (p: ContentPage) =
        li [ attr "data-sidebar-item" "true" ] [ a [ _href (Url.resolve rootPath p.OutputPath); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm transition-all" ] [ str p.Metadata.Title ] ]

    let rec sidebarEntityLink (rootPath: string) (e: EntityModel) =
        li [ attr "data-sidebar-item" "true" ] [
            if e.Entities.IsEmpty then
                a [ _href (Url.resolve rootPath ("api/" + e.Id + ".html")); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm" ] [ str e.Name ]
            else
                details [ _class "group"; attr "open" "false" ] [
                    summary [ _class "flex items-center justify-between py-2 px-4 hover:bg-base-300 rounded-lg cursor-pointer list-none" ] [
                        a [
                            _href (Url.resolve rootPath ("api/" + e.Id + ".html"))
                            attr "onclick" "event.stopPropagation();"
                            _class "flex-1 min-w-0 text-sm font-medium truncate hover:link"
                        ] [
                            str e.Name
                        ]
                        button [
                            _type "button"
                            attr "onclick" "const d = this.closest('details'); if (d) d.open = !d.open; event.preventDefault(); event.stopPropagation();"
                            _class "ml-2 shrink-0"
                            attr "aria-label" $"Toggle {e.Name}"
                        ] [
                            i [ _class "bi bi-chevron-right text-[10px] transition-transform group-open:rotate-90" ] []
                        ]
                    ]
                    ul [ _class "menu menu-sm p-0 mt-1 ml-2 border-l border-base-300" ] (e.Entities |> List.map (sidebarEntityLink rootPath))
                ]
        ]

    let sidebar (rootPath: string) (pages: ContentPage list) (package: PackageModel) =
        let apiGroups = 
            package.Entities 
            |> List.groupBy (fun e -> e.Id.Split('.').[0])
            |> List.sortBy fst

        let docsGroups =
            pages
            |> List.filter (fun p -> p.OutputPath <> "index.html")
            |> List.sortBy (fun p -> p.SectionOrder, docsSectionKey p, p.Metadata.Weight, p.Metadata.Title)
            |> List.groupBy docsSectionKey
            |> List.sortBy (fun (groupKey, items) -> items |> List.map (fun page -> page.SectionOrder) |> List.min, groupKey)

        div [ _class "flex flex-col gap-10 pb-32"; _id "sidebar-root" ] [
            div [ _class "sticky top-0 z-10 bg-base-100/95 backdrop-blur border-b border-base-300 -mx-10 px-10 pb-6 pt-2" ] [
                label [ _for "sidebar-filter"; _class "text-[10px] font-black uppercase tracking-[0.3em] opacity-40 block mb-3" ] [ str "Filter" ]
                input [
                    _id "sidebar-filter"
                    _type "search"
                    _placeholder "Filter guides and API"
                    _class "input input-bordered input-sm w-full rounded-2xl"
                    attr "autocomplete" "off"
                ]
            ]
            div [ attr "data-sidebar-section" "true" ] [
                h3WithAnchor "overview" "Overview" "text-[11px] font-black uppercase text-base-content px-4 mb-4 tracking-[0.2em] opacity-50"
                ul [ _class "menu menu-sm p-0 gap-1" ] [
                    li [ attr "data-sidebar-item" "true" ] [ a [ _href (Url.resolve rootPath "index.html"); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm transition-all" ] [ str "Home" ] ]
                    li [ attr "data-sidebar-item" "true" ] [ a [ _href (Url.resolve rootPath "api.html"); _class "py-2 px-4 hover:bg-base-300 rounded-lg block text-sm transition-all" ] [ str "API Reference" ] ]
                ]
            ]
            // Docs Sections
            div [ attr "data-sidebar-section" "true" ] [
                h3WithAnchor "docs" "Docs" "text-[11px] font-black uppercase text-base-content px-4 mb-4 tracking-[0.2em] opacity-50"
                ul [ _class "menu menu-sm p-0 gap-2" ] (
                    docsGroups
                    |> List.map (fun (groupKey, items) ->
                        if groupKey = "overview" then
                            li [ attr "data-sidebar-item" "true" ] [
                                ul [ _class "menu menu-sm p-0 gap-1" ] (items |> List.map (sidebarPageLink rootPath))
                            ]
                        else
                            li [ attr "data-sidebar-item" "true" ] [
                                details [ _class "group"; attr "open" "true" ] [
                                    summary [ _class "flex items-center justify-between py-2 px-4 hover:bg-base-300 rounded-lg cursor-pointer list-none font-black text-[10px] uppercase tracking-[0.2em] opacity-70" ] [
                                        span [] [ str (docsSectionLabel groupKey items) ]
                                        i [ _class "bi bi-chevron-down text-[8px] transition-transform group-open:rotate-180" ] []
                                    ]
                                    ul [ _class "menu menu-sm p-0 mt-2 ml-2 border-l border-base-300" ] (items |> List.map (sidebarPageLink rootPath))
                                ]
                            ]
                    )
                )
            ]
            // API Reference Section
            div [ attr "data-sidebar-section" "true" ] [
                h3WithAnchor "api-reference" "API Reference" "text-[11px] font-black uppercase text-base-content px-4 mb-4 tracking-[0.2em] opacity-50"
                ul [ _class "menu menu-sm p-0 gap-2" ] (
                    apiGroups |> List.map (fun (area, entities) ->
                        let entitiesToRender = 
                            // If we have a single namespace root that matches the area name, and it has children,
                            // promote children to the top level of this group to avoid "Namespace > Namespace" redundancy.
                            match entities with
                            | [ e ] when e.Kind = EntityKind.Namespace && e.Name.Equals(area, StringComparison.OrdinalIgnoreCase) && not e.Entities.IsEmpty ->
                                e.Entities
                            | _ -> entities

                        let areaPageId =
                            entities
                            |> List.tryFind (fun e -> e.Kind = EntityKind.Namespace && e.Name.Equals(area, StringComparison.OrdinalIgnoreCase))
                            |> Option.map (fun e -> e.Id)
                            |> Option.defaultValue area

                        li [ attr "data-sidebar-item" "true" ] [
                            details [ _class "group"; attr "open" "true" ] [
                                summary [ _class "flex items-center justify-between py-2 px-4 text-primary font-black hover:bg-base-300 rounded-lg cursor-pointer list-none uppercase tracking-widest text-[10px]" ] [
                                    a [
                                        _href (Url.resolve rootPath ("api/" + areaPageId + ".html"))
                                        attr "onclick" "event.stopPropagation();"
                                        _class "flex-1 truncate hover:link"
                                    ] [
                                        str area
                                    ]
                                    i [ _class "bi bi-chevron-down text-[8px] transition-transform group-open:rotate-180" ] []
                                ]
                                ul [ _class "menu menu-sm p-0 mt-2 gap-1 border-l-2 border-primary/10 ml-4" ] (entitiesToRender |> List.map (sidebarEntityLink rootPath))
                            ]
                        ]
                    )
                )
            ]
        ]

    let apiCard (repoUrl: string option) (memberModel: MemberModel) =
        let sourceLink =
            sourceLinkHref repoUrl memberModel.Location
            |> Option.map (fun href ->
                a [
                    _href href
                    _target "_blank"
                    attr "rel" "noreferrer"
                    _class "btn btn-ghost btn-sm btn-circle border border-base-300 text-base-content/50 hover:text-primary hover:border-primary/30"
                    attr "aria-label" $"View source for {memberModel.Name}"
                    attr "title" $"View source for {memberModel.Name}"
                ] [ i [ _class "bi bi-code-slash" ] [] ])

        div [ _class "border border-base-300 rounded-2xl bg-base-100 overflow-hidden" ] [
            div [ _class "bg-base-200/20 px-4 py-3 border-b border-base-300 flex flex-col gap-3" ] [
                div [ _class "flex items-start justify-between gap-4" ] [
                    h3 [ _id memberModel.Id; attr "data-toc-title" memberModel.Name; _class "group text-xl font-black tracking-tight flex items-center gap-3 scroll-mt-24" ] [
                        span [ _class "leading-tight" ] [ str memberModel.Name ]
                        anchorIcon ("#" + memberModel.Id) $"Copy link to {memberModel.Name}"
                    ]
                    match sourceLink with
                    | Some link -> link
                    | None -> emptyText
                ]
                div [ _class "text-xs font-mono text-primary bg-primary/5 px-3 py-1.5 rounded-full border border-primary/10 shadow-inner overflow-x-auto max-w-full block" ] [
                    rawText (Presentation.highlightSignatureHtml memberModel.Signature)
                ]
                span [ _class "text-[10px] font-black uppercase opacity-30 tracking-widest" ] [ str "Member" ]
            ]
            div [ _class "p-4 md:p-5" ] [
                div [ _class "prose prose-sm md:prose-base max-w-none mb-5 opacity-80 leading-relaxed" ] [ rawText memberModel.SummaryHtml ]

                (if not memberModel.Parameters.IsEmpty then
                    div [ _class "mb-8" ] [
                        h4 [ _class "text-[11px] font-black uppercase opacity-40 mb-4 tracking-widest flex items-center gap-2" ] [ 
                            i [ _class "bi bi-list-nested" ] []; str "Parameters" 
                        ]
                        div [ _class "overflow-x-auto rounded-xl border border-base-300 shadow-sm not-prose" ] [
                            table [ _class "table table-sm table-zebra w-full" ] [
                                thead [ _class "bg-base-200/50" ] [
                                    tr [] [
                                        th [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.625rem !important; padding-bottom: 0.625rem !important;" ] [ str "Name" ]
                                        th [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.625rem !important; padding-bottom: 0.625rem !important;" ] [ str "Type" ]
                                        th [ attr "style" "padding-right: 1.5rem !important; padding-top: 0.625rem !important; padding-bottom: 0.625rem !important;" ] [ str "Description" ]
                                    ]
                                ]
                                tbody [] (memberModel.Parameters |> List.map (fun p ->
                                    tr [] [
                                        td [ _class "font-bold font-mono text-sm text-primary"; attr "style" "padding-left: 1.5rem !important; padding-top: 0.5rem !important; padding-bottom: 0.5rem !important; vertical-align: top !important;" ] [ str p.Name ]
                                        td [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.5rem !important; padding-bottom: 0.5rem !important; vertical-align: top !important;" ] [ span [ _class "text-secondary text-xs bg-secondary/5 px-2 py-0.5 rounded" ] [ rawText (Presentation.highlightSignatureHtml p.Type) ] ]
                                        td [ _class "text-sm opacity-80 leading-relaxed"; attr "style" "padding-right: 1.5rem !important; padding-top: 0.5rem !important; padding-bottom: 0.5rem !important; vertical-align: top !important;" ] [ rawText p.DescriptionHtml ]
                                    ]
                                ))
                             ]
                        ]
                    ]
                else emptyText)

                div [ _class "mb-8 p-4 bg-base-200/15 rounded-xl border border-base-300 flex flex-col gap-3" ] [
                    h4 [ _class "text-[11px] font-black uppercase opacity-40 tracking-widest flex items-center gap-2" ] [ 
                        i [ _class "bi bi-arrow-return-right text-accent/60" ] []; str "Returns" 
                    ]
                    div [ _class "text-accent font-mono text-sm font-black bg-accent/5 px-4 py-2.5 rounded-xl border border-accent/10 overflow-x-auto w-full" ] [ rawText (Presentation.highlightSignatureHtml memberModel.ReturnType) ]
                ]

                (if not memberModel.Examples.IsEmpty then
                    div [ _class "not-prose" ] [
                        h4 [ _class "text-[11px] font-black uppercase opacity-40 mb-4 tracking-widest flex items-center gap-2" ] [ 
                             i [ _class "bi bi-play-circle-fill text-primary/60" ] []; str "Verification Examples" 
                        ]
                        (memberModel.Examples |> List.map (fun ex ->
                            div [ _class "mb-6" ] [
                                if ex.Name <> "Example" then p [ _class "text-[10px] font-black mb-2 opacity-50 uppercase tracking-[0.2em]" ] [ str ex.Name ]
                                pre [ _class "bg-neutral text-neutral-content p-5 rounded-2xl text-sm font-mono overflow-x-auto border-0 shadow-sm" ] [
                                    code [ _class "language-fsharp" ] [ str ex.Content ]
                                ]
                            ]
                        ) |> div [])
                    ]
                else emptyText)
            ]
        ]

    let layout (pageTitle: string) (pages: ContentPage list) (package: PackageModel) (config: SiteConfig) (versions: string list) (theme: string) (rootPath: string) (content: XmlNode list) =
        let safeRoot = Url.ensureTrailing rootPath
        let homeHref = Url.resolve safeRoot "index.html"
        let apiHref = Url.resolve safeRoot "api.html"
        let siteName = config.SiteName |> Option.filter (not << String.IsNullOrWhiteSpace) |> Option.defaultValue "FsLiveDocs"
        let logoText =
            config.LogoText
            |> Option.filter (not << String.IsNullOrWhiteSpace)
            |> Option.defaultWith (fun () -> if siteName.Length <= 2 then siteName else siteName.Substring(0, 2))
        let navigation =
            config.Navigation
            |> Option.filter (not << List.isEmpty)
            |> Option.defaultValue [ { Label = "Home"; Href = "index.html" }; { Label = "API"; Href = "api.html" } ]
        let navigationHref href =
            if Uri.IsWellFormedUriString(href, UriKind.Absolute) || href.StartsWith("#") then href
            else Url.resolve safeRoot href
        html [ _lang "en"; attr "data-theme" theme; _class "scroll-smooth" ] [
            head [] [
                meta [ _charset "utf-8" ]
                meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/daisyui@4.10.2/dist/full.min.css" ]
                script [ _src "https://cdn.tailwindcss.com?plugins=typography" ] []
                link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css" ]
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css" ]
                link [ _rel "stylesheet"; _href (Url.resolve safeRoot "pagefind/pagefind-ui.css") ]
                script [] [ rawText "const theme = localStorage.getItem('theme'); if(theme) document.documentElement.setAttribute('data-theme', theme);" ]
                style [] [ str """
                    @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;700;900&family=Fira+Code:wght@400;700&display=swap');
                    body { font-family: 'Inter', sans-serif; }
                    .drawer-side::-webkit-scrollbar { width: 4px; }
                    .drawer-side::-webkit-scrollbar-thumb { background: rgba(0,0,0,0.1); border-radius: 10px; }
                    .prose pre { background: transparent !important; padding: 0 !important; }
                    .prose pre code { background: transparent !important; border: 0 !important; }
                    code { font-family: 'Fira Code', monospace; }
                    summary::-webkit-details-marker { display: none !important; }
                    summary::marker { display: none !important; }
                    summary { list-style: none !important; }
                    #search-ui {
                        --pagefind-ui-primary: hsl(var(--p));
                        --pagefind-ui-text: hsl(var(--bc));
                        --pagefind-ui-background: hsl(var(--b1));
                        --pagefind-ui-border: hsl(var(--b3));
                        --pagefind-ui-font: inherit;
                    }
                    .pagefind-ui__search-input {
                        background-color: hsl(var(--b2)) !important;
                        border: 1px solid hsl(var(--b3)) !important;
                        border-radius: 1rem !important;
                        padding: 0.75rem 1.25rem !important;
                        color: hsl(var(--bc)) !important;
                    }
                    .pagefind-ui__search-input:focus {
                        outline: 2px solid hsl(var(--p)) !important;
                        outline-offset: 1px !important;
                    }
                    .code-frame {
                        position: relative;
                    }
                    .code-frame code {
                        white-space: pre !important;
                        word-break: normal !important;
                        overflow-wrap: normal !important;
                        display: block;
                    }
                    .code-frame .code-copy-button {
                        position: absolute;
                        top: 0.5rem;
                        right: 0.5rem;
                        z-index: 2;
                    }
                    .prompt-unselectable {
                        user-select: none;
                        pointer-events: none;
                        opacity: 0.72;
                    }
                    .not-prose pre {
                        background-color: hsl(var(--n)) !important;
                        padding: 1.5rem !important;
                        padding-top: 3.75rem !important;
                    }
                """ ]
                title [] [ str $"{pageTitle} - {siteName}" ]
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
                            div [ _class "bg-primary text-primary-content w-12 h-12 rounded-2xl flex items-center justify-center font-black text-xl shadow-xl shadow-primary/20 group-hover:rotate-12 transition-transform" ] [ str logoText ]
                            span [ _class "text-2xl font-black tracking-tighter" ] [ str siteName ]
                        ]
                    ]
                    div [ _class "flex-none hidden lg:flex" ] [
                        ul [ _class "menu menu-horizontal px-1 gap-6" ] [
                            yield! navigation |> List.map (fun item -> navItem item.Label (navigationHref item.Href))
                            li [] [
                                details [ _class "dropdown dropdown-end" ] [
                                    summary [ _class "px-8 py-3 bg-base-200 hover:bg-base-300 rounded-2xl cursor-pointer transition-all font-black text-xs uppercase tracking-widest" ] [ 
                                        str (if versions.IsEmpty then "v0.1.0" else versions |> List.head) 
                                    ]
                                    ul [ _class "p-3 bg-base-100 rounded-2xl shadow-2xl border border-base-300 w-56 mt-4" ] (
                                        versions |> List.map (fun v ->
                                            let target = if not versions.IsEmpty && v = versions.Head then "index.html" else "history/" + v + "/index.html"
                                            li [] [ a [ _href (Url.resolve safeRoot target); _class "py-4 rounded-xl font-bold" ] [ str v ] ]
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
                            ul [ attr "tabindex" "0"; _class "dropdown-content z-[110] menu p-4 shadow-2xl bg-base-100 rounded-2xl w-64 mt-4 border border-base-300 grid grid-cols-2 gap-2" ] [
                                [ "light"; "dark"; "cupcake"; "dracula"; "emerald"; "corporate"; "retro"; "cyberpunk" ]
                                |> List.map (fun t -> li [] [ a [ attr "data-set-theme" t; _class "text-[10px] font-black uppercase tracking-wider h-10 flex items-center justify-center rounded-xl hover:bg-base-200" ] [ str t ] ])
                                |> div [ _class "contents" ]
                            ]
                        ]
                    ]
                ]

                div [ _class "flex-1 flex" ] [
                    div [ _class "drawer lg:drawer-open" ] [
                        input [ _id "my-drawer-2"; _type "checkbox"; _class "drawer-toggle" ]
                        div [ _class "drawer-content min-h-[calc(100vh-5rem)]" ] [
                            div [ _class "grid grid-cols-1 xl:grid-cols-[minmax(0,1fr)_14rem] gap-6 p-4 md:p-6 xl:p-8" ] [
                                div [ _class "min-w-0" ] [
                                    main [ _class "prose prose-base md:prose-lg max-w-none bg-base-100 p-6 md:p-8 rounded-xl shadow-xl shadow-base-300/30 border border-base-300 min-h-[85vh]" ] [
                                            div [ _id "search-ui"; _class "not-prose mb-12" ] []
                                            yield! content
                                    ]
                                ]
                                aside [ _class "hidden xl:block sticky top-24 self-start h-[calc(100vh-7rem)] overflow-y-auto border-l border-base-300 bg-base-200/30" ] [
                                    div [ _class "px-6 py-10" ] [
                                        h4 [ _class "text-[10px] font-black uppercase mb-8 opacity-30 tracking-[0.3em]" ] [ str "On This Page" ]
                                        ul [ _id "on-this-page"; _class "menu menu-sm opacity-80 border-l-4 border-primary/10 gap-3 ps-6" ] []
                                    ]
                                ]
                            ]
                        ] 
                        div [ _class "drawer-side z-50 lg:fixed lg:top-20 lg:bottom-0 lg:h-[calc(100vh-5rem)]" ] [
                            label [ _for "my-drawer-2"; _class "drawer-overlay" ] []
                            div [ _class "bg-base-100 w-80 h-full border-r border-base-300 overflow-y-auto p-10 shadow-sm transition-all" ] [
                                sidebar safeRoot pages package
                            ]
                        ]
                    ]
                ]

                script [ _src (Url.resolve safeRoot "pagefind/pagefind-ui.js") ] []
                script [] [ rawText "window.addEventListener('DOMContentLoaded', (event) => { if(typeof PagefindUI !== 'undefined') new PagefindUI({ element: '#search-ui', showSubResults: true }); });" ]
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js" ] []
                script [ _src "https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-fsharp.min.js" ] []
                script [] [ rawText "document.querySelectorAll('[data-set-theme]').forEach(el => el.addEventListener('click', () => { const t = el.getAttribute('data-set-theme'); document.documentElement.setAttribute('data-theme', t); localStorage.setItem('theme', t); }))" ]
                script [] [ rawText $"""
                    window.addEventListener('DOMContentLoaded', () => {{
                        const codeBlocks = Array.from(document.querySelectorAll('pre code'));
                        const escapeHtml = (text) => text
                            .replace(/&/g, '&amp;')
                            .replace(/</g, '&lt;')
                            .replace(/>/g, '&gt;')
                            .replace(/"/g, '&quot;')
                            .replace(/'/g, '&#39;');

                        codeBlocks.forEach((code, index) => {{
                            const pre = code.parentElement;
                            if (!pre || pre.dataset.enhanced === 'true') return;
                            pre.dataset.enhanced = 'true';
                            pre.classList.add('code-frame');

                            const raw = code.textContent.replace(/\r\n/g, '\n');
                            const lines = raw.split('\n');
                            const copyText = raw.replace(/^(\s*)(iex\(\d+\)>|iex>|fsi>|\.{3}>|>)\s?/gm, '$1');

                            code.innerHTML = lines.map(line => {{
                                const match = line.match(/^(\s*)(iex\(\d+\)>|iex>|fsi>|\.{3}>|>)(\s?)(.*)$/);
                                if (!match) return escapeHtml(line);
                                const [, indent, prompt, spacer, rest] = match;
                                return `${{escapeHtml(indent)}}<span class="prompt-unselectable">${{escapeHtml(prompt + spacer)}}</span>${{escapeHtml(rest)}}`;
                            }}).join('\n');

                            code.dataset.copyText = copyText;

                            if (window.Prism && typeof window.Prism.highlightElement === 'function') {{
                                window.Prism.highlightElement(code);
                            }}

                            // Prism preserves the code structure, but explicit breaks avoid whitespace collapsing
                            // in browsers that flatten newline text nodes inside enhanced code frames.
                            code.innerHTML = code.innerHTML.replace(/\n/g, '<br>');

                            const button = document.createElement('button');
                            button.type = 'button';
                            button.className = 'code-copy-button btn btn-xs btn-outline';
                            button.innerHTML = '<i class="bi bi-clipboard"></i><span class="ml-1">Copy</span>';
                            button.addEventListener('click', async () => {{
                                try {{
                                    await navigator.clipboard.writeText(code.dataset.copyText || raw);
                                    button.innerHTML = '<i class="bi bi-check2"></i><span class="ml-1">Copied</span>';
                                    setTimeout(() => {{
                                        button.innerHTML = '<i class="bi bi-clipboard"></i><span class="ml-1">Copy</span>';
                                    }}, 1200);
                                }} catch {{
                                    button.innerHTML = '<i class="bi bi-x"></i><span class="ml-1">Failed</span>';
                                    setTimeout(() => {{
                                        button.innerHTML = '<i class="bi bi-clipboard"></i><span class="ml-1">Copy</span>';
                                    }}, 1200);
                                }}
                            }});
                            pre.appendChild(button);
                        }});
                    }});
                """ ]
                script [] [ rawText $"""
                    window.addEventListener('DOMContentLoaded', () => {{
                        const filter = document.getElementById('sidebar-filter');
                        if (filter) {{
                            const apply = () => {{
                                const query = filter.value.trim().toLowerCase();
                                document.querySelectorAll('#sidebar-root [data-sidebar-item="true"]').forEach(item => {{
                                    const text = item.textContent.toLowerCase();
                                    item.style.display = !query || text.includes(query) ? '' : 'none';
                                }});
                                document.querySelectorAll('#sidebar-root [data-sidebar-section="true"]').forEach(section => {{
                                    const visible = Array.from(section.querySelectorAll('[data-sidebar-item="true"]')).some(el => el.style.display !== 'none');
                                    section.style.display = visible ? '' : 'none';
                                }});
                            }};
                            filter.addEventListener('input', apply);
                            apply();
                        }}

                        const focusSearch = () => {{
                            const input = document.querySelector('#search-ui input, .pagefind-ui__search-input');
                            if (input) {{
                                input.focus();
                                input.select?.();
                                return true;
                            }}
                            return false;
                        }};

                        document.addEventListener('keydown', (event) => {{
                            if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey) return;
                            const active = document.activeElement;
                            const typing = active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.isContentEditable);
                            if (typing) return;

                            if (event.key === '/' || event.key === 's') {{
                                if (focusSearch()) {{
                                    event.preventDefault();
                                }}
                            }} else if (event.key === 'd') {{
                                window.location.href = '{homeHref}';
                            }} else if (event.key === 'a') {{
                                window.location.href = '{apiHref}';
                            }}
                        }});
                    }});
                """ ]
                // Dynamic On-This-Page Generator
                script [] [ rawText """
                    window.addEventListener('DOMContentLoaded', () => {
                        const headings = Array.from(document.querySelectorAll('main h1, main h2, main h3')).filter(h => h.id !== 'search-ui');
                        const toc = document.getElementById('on-this-page');
                        if (toc && headings.length > 0) {
                            headings.forEach(h => {
                                // Extract clean text ignoring badges/metadata
                                let cleanText = h.getAttribute('data-toc-title');
                                if (!cleanText) {
                                    cleanText = Array.from(h.childNodes)
                                        .filter(node => node.nodeType === Node.TEXT_NODE)
                                        .map(node => node.textContent.trim())
                                        .join(' ');
                                }
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
