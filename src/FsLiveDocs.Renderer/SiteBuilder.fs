namespace FsLiveDocs.Renderer

open System
open System.IO
open System.Text.RegularExpressions
open Giraffe.ViewEngine
open FsLiveDocs.Core

/// <summary>The high-level site assembly engine.</summary>
module SiteBuilder =

    let private parallelRender (items: 'a array) (render: 'a -> unit) =
        let options = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = min 4 Environment.ProcessorCount)
        Threading.Tasks.Parallel.ForEach(items, options, Action<'a>(render)) |> ignore

    let private validateGeneratedApiLinks (apiDir: string) =
        let hrefPattern = Regex("href=\"(?<href>[^\"]+)\"", RegexOptions.IgnoreCase)
        let apiRoot = Path.GetFullPath(apiDir) + string Path.DirectorySeparatorChar

        for pagePath in Directory.GetFiles(apiDir, "*.html", SearchOption.TopDirectoryOnly) do
            let html = File.ReadAllText(pagePath)
            for link in hrefPattern.Matches(html) do
                let href = link.Groups.["href"].Value
                if not (href.StartsWith("#", StringComparison.Ordinal))
                   && not (href.StartsWith("../", StringComparison.Ordinal))
                   && not (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                   && not (href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                   && not (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) then
                    let targetName = href.Split([| '#'; '?' |], 2).[0] |> Uri.UnescapeDataString
                    let targetPath = Path.GetFullPath(Path.Combine(apiDir, targetName))
                    if not (targetPath.StartsWith(apiRoot, StringComparison.Ordinal)) || not (File.Exists targetPath) then
                        invalidOp $"Broken generated API link in {Path.GetFileName(pagePath)}: {href}"

    /// <summary>Shared inputs for rendering a documentation page.</summary>
    type SiteRenderContext = {
        AllPages: ContentPage list
        Package: PackageModel
        Config: SiteConfig
        Versions: string list
        Theme: string
        RootPath: string
    }

    /// <summary>Shared inputs for building the generated site.</summary>
    type SiteBuildContext = {
        Pages: ContentPage list
        Package: PackageModel
        Config: SiteConfig
        Versions: string list
        Theme: string
        RootPath: string
        OutputDir: string
    }

    /// <summary>Renders a single Markdown guide page.</summary>
    /// <param name="page">The processed content page to render.</param>
    /// <returns>The rendered HTML document as a string.</returns>
    let renderPage (page: ContentPage) (context: SiteRenderContext) =
        let content = [
            div [] [ rawText page.ContentHtml ]
        ]
        View.layout page.Metadata.Title context.AllPages context.Package context.Config context.Versions context.Theme context.RootPath content
        |> fun node -> RenderView.AsString.htmlNode node

    /// <summary>Renders a single API entity page (Module or Type).</summary>
    /// <param name="e">The entity to render.</param>
    /// <returns>The rendered HTML document as a string.</returns>
    let renderEntityPage (e: EntityModel) (context: SiteRenderContext) =
        // Other projects' own root entities (e.g. "Axial.Layers") nest under a shared parent
        // namespace ("Axial") in the merged tree. Each such project already gets its own sidebar
        // group and API index card, so listing it again in the parent's Contents is noise.
        let otherPackageRootIds =
            (if isNull (box context.Package.Packages) then [] else context.Package.Packages)
            |> List.map (fun package -> package.Name)
            |> Set.ofList

        let renderPackageBadges (ent: EntityModel) =
            let packageNames =
                // Only a package that directly owns this entity (not merely an ancestor namespace
                // shared by many packages) is worth surfacing here - otherwise every package that
                // nests anything below a shared namespace root would show up on that root's page.
                (if isNull (box context.Package.Packages) then [] else context.Package.Packages)
                |> List.filter (fun package -> package.EntityIds |> List.contains ent.Id)
                |> List.map (fun package -> package.Name)
                // A package name that matches the entity's own id is already implied by the page's
                // breadcrumb, so surfacing it as a badge is noise.
                |> List.filter (fun name -> name <> ent.Id)
                |> List.distinct
                |> List.sort
            if packageNames.IsEmpty then emptyText
            else
                div [ _class "not-prose flex flex-wrap items-center gap-2 -mt-4 mb-8" ] [
                    span [ _class "text-[10px] font-black uppercase tracking-widest opacity-40" ] [ str "Package" ]
                    yield! packageNames |> List.map (fun name -> span [ _class "badge badge-outline font-mono" ] [ str name ])
                ]

        let renderSummaryBlock summary =
            if Documentation.isEmpty summary then
                emptyText
            else
                let rendered = Presentation.renderDocumentationHtml context.Package summary
                div [ _class "prose prose-lg max-w-none mb-12 bg-base-200/30 p-8 rounded-3xl border border-base-300" ] [ rawText rendered ]

        let renderFieldTable (title: string) (items: MemberModel list) =
            if items.IsEmpty then emptyText
            else
                div [ _class "mb-16 not-prose" ] [
                    View.h2WithAnchor (e.Id + "-fields") title "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                    div [ _class "overflow-x-auto rounded-2xl border border-base-300 shadow-sm" ] [
                        table [ _class "table table-zebra w-full" ] [
                            thead [ _class "bg-base-200/50" ] [
                                tr [] [
                                    th [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Name" ]
                                    th [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Type" ]
                                    th [ attr "style" "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Description" ]
                                ]
                            ]
                            tbody [] (
                                items
                                |> List.map (fun m ->
                                    tr [] [
                                        td [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                            a [ _href ("#" + m.Id); _class "font-bold text-primary hover:underline" ] [ str m.Name ]
                                        ]
                                        td [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                            span [ _class "font-mono text-xs text-secondary bg-secondary/5 px-2 py-0.5 rounded" ] [ rawText m.Signature ]
                                        ]
                                        td [ _class "text-sm opacity-80 leading-relaxed"; attr "style" "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                            str (Presentation.synopsis m.Summary)
                                        ]
                                    ]
                                )
                            )
                        ]
                    ]
                ]

        let renderGenericEntity (ent: EntityModel) =
            div [ _class ""; _id ent.Id ] [
                h1 [
                    _class "text-4xl font-black mb-8 pb-4 border-b-8 border-primary/10 tracking-tight group scroll-mt-24 flex items-center gap-3"
                    attr "data-toc-title" ent.Name
                ] [
                    span [ _class "leading-tight" ] [ str ent.Name ]
                    span [ _class "badge badge-primary opacity-50 font-mono text-[10px]" ] [ str (string ent.Kind) ]
                    a [
                        _href ("#" + ent.Id)
                        _class "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
                        attr "aria-label" $"Copy link to {ent.Name}"
                        attr "title" $"Copy link to {ent.Name}"
                    ] [ i [ _class "bi bi-link-45deg text-base" ] [] ]
                ]

                renderPackageBadges ent

                renderSummaryBlock ent.Summary

                let ownContents =
                    ent.Entities |> List.filter (fun ne -> not (otherPackageRootIds.Contains ne.Id))

                let contentsCard (ne: EntityModel) =
                    a [ _href (ne.Id + ".html"); _class "flex items-center justify-between p-4 bg-base-100 border border-base-300 rounded-2xl hover:border-primary hover:shadow-md transition-all group" ] [
                        span [ _class "font-bold group-hover:text-primary transition-colors" ] [ str ne.Name ]
                        span [ _class "badge badge-sm opacity-40 font-mono text-[10px]" ] [ str (string ne.Kind) ]
                    ]

                if not ownContents.IsEmpty then
                    // Two independent projects can both add members directly to the same shared
                    // namespace (e.g. "Axial" and "Axial.Telemetry" both declare things in namespace
                    // "Axial.Telemetry"). Split Contents by the owning project and give each group an
                    // anchor, so a sidebar link scoped to one project can land on that project's own
                    // members instead of the page just looking like it belongs to a different one.
                    let packagesFor (childId: string) =
                        (if isNull (box context.Package.Packages) then [] else context.Package.Packages)
                        |> List.filter (fun package -> package.EntityIds |> List.contains childId)
                        |> List.map (fun package -> package.Name)

                    let groupedByPackage =
                        ownContents
                        |> List.groupBy (fun ne -> packagesFor ne.Id |> List.tryHead |> Option.defaultValue "")
                        |> List.sortBy fst

                    div [ _class "mb-16" ] [
                        View.h2WithAnchor (ent.Id + "-contents") "Contents" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                        if groupedByPackage.Length > 1 then
                            div [ _class "flex flex-col gap-8" ] (
                                groupedByPackage |> List.map (fun (packageName, items) ->
                                    div [ _class "flex flex-col gap-4" ] [
                                        if packageName <> "" then
                                            h3 [
                                                _id ("package-" + packageName)
                                                _class "scroll-mt-24 text-[10px] font-black uppercase tracking-widest opacity-40"
                                            ] [ str packageName ]
                                        div [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ] (items |> List.map contentsCard)
                                    ])
                            )
                        else
                            div [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ] (ownContents |> List.map contentsCard)
                    ]

                if ent.Kind <> EntityKind.Module && not ent.Members.IsEmpty then
                    div [ _class "mb-16 not-prose" ] [
                        View.h2WithAnchor (ent.Id + "-spec") "Specification" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                        div [ _class "rounded-3xl border border-base-300 bg-base-100 shadow-sm overflow-hidden" ] [
                            div [ _class "grid grid-cols-1 md:grid-cols-3 gap-0 border-b border-base-300" ] [
                                div [ _class "p-5 md:p-6" ] [
                                    div [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ] [ str "Kind" ]
                                    div [ _class "text-lg font-black" ] [ str (string ent.Kind) ]
                                ]
                                div [ _class "p-5 md:p-6 border-t md:border-t-0 md:border-l border-base-300" ] [
                                    div [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ] [ str "Members" ]
                                    div [ _class "text-lg font-black" ] [ str (string ent.Members.Length) ]
                                ]
                                div [ _class "p-5 md:p-6 border-t md:border-t-0 md:border-l border-base-300" ] [
                                    div [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ] [ str "Examples" ]
                                    div [ _class "text-lg font-black" ] [ str (string (Presentation.entityExamples ent).Length) ]
                                ]
                            ]
                            div [ _class "p-5 md:p-6 space-y-3" ] (
                                ent.Members
                                |> List.take (min 5 ent.Members.Length)
                                |> List.map (fun m ->
                                    div [ _class "flex flex-col gap-2 rounded-2xl border border-base-300 bg-base-200/20 p-4" ] [
                                        div [ _class "flex items-center justify-between gap-4" ] [
                                            span [ _class "font-bold text-primary" ] [ str m.Name ]
                                            span [ _class "text-[10px] uppercase tracking-[0.3em] opacity-40 font-black" ] [ str "Signature" ]
                                        ]
                                        div [ _class "font-mono text-sm text-accent overflow-x-auto" ] [ rawText (Presentation.highlightSignatureHtml m.Signature) ]
                                    ]
                                )
                            )
                        ]
                    ]

                if not ent.Members.IsEmpty then
                    div [ _class "mb-16 not-prose" ] [
                        View.h2WithAnchor (ent.Id + "-summary") "Summary" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                        div [ _class "overflow-x-auto rounded-2xl border border-base-300 shadow-sm" ] [
                            table [ _class "table table-zebra w-full" ] [
                                thead [ _class "bg-base-200/50" ] [
                                    tr [] [
                                        th [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Name" ]
                                        th [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Signature" ]
                                        th [ attr "style" "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ] [ str "Synopsis" ]
                                    ]
                                ]
                                tbody [] (
                                    ent.Members
                                    |> List.map (fun m ->
                                        tr [] [
                                            td [ attr "style" "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                                a [ _href ("#" + m.Id); _class "font-bold text-primary hover:underline" ] [ str m.Name ]
                                            ]
                                            td [ attr "style" "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                                span [ _class "font-mono text-xs text-secondary bg-secondary/5 px-2 py-0.5 rounded" ] [ rawText m.Signature ]
                                            ]
                                            td [ _class "text-sm opacity-80 leading-relaxed"; attr "style" "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ] [
                                                str (Presentation.synopsis m.Summary)
                                            ]
                                        ]
                                    )
                                )
                            ]
                        ]
                    ]

                div [ _class "space-y-12" ] (ent.Members |> List.map (View.apiCard context.Package context.Config.RepoUrl))

                let examples = Presentation.entityExamples ent
                if not examples.IsEmpty then
                    div [ _class "mt-24 border-t border-base-300 pt-16" ] [
                        View.h2WithAnchor (ent.Id + "-examples") "Examples" "text-3xl font-black mb-10 tracking-tighter"
                        div [ _class "space-y-12" ] (
                            examples |> List.map (fun ex ->
                                let exampleId =
                                    let slug = Regex.Replace(ex.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')
                                    if String.IsNullOrWhiteSpace slug then "example" else slug
                                div [ _class "not-prose" ] [
                                    if ex.Name <> "Example" then
                                        View.h3WithAnchor (ent.Id + "-example-" + exampleId) ex.Name "text-sm font-black mb-4 opacity-40 tracking-[0.3em]"
                                    pre [ _class "bg-neutral text-neutral-content p-6 rounded-2xl text-sm font-mono overflow-x-auto border-0 shadow-md" ] [
                                        code [ _class "language-fsharp" ] [ str ex.Content ]
                                    ]
                                ])
                        )
                    ]
            ]

        let renderRecordEntity (ent: EntityModel) =
            div [ _id ent.Id ] [
                h1 [
                    _class "text-4xl font-black mb-8 pb-4 border-b-8 border-primary/10 tracking-tight group scroll-mt-24 flex items-center gap-3"
                    attr "data-toc-title" ent.Name
                ] [
                    span [ _class "leading-tight" ] [ str ent.Name ]
                    span [ _class "badge badge-primary opacity-50 font-mono text-[10px]" ] [ str (string ent.Kind) ]
                    a [
                        _href ("#" + ent.Id)
                        _class "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
                        attr "aria-label" $"Copy link to {ent.Name}"
                        attr "title" $"Copy link to {ent.Name}"
                    ] [ i [ _class "bi bi-link-45deg text-base" ] [] ]
                ]

                renderPackageBadges ent

                renderSummaryBlock ent.Summary
                renderFieldTable "Fields" ent.Members

                let examples = Presentation.entityExamples ent
                if not examples.IsEmpty then
                    div [ _class "mt-24 border-t border-base-300 pt-16" ] [
                        View.h2WithAnchor (ent.Id + "-examples") "Examples" "text-3xl font-black mb-10 tracking-tighter"
                        div [ _class "space-y-12" ] (
                            examples |> List.map (fun ex ->
                                let exampleId =
                                    let slug = Regex.Replace(ex.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')
                                    if String.IsNullOrWhiteSpace slug then "example" else slug
                                div [ _class "not-prose" ] [
                                    if ex.Name <> "Example" then
                                        View.h3WithAnchor (ent.Id + "-example-" + exampleId) ex.Name "text-sm font-black mb-4 opacity-40 tracking-[0.3em]"
                                    pre [ _class "bg-neutral text-neutral-content p-6 rounded-2xl text-sm font-mono overflow-x-auto border-0 shadow-md" ] [
                                        code [ _class "language-fsharp" ] [ str ex.Content ]
                                    ]
                                ])
                        )
                    ]
            ]

        let content =
            [
                match e.Kind with
                | EntityKind.Record -> renderRecordEntity e
                | _ -> renderGenericEntity e
            ]
        View.layout e.Name context.AllPages context.Package context.Config context.Versions context.Theme context.RootPath content
        |> fun node -> RenderView.AsString.htmlNode node

    /// <summary>Generates a text-based summary of the API for LLM consumption.</summary>
    /// <param name="package">The package model to summarize.</param>
    /// <returns>A plaintext `llms.txt` document.</returns>
    /// <example name="GenerateLlmsTxtExample" data-livedocs="snapshot">
    /// > let package = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] };;
    /// val package: PackageModel = { Version = "1.0"
    ///   Entities = []
    ///   Scenarios = []
    ///   Packages = [] }
    ///
    /// > let summary = SiteBuilder.generateLlmsTxt package;;
    /// val summary: string = "# API Reference for LLMs
    /// "
    ///
    /// > summary.Split('\n').[0];;
    /// val it: string = "# API Reference for LLMs"
    /// </example>
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
    let build (context: SiteBuildContext) =
        let renderContext = {
            AllPages = context.Pages
            Package = context.Package
            Config = context.Config
            Versions = context.Versions
            Theme = context.Theme
            RootPath = context.RootPath
        }

        if Directory.Exists(context.OutputDir) then Directory.Delete(context.OutputDir, true)
        Directory.CreateDirectory(context.OutputDir) |> ignore
        
        // LLMS Integration
        File.WriteAllText(Path.Combine(context.OutputDir, "llms.txt"), generateLlmsTxt context.Package)
        
        // Pages are independent immutable renders. Rendering them concurrently avoids making
        // large documentation sets pay the full HTML generation cost serially.
        context.Pages
        |> List.toArray
        |> fun pages -> parallelRender pages (fun page ->
            let depth = page.OutputPath.Split('/').Length - 1
            let pageContext = { renderContext with RootPath = context.RootPath + String.replicate depth "../" }
            let html = renderPage page pageContext
            let outputPath = Path.Combine(context.OutputDir, page.OutputPath)
            let outputDirectory = Path.GetDirectoryName(outputPath)
            Directory.CreateDirectory(outputDirectory) |> ignore
            File.WriteAllText(outputPath, html))

        // Render API docs - Multi-page approach
        let apiDir = Path.Combine(context.OutputDir, "api")
        if not (Directory.Exists(apiDir)) then Directory.CreateDirectory(apiDir) |> ignore
        
        let apiRenderContext = { renderContext with RootPath = context.RootPath + "../" }
        let allEntities = Presentation.flattenEntities context.Package.Entities

        allEntities
        |> List.toArray
        |> fun entities -> parallelRender entities (fun entity ->
            let html = renderEntityPage entity apiRenderContext
            File.WriteAllText(Path.Combine(apiDir, entity.Id + ".html"), html))

        let packageDir = Path.Combine(apiDir, "packages")
        Directory.CreateDirectory(packageDir) |> ignore

        (if isNull (box context.Package.Packages) then [] else context.Package.Packages)
        |> List.toArray
        |> fun packages -> parallelRender packages (fun packageInfo ->
            let ownedIds = packageInfo.EntityIds |> Set.ofList
            let ownedEntities = allEntities |> List.filter (fun entity -> ownedIds.Contains entity.Id)

            if not ownedEntities.IsEmpty then
                let packageContent = [
                    div [ _class "flex items-center gap-3 mb-12" ] [
                        h1 [ _id "package"; attr "data-toc-title" packageInfo.Name; _class "text-5xl font-black tracking-tighter" ] [ str packageInfo.Name ]
                        span [ _class "badge badge-primary badge-sm" ] [ str "Package" ]
                    ]
                    View.h2WithAnchor "contents" "Contents" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                    div [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ] (
                        ownedEntities |> List.map (fun entity ->
                            a [ _href ("../" + entity.Id + ".html"); _class "flex items-center justify-between p-4 bg-base-100 border border-base-300 rounded-2xl hover:border-primary hover:shadow-md transition-all group" ] [
                                span [ _class "font-bold group-hover:text-primary transition-colors" ] [ str entity.Name ]
                                span [ _class "badge badge-sm opacity-40 font-mono text-[10px]" ] [ str (string entity.Kind) ]
                            ])
                    )
                ]
                let packageContext = { renderContext with RootPath = context.RootPath + "../../" }
                let html = View.layout packageInfo.Name context.Pages context.Package context.Config context.Versions context.Theme packageContext.RootPath packageContent |> RenderView.AsString.htmlNode
                File.WriteAllText(Path.Combine(packageDir, Uri.EscapeDataString packageInfo.Name + ".html"), html))

        validateGeneratedApiLinks apiDir

        // Generate api.html (Overview / API Reference index)
        let card (e: EntityModel) =
            a [ _href (context.RootPath + "api/" + e.Id + ".html"); _class "card bg-base-100 border border-base-300 p-5 hover:shadow-xl hover:border-primary transition-all group" ] [
                div [ _class "flex justify-between items-center" ] [
                    h3 [ _class "text-lg font-bold group-hover:text-primary transition-colors" ] [ str e.Name ]
                    span [ _class "badge badge-sm opacity-40" ] [ str (string e.Kind) ]
                ]
                p [ _class "text-sm opacity-60 mt-2 line-clamp-2" ] [ str (Presentation.synopsis e.Summary) ]
            ]

        let apiSections (entities: EntityModel list) =
            let topLevel =
                match entities with
                | [ e ] when e.Kind = EntityKind.Namespace && e.Members.IsEmpty -> e.Entities
                | _ -> entities

            topLevel |> List.map (fun root ->
                let descendants = Presentation.flattenEntities root.Entities
                section [ _class "flex flex-col gap-5" ] [
                    card root
                    if not descendants.IsEmpty then
                        div [ _class "grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4 ml-4 md:ml-8 border-l-4 border-primary/10 pl-4 md:pl-8" ] (
                            descendants |> List.map card
                        )
                ]
            )

        let packageGroups =
            if isNull (box context.Package.Packages) then []
            else
                context.Package.Packages
                |> List.map (fun project -> project.Name, View.entitiesForPackage project context.Package.Entities)
                |> List.filter (snd >> List.isEmpty >> not)

        let apiOverview = [
            View.h1WithAnchor "api-reference" "API Reference" "text-5xl font-black mb-12 tracking-tighter"
            div [ _class "flex flex-col gap-16 not-prose" ] (
                if packageGroups.IsEmpty then
                    apiSections context.Package.Entities
                else
                    packageGroups |> List.collect (fun (projectName, entities) ->
                        [
                            h2 [ _class "text-xs font-black uppercase tracking-[0.2em] opacity-40 border-b border-base-300 pb-3" ] [ str projectName ]
                            div [ _class "flex flex-col gap-12" ] (apiSections entities)
                        ])
            )
        ]
        let (apiHtml: string) = View.layout "API Reference" context.Pages context.Package context.Config context.Versions context.Theme context.RootPath apiOverview |> RenderView.AsString.htmlNode
        File.WriteAllText(Path.Combine(context.OutputDir, "api.html"), apiHtml)

        // Generate a fallback homepage only when the consumer has not authored docs/index.md.
        let indexPath = Path.Combine(context.OutputDir, "index.html")
        if not (File.Exists(indexPath)) then
          let indexContent = [
            h1 [
                _id "home"
                attr "data-toc-title" "FsLiveDocs"
                _class "text-7xl font-black mb-8 tracking-tighter group scroll-mt-24 flex items-center gap-3"
            ] [
                span [ _class "text-primary italic" ] [ str "Fs" ]
                str "LiveDocs"
                a [
                    _href "#home"
                    _class "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
                    attr "aria-label" "Copy link to FsLiveDocs"
                    attr "title" "Copy link to FsLiveDocs"
                ] [ i [ _class "bi bi-link-45deg text-base" ] [] ]
            ]
            p [ _class "text-2xl opacity-60 leading-relaxed max-w-3xl mb-12" ] [ 
                str "Verified documentation for the F# ecosystem. Guaranteed to compile, guaranteed to run." 
            ]
            div [ _class "flex flex-wrap gap-6 not-prose" ] [
                a [ _href (context.RootPath + "api.html"); _class "btn btn-primary btn-lg rounded-2xl px-12 h-20 shadow-2xl shadow-primary/20 text-lg no-underline" ] [ str "Explore API" ]
                a [ _href (context.RootPath + "verified-examples.html"); _class "btn btn-outline btn-lg rounded-2xl px-12 h-20 text-lg hover:bg-base-300 no-underline" ] [ str "Read Guides" ]
            ]
            div [ _class "grid grid-cols-1 md:grid-cols-3 gap-8 mt-24 not-prose" ] [
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-3xl shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-primary/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-check2-circle text-3xl text-primary" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Verified" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Every example is a test. If it doesn't run, the build fails." ]
                ]
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-3xl shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-secondary/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-link-45deg text-3xl text-secondary" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Connected" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Live transclusion from your source code directly into your guides." ]
                ]
                div [ _class "card bg-base-100 border border-base-300 p-8 rounded-3xl shadow-sm hover:shadow-xl transition-all group" ] [
                    div [ _class "bg-accent/10 w-16 h-16 rounded-2xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform" ] [
                        i [ _class "bi bi-clock-history text-3xl text-accent" ] []
                    ]
                    h3 [ _class "text-xl font-black mb-2" ] [ str "Versioned" ]
                    p [ _class "opacity-60 text-sm leading-relaxed" ] [ str "Zero-recompile documentation snapshots." ]
                ]
            ]
          ]
          let (html: string) = View.layout "Home" context.Pages context.Package context.Config context.Versions context.Theme context.RootPath indexContent |> RenderView.AsString.htmlNode
          File.WriteAllText(indexPath, html)

    /// <summary>Builds the current site and computes the version list from history snapshots.</summary>
    /// <param name="historyDir">The directory containing previous package snapshots.</param>
    /// <param name="currentPackage">The latest package model.</param>
    /// <param name="pages">The guide pages to render.</param>
    /// <param name="config">Build-time site configuration.</param>
    /// <param name="theme">The active DaisyUI theme.</param>
    /// <param name="outputDir">The output directory that will receive the rendered site.</param>
    let buildAll (historyDir: string) (currentPackage: PackageModel) (pages: ContentPage list) (config: SiteConfig) (theme: string) (outputDir: string) =
        let versions = 
            if Directory.Exists(historyDir) then
                Directory.GetFiles(historyDir, "*.json")
                |> Array.map Path.GetFileNameWithoutExtension
                |> Array.toList
            else []
        
        let allVersions = currentPackage.Version :: versions |> List.distinct
        
        build {
            Pages = pages
            Package = currentPackage
            Config = config
            Versions = allVersions
            Theme = theme
            RootPath = ""
            OutputDir = outputDir
        }

        if Directory.Exists(historyDir) then
            for vJson in Directory.GetFiles(historyDir, "*.json") do
                let v = Path.GetFileNameWithoutExtension(vJson)
                let json = File.ReadAllText(vJson)
                let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(json, FsLiveDocs.Core.Serialization.jsonSettings)
                let vDir = Path.Combine(outputDir, "history", v)
                build {
                    Pages = pages
                    Package = package
                    Config = config
                    Versions = allVersions
                    Theme = theme
                    RootPath = "../../"
                    OutputDir = vDir
                }

    /// <summary>Builds current and historical sites from verified API models and tagged documentation trees.</summary>
    let buildHistory (currentVersion: string) (sites: (string * PackageModel * ContentPage list * string) list) (config: SiteConfig) (theme: string) (outputDir: string) =
        let versions =
            currentVersion :: (sites |> List.map (fun (version, _, _, _) -> version) |> List.filter ((<>) currentVersion))
        let current =
            sites
            |> List.tryFind (fun (version, _, _, _) -> version = currentVersion)
            |> Option.defaultWith (fun () -> invalidOp $"Current history version {currentVersion} was not loaded.")

        let renderSite rootPath destination (version, package, pages, docsDir) =
            build {
                Pages = pages
                Package = package
                Config = config
                Versions = versions
                Theme = theme
                RootPath = rootPath
                OutputDir = destination
            }
            ContentProvider.copyStaticFiles docsDir destination

        renderSite "" outputDir current

        for site in sites |> List.filter (fun (version, _, _, _) -> version <> currentVersion) do
            let version, _, _, _ = site
            renderSite "../../" (Path.Combine(outputDir, "history", version)) site
