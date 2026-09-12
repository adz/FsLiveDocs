namespace FsLiveDocs.Renderer

open System
open System.IO
open System.Text.RegularExpressions
open Giraffe.ViewEngine
open Axial
open Axial.FileSystem
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects
open FsLiveDocs.Renderer.Rendering

/// <summary>The high-level site assembly engine: decides which pages exist and where they go.
/// Page-by-page HTML templating is a deep module of its own -- see
/// <see cref="T:FsLiveDocs.Renderer.Rendering.PageRenderer"/>.</summary>
module SiteBuilder =

    /// <summary>Shared inputs for rendering a documentation page.</summary>
    type SiteRenderContext = PageRenderer.SiteRenderContext

    /// <summary>Renders a single Markdown guide page.</summary>
    let renderPage page context = PageRenderer.renderPage page context

    /// <summary>Renders a single API entity page.</summary>
    let renderEntityPage entity context = PageRenderer.renderEntityPage entity context

    /// <summary>Generates a text-based summary of the API for LLM consumption.</summary>
    let generateLlmsTxt package = PageRenderer.generateLlmsTxt package

    /// <summary>One renderer-generated blog listing page before it is placed in the site layout.</summary>
    type private BlogListingPage = { Path: string; Title: string; Body: string }

    /// Builds the generated blog index, archive, and series pages from the non-draft dated posts.
    /// Every link is relative to the generated page itself, so the output works under any hosting
    /// sub-path and inside versioned history directories.
    let private blogListingPages (pages: ContentPage list) =
        let index = Blog.buildPostIndex true pages
        let encode = Net.WebUtility.HtmlEncode
        let rootFor (path: string) = String.replicate (path.Split('/').Length - 1) "../"
        let card root (post: ContentPage) =
            let date = post.Metadata.Date |> Option.map (fun value -> value.ToString("yyyy-MM-dd")) |> Option.defaultValue ""
            let tags =
                post.Metadata.Tags
                |> List.map (fun tag ->
                    "<a class=\"badge badge-ghost\" href=\"" + root + "blog/tags/" + encode (tag.Trim().ToLowerInvariant()) + ".html\">" + encode tag + "</a>")
                |> String.concat ""
            "<article class=\"livedocs-blog-card card card-compact bg-base-100 border border-base-300\"><div class=\"card-body\">"
            + "<h2 class=\"card-title text-lg\"><a class=\"link link-hover\" href=\"" + root + encode post.OutputPath + "\">" + encode post.Metadata.Title + "</a></h2>"
            + "<p class=\"text-sm opacity-70\">" + date + " · " + string (Blog.estimatedReadingMinutes post) + " min read</p>"
            + "<p>" + encode (Blog.excerpt post) + "</p>"
            + "<div class=\"flex flex-wrap gap-1\">" + tags + "</div></div></article>"
        let listing root heading (posts: ContentPage list) navigation =
            "<div class=\"livedocs-blog not-prose\"><h1 class=\"text-3xl font-bold mb-6\">" + encode heading + "</h1>"
            + "<div class=\"grid gap-3\">" + (posts |> List.map (card root) |> String.concat "") + "</div>"
            + navigation + "</div>"
        let chunks =
            match index.ByDateDesc |> List.chunkBySize 10 with
            | [] -> [ [] ]
            | values -> values
        let pagePath number = if number = 1 then "blog/index.html" else $"blog/page/{number}/index.html"
        let indexPages =
            chunks
            |> List.mapi (fun position chunk ->
                let number = position + 1
                let path = pagePath number
                let root = rootFor path
                let link number label = "<a class=\"btn btn-sm btn-ghost\" href=\"" + root + pagePath number + "\">" + label + "</a>"
                let older = if number < chunks.Length then link (number + 1) "← Older posts" else "<span></span>"
                let newer = if number > 1 then link (number - 1) "Newer posts →" else "<span></span>"
                let navigation = "<nav class=\"livedocs-blog-pagination flex justify-between mt-6\">" + older + newer + "</nav>"
                { Path = path; Title = "Blog"; Body = listing root "Blog" chunk navigation })
        let archive folder label (posts: Map<string, ContentPage list>) =
            posts
            |> Map.toList
            |> List.map (fun (key, entries) ->
                let path = $"blog/{folder}/{key}.html"
                { Path = path; Title = $"{label} {key}"; Body = listing (rootFor path) $"{label} {key}" entries "" })
        let series =
            (Blog.buildSeriesIndex true pages).BySeriesName
            |> Map.toList
            |> List.map (fun (name, entries) ->
                let path = $"blog/series/{name}.html"
                let root = rootFor path
                let title = entries |> List.tryHead |> Option.bind _.Post.Metadata.Series |> Option.defaultValue name
                let parts =
                    entries
                    |> List.map (fun entry ->
                        $"<li><span class=\"opacity-70\">Part {entry.PartNumber} of {entry.PartCount}:</span> <a class=\"link\" href=\"{root}{encode entry.Post.OutputPath}\">{encode entry.Post.Metadata.Title}</a></li>")
                    |> String.concat ""
                { Path = path
                  Title = $"Series: {title}"
                  Body = $"<div class=\"livedocs-blog not-prose\"><h1 class=\"text-3xl font-bold mb-6\">Series: {encode title}</h1><ol class=\"livedocs-series-parts list-decimal pl-6 space-y-1\">{parts}</ol></div>" })
        indexPages @ archive "tags" "Posts tagged" index.ByTag @ archive "category" "Posts in" index.ByCategory @ series

    /// Builds the Atom feed. Links are relative to the feed's own location (`blog/feed.xml`), which
    /// feed readers resolve against the URL they fetched the feed from.
    let internal blogFeed (pages: ContentPage list) =
        let index = Blog.buildPostIndex true pages
        let encode = Net.WebUtility.HtmlEncode
        let updated =
            index.ByDateDesc
            |> List.tryHead
            |> Option.bind _.Metadata.Date
            |> Option.map (fun date -> date.ToString("yyyy-MM-dd") + "T00:00:00Z")
            |> Option.defaultValue "1970-01-01T00:00:00Z"
        let entries =
            index.ByDateDesc
            |> List.map (fun post ->
                let date = post.Metadata.Date.Value.ToString("yyyy-MM-dd") + "T00:00:00Z"
                "<entry><title>" + encode post.Metadata.Title + "</title>"
                + "<id>urn:fslivedocs:post:" + encode post.OutputPath + "</id>"
                + "<link rel=\"alternate\" type=\"text/html\" href=\"../" + encode post.OutputPath + "\"/>"
                + "<updated>" + date + "</updated><published>" + date + "</published>"
                + "<summary>" + encode (Blog.excerpt post) + "</summary></entry>")
            |> String.concat ""
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><feed xmlns=\"http://www.w3.org/2005/Atom\"><title>Blog</title>"
        + "<id>urn:fslivedocs:blog-feed</id><updated>" + updated + "</updated>"
        + "<link rel=\"self\" type=\"application/atom+xml\" href=\"feed.xml\"/><link rel=\"alternate\" type=\"text/html\" href=\"index.html\"/>"
        + entries + "</feed>"

    /// Writes the generated blog pages through the caller's page renderer, so they share the site
    /// layout, theme, navigation, and (for documentation sets) chrome of ordinary guide pages.
    /// Sites without dated posts get no blog output at all.
    let private renderBlogOutputs (render: ContentPage -> string) outputDir (pages: ContentPage list) =
        if not (Blog.buildPostIndex true pages).ByDateDesc.IsEmpty then
            let write (path: string) (text: string) =
                let destination = Path.Combine(outputDir, path)
                Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
                File.WriteAllText(destination, text)
            for listing in blogListingPages pages do
                let page =
                    { Metadata = ContentMetadata.empty listing.Title
                      ContentHtml = listing.Body
                      Markdown = ""
                      FilePath = listing.Path
                      OutputPath = listing.Path
                      SectionOrder = Int32.MaxValue }
                write listing.Path (render page)
            write "blog/feed.xml" (blogFeed pages)

    /// <summary>Shared inputs for building the generated site.</summary>
    type SiteBuildContext = {
        Pages: ContentPage list
        Package: PackageModel
        Config: SiteConfig
        Versions: string list
        Theme: string
        RootPath: string
        SiteRootPath: string
        OutputDir: string
    }

    /// <summary>One resolved documentation set ready for renderer-only site assembly.</summary>
    type DocsSetSite =
        {
            Set: ReleaseDocsSet
            /// Global package model, optionally enriched by this set's API Markdown.
            Package: PackageModel
            Pages: ContentPage list
        }

    /// <summary>One captured version and its renderer-neutral set model.</summary>
    type DocsSetVersionSite =
        { Version: string
          Package: PackageModel
          Sets: DocsSetSite list
          StaticRoot: string option
          UsesDocumentationSets: bool }

    let private parallelRender (items: 'a array) (render: 'a -> unit) =
        let options = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = min 4 Environment.ProcessorCount)
        Threading.Tasks.Parallel.ForEach(items, options, Action<'a>(render)) |> ignore

    /// Computes `f` over every item concurrently and returns the results in input order. Pure
    /// rendering work (building an HTML string) stays parallel here; only the file-system writes
    /// that follow are gathered into a Flow.
    let private parallelMap (items: 'a array) (f: 'a -> 'b) : 'b array =
        let results: 'b array = Array.zeroCreate items.Length
        let options = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = min 4 Environment.ProcessorCount)
        Threading.Tasks.Parallel.For(0, items.Length, options, (fun i -> results.[i] <- f items.[i])) |> ignore
        results

    /// Writes every (path, content) pair as one composed Flow, run exactly once -- never one
    /// `Run.*` call per output file, however many pages or entities are being rendered.
    let private writeAll (writes: (string * string) list) =
        let work =
            writes
            |> Flow.traverse (fun (path, content) ->
                flow {
                    match Path.GetDirectoryName(path: string) with
                    | null
                    | "" -> ()
                    | directory -> do! FileSystem.createDirectory directory
                    do! FileSystem.writeAllText path content
                })
            |> Flow.map ignore
        Run.orRaise FileSystemError.describe "Could not write generated site output" work

    /// Ensures a directory exists, even when nothing is subsequently written into it (an empty API
    /// surface still needs `api/` to exist for link validation to have somewhere to look).
    let private ensureDirectory (path: string) =
        Run.orRaise FileSystemError.describe $"Could not create directory {path}" (FileSystem.createDirectory path)

    /// Deletes an existing output directory and recreates it empty, as one composed Flow.
    let private resetOutputDirectory (outputDir: string) =
        let work =
            flow {
                let! exists = FileSystem.directoryExists outputDir
                if exists then do! FileSystem.deleteDirectory outputDir true
                do! FileSystem.createDirectory outputDir
            }
        Run.orRaise FileSystemError.describe $"Could not reset output directory {outputDir}" work

    let private packageIntroduction packageName entities =
        let rec findExact = function
            | [] -> None
            | entity :: rest ->
                if entity.Id.Equals(packageName, StringComparison.OrdinalIgnoreCase)
                   && not (Documentation.isEmpty entity.Summary) then
                    Some entity.Summary
                else
                    findExact entity.Entities |> Option.orElseWith (fun () -> findExact rest)

        let rec findFirst = function
            | [] -> None
            | entity :: rest ->
                if not (Documentation.isEmpty entity.Summary) then Some entity.Summary
                else findFirst entity.Entities |> Option.orElseWith (fun () -> findFirst rest)

        findExact entities |> Option.orElseWith (fun () -> findFirst entities)

    let private shiftApiHtmlIntoPackageDirectory html =
        Regex.Replace(
            html,
            "(?<attribute>href|src)=\"(?<url>[^\"]+)\"",
            MatchEvaluator(fun matchedValue ->
                let url = matchedValue.Groups["url"].Value
                if url.StartsWith("#", StringComparison.Ordinal)
                   || url.StartsWith("/", StringComparison.Ordinal)
                   || Uri.IsWellFormedUriString(url, UriKind.Absolute) then
                    matchedValue.Value
                else
                    let attributeName = matchedValue.Groups["attribute"].Value
                    $"{attributeName}=\"../{url}\""),
            RegexOptions.IgnoreCase)

    let private validateGeneratedApiLinks (apiDir: string) =
        let hrefPattern = Regex("href=\"(?<href>[^\"]+)\"", RegexOptions.IgnoreCase)
        let apiRoot = Path.GetFullPath(apiDir) + string Path.DirectorySeparatorChar

        // Every generated page's HTML is read up front, and every link target's existence is
        // checked up front, as one composed Flow -- rather than one read/exists call per page or
        // per link, of which a large API surface can have many.
        let work =
            flow {
                let! files = FileSystem.getFiles apiDir "*.html" SearchOption.TopDirectoryOnly

                return!
                    files
                    |> Array.toList
                    |> Flow.traverse (fun path -> FileSystem.readAllText path |> Flow.map (fun html -> path, html))
            }

        let pages = Run.orRaise FileSystemError.describe $"Could not validate generated API links under {apiDir}" work

        let candidateLinks =
            pages
            |> List.collect (fun (pagePath, html) ->
                hrefPattern.Matches(html)
                |> Seq.cast<Match>
                |> Seq.choose (fun link ->
                    let href = link.Groups.["href"].Value
                    if not (href.StartsWith("#", StringComparison.Ordinal))
                       && not (href.StartsWith("../", StringComparison.Ordinal))
                       && not (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                       && not (href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                       && not (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) then
                        let targetName = href.Split([| '#'; '?' |], 2).[0] |> Uri.UnescapeDataString
                        let targetPath = Path.GetFullPath(Path.Combine(apiDir, targetName))
                        Some(pagePath, href, targetPath)
                    else
                        None)
                |> Seq.toList)

        let existenceWork =
            candidateLinks
            |> List.map (fun (_, _, targetPath) -> targetPath)
            |> List.distinct
            |> Flow.traverse (fun targetPath -> FileSystem.fileExists targetPath |> Flow.map (fun exists -> targetPath, exists))

        let targetExists =
            Run.orRaise FileSystemError.describe $"Could not validate generated API links under {apiDir}" existenceWork
            |> Map.ofList

        for pagePath, href, targetPath in candidateLinks do
            if not (targetPath.StartsWith(apiRoot, StringComparison.Ordinal)) || not targetExists.[targetPath] then
                invalidOp $"Broken generated API link in {Path.GetFileName(pagePath)}: {href}"

    /// <summary>Builds the primary documentation site.</summary>
    let build (context: SiteBuildContext) =
        let renderContext : SiteRenderContext = {
            AllPages = context.Pages
            Package = context.Package
            Config = context.Config
            Versions = context.Versions
            Theme = context.Theme
            RootPath = context.RootPath
            SiteRootPath = context.SiteRootPath
        }

        resetOutputDirectory context.OutputDir

        // Pages are independent immutable renders. Rendering them concurrently avoids making
        // large documentation sets pay the full HTML generation cost serially; the resulting
        // (path, html) pairs are then written as one composed Flow, run exactly once.
        let pageWrites =
            context.Pages
            |> List.toArray
            |> fun pages ->
                parallelMap pages (fun page ->
                    let depth = page.OutputPath.Split('/').Length - 1
                    let pageContext =
                        { renderContext with
                            RootPath = context.RootPath + String.replicate depth "../"
                            SiteRootPath = context.SiteRootPath + String.replicate depth "../" }
                    let html = renderPage page pageContext
                    Path.Combine(context.OutputDir, page.OutputPath), html)
            |> Array.toList

        renderBlogOutputs
            (fun page ->
                let depth = page.OutputPath.Split('/').Length - 1
                renderPage
                    page
                    { renderContext with
                        RootPath = context.RootPath + String.replicate depth "../"
                        SiteRootPath = context.SiteRootPath + String.replicate depth "../" })
            context.OutputDir
            context.Pages

        // Render API docs - Multi-page approach
        let apiDir = Path.Combine(context.OutputDir, "api")

        let apiRenderContext =
            { renderContext with
                RootPath = context.RootPath + "../"
                SiteRootPath = context.SiteRootPath + "../" }
        let allEntities = Presentation.flattenEntities context.Package.Entities

        let entityWrites =
            allEntities
            |> List.toArray
            |> fun entities ->
                parallelMap entities (fun entity ->
                    Path.Combine(apiDir, entity.Id + ".html"), renderEntityPage entity apiRenderContext)
            |> Array.toList

        let packageDir = Path.Combine(apiDir, "packages")

        let packageWrites =
            (if isNull (box context.Package.Packages) then [] else context.Package.Packages)
            |> List.toArray
            |> fun packages ->
                parallelMap packages (fun packageInfo ->
                    let ownedIds = packageInfo.EntityIds |> Set.ofList
                    let ownedEntities = allEntities |> List.filter (fun entity -> ownedIds.Contains entity.Id)
                    let contributedEntities = View.entitiesForPackage packageInfo context.Package.Entities

                    if not ownedEntities.IsEmpty then
                        let introduction =
                            packageIntroduction packageInfo.Name contributedEntities
                            |> Option.map (Presentation.renderDocumentationHtml context.Package >> shiftApiHtmlIntoPackageDirectory)

                        let packageContent = [
                            div [ _class "flex items-center gap-3 mb-8" ] [
                                h1 [ _id "package"; attr "data-toc-title" packageInfo.Name; _class "text-5xl font-black tracking-tighter" ] [ str packageInfo.Name ]
                                span [ _class "badge badge-primary badge-sm" ] [ str "Package" ]
                            ]
                            match introduction with
                            | Some html ->
                                div [ _class "prose prose-lg max-w-none mb-12 bg-base-200/30 p-8 rounded-3xl border border-base-300" ] [ rawText html ]
                            | None -> emptyText
                            View.h2WithAnchor "contents" "Contents" "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                            div [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ] (
                                ownedEntities |> List.map (fun entity ->
                                    a [ _href ("../" + entity.Id + ".html"); _class "flex items-center justify-between p-4 bg-base-100 border border-base-300 rounded-2xl hover:border-primary hover:shadow-md transition-all group" ] [
                                        span [ _class "font-bold group-hover:text-primary transition-colors" ] [ str entity.Name ]
                                        span [ _class "badge badge-sm opacity-40 font-mono text-[10px]" ] [ str (string entity.Kind) ]
                                    ])
                            )
                        ]
                        let packageContext =
                            { renderContext with
                                RootPath = context.RootPath + "../../"
                                SiteRootPath = context.SiteRootPath + "../../" }
                        let outputPath = "api/packages/" + Uri.EscapeDataString packageInfo.Name + ".html"
                        let html = View.layout packageInfo.Name context.Pages context.Package context.Config context.Versions context.Theme packageContext.RootPath packageContext.SiteRootPath outputPath packageContent |> RenderView.AsString.htmlNode
                        Some(Path.Combine(packageDir, Uri.EscapeDataString packageInfo.Name + ".html"), html)
                    else
                        None)
            |> Array.toList
            |> List.choose id

        ensureDirectory apiDir

        writeAll (
            (Path.Combine(context.OutputDir, "llms.txt"), generateLlmsTxt context.Package)
            :: pageWrites @ entityWrites @ packageWrites
        )

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
        let (apiHtml: string) = View.layout "API Reference" context.Pages context.Package context.Config context.Versions context.Theme context.RootPath context.SiteRootPath "api.html" apiOverview |> RenderView.AsString.htmlNode
        writeAll [ Path.Combine(context.OutputDir, "api.html"), apiHtml ]

        // Generate a fallback homepage only when the consumer has not authored docs/index.md.
        let indexPath = Path.Combine(context.OutputDir, "index.html")
        let indexAlreadyExists = Run.orFallback (FileSystem.fileExists indexPath) false
        if not indexAlreadyExists then
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
          let (html: string) = View.layout "Home" context.Pages context.Package context.Config context.Versions context.Theme context.RootPath context.SiteRootPath "index.html" indexContent |> RenderView.AsString.htmlNode
          writeAll [ indexPath, html ]

    let private packageForEntityIds (package: PackageModel) (entityIds: string list) =
        let allowed = Set.ofList entityIds

        let rec retain (entity: EntityModel) =
            let children = entity.Entities |> List.choose retain

            if allowed.Contains entity.Id || not children.IsEmpty then
                Some { entity with Entities = children }
            else
                None

        let packages =
            (if isNull (box package.Packages) then
                 []
             else
                 package.Packages)
            |> List.choose (fun info ->
                let ids = info.EntityIds |> List.filter allowed.Contains

                if ids.IsEmpty then
                    None
                else
                    Some { info with EntityIds = ids })

        { package with
            Entities = package.Entities |> List.choose retain
            Packages = packages }

    let private routePrefix (set: ReleaseDocsSet) =
        if String.IsNullOrEmpty set.Path then
            ""
        else
            set.Path.Trim('/') + "/"

    let private setRootOutput (set: ReleaseDocsSet) = routePrefix set + "index.html"
    let private setApiOutput (set: ReleaseDocsSet) = routePrefix set + "api/index.html"

    let private versionOutputRoot currentVersion version =
        if version = currentVersion then
            ""
        else
            "history/" + version + "/"

    let private setLinks (site: DocsSetVersionSite) =
        site.Sets
        |> List.map (fun value ->
            ({ Id = value.Set.Id
               Title = value.Set.Title
               Path = value.Set.Path }
            : View.DocsSetLink))

    let private fallbackForSet (targetSite: DocsSetVersionSite) (setId: string) preferApi =
        match targetSite.Sets |> List.tryFind (fun candidate -> candidate.Set.Id = setId) with
        | Some target when preferApi && target.Set.Api -> setApiOutput target.Set
        | Some target -> setRootOutput target.Set
        | None ->
            targetSite.Sets
            |> List.tryFind (fun candidate -> candidate.Set.IsDefault)
            |> Option.map (fun candidate -> setRootOutput candidate.Set)
            |> Option.defaultValue "index.html"

    let private guideVersionTargets
        currentVersion
        (sites: DocsSetVersionSite list)
        (currentSet: ReleaseDocsSet)
        (page: ContentPage)
        =
        let relative = page.OutputPath.Substring((routePrefix currentSet).Length)

        sites
        |> List.map (fun targetSite ->
            let destination =
                match
                    targetSite.Sets
                    |> List.tryFind (fun candidate -> candidate.Set.Id = currentSet.Id)
                with
                | Some target ->
                    let exact = routePrefix target.Set + relative

                    if target.Pages |> List.exists (fun candidate -> candidate.OutputPath = exact) then
                        exact
                    else
                        setRootOutput target.Set
                | None -> fallbackForSet targetSite currentSet.Id false

            targetSite.Version, versionOutputRoot currentVersion targetSite.Version + destination)

    let private apiVersionTargets
        currentVersion
        (sites: DocsSetVersionSite list)
        (currentSet: ReleaseDocsSet)
        entityId
        =
        sites
        |> List.map (fun targetSite ->
            let destination =
                match
                    targetSite.Sets
                    |> List.tryFind (fun candidate -> candidate.Set.Id = currentSet.Id)
                with
                | Some target when target.Set.Api && target.Set.ApiEntityIds |> List.contains entityId ->
                    routePrefix target.Set + "api/" + entityId + ".html"
                | Some target when target.Set.Api -> setApiOutput target.Set
                | Some target -> setRootOutput target.Set
                | None -> fallbackForSet targetSite currentSet.Id true

            targetSite.Version, versionOutputRoot currentVersion targetSite.Version + destination)

    let private rootVersionTargets
        currentVersion
        (sites: DocsSetVersionSite list)
        (currentSet: ReleaseDocsSet)
        preferApi
        =
        sites
        |> List.map (fun targetSite ->
            targetSite.Version,
            versionOutputRoot currentVersion targetSite.Version
            + fallbackForSet targetSite currentSet.Id preferApi)

    let private renderDocsSetVersion
        currentVersion
        versions
        allSites
        config
        theme
        siteRootPath
        destination
        (site: DocsSetVersionSite)
        =
        let links = setLinks site

        for docsSet in site.Sets do
            let set = docsSet.Set
            let navigationPackage = packageForEntityIds docsSet.Package set.ApiEntityIds

            let apiRoutes =
                site.Sets
                |> List.sortBy (fun candidate ->
                    if candidate.Set.Id = set.Id then 0
                    elif candidate.Set.IsDefault then 1
                    else 2)
                |> List.collect (fun candidate ->
                    candidate.Set.ApiEntityIds |> List.map (fun id -> id, routePrefix candidate.Set))
                |> List.rev
                |> Map.ofList

            let chrome targets =
                ({ SetId = set.Id
                   SetTitle = set.Title
                   SetPath = set.Path
                   VersionPath = versionOutputRoot currentVersion site.Version
                   Sets = links
                   Sidebar = set.Sidebar
                   Api = set.Api
                   NavigationPackage = navigationPackage
                   ApiRoutes = apiRoutes
                   VersionTargets = targets }
                : View.SiteChrome)

            let baseContext rootPath : SiteRenderContext =
                { AllPages = docsSet.Pages
                  Package = docsSet.Package
                  Config = config
                  Versions = versions
                  Theme = theme
                  RootPath = rootPath
                  SiteRootPath = rootPath }

            let pageWrites =
                docsSet.Pages
                |> List.toArray
                |> fun pages ->
                    parallelMap pages (fun page ->
                        let depth = page.OutputPath.Split('/').Length - 1
                        let context = baseContext (siteRootPath + String.replicate depth "../")

                        let html =
                            PageRenderer.renderPageCore
                                (Some(chrome (guideVersionTargets currentVersion allSites set page)))
                                page
                                context

                        Path.Combine(destination, page.OutputPath), html)
                |> Array.toList

            let apiWrites, apiDirForValidation =
                if set.Api then
                    let apiDir =
                        Path.Combine(destination, (routePrefix set).Replace('/', Path.DirectorySeparatorChar), "api")

                    let allEntities = Presentation.flattenEntities navigationPackage.Entities

                    let entityContext =
                        baseContext (siteRootPath + String.replicate ((setApiOutput set).Split('/').Length - 1) "../")

                    let entityWrites =
                        allEntities
                        |> List.toArray
                        |> fun entities ->
                            parallelMap entities (fun entity ->
                                let targets = apiVersionTargets currentVersion allSites set entity.Id
                                let html = PageRenderer.renderEntityPageCore (Some(chrome targets)) entity entityContext
                                Path.Combine(apiDir, entity.Id + ".html"), html)
                        |> Array.toList

                    let packageDir = Path.Combine(apiDir, "packages")

                    let packageWrites =
                        navigationPackage.Packages
                        |> List.choose (fun packageInfo ->
                            let owned =
                                View.entitiesForPackage packageInfo navigationPackage.Entities
                                |> Presentation.flattenEntities

                            if not owned.IsEmpty then
                                let packageContent =
                                    [ h1
                                          [ _id "package"
                                            attr "data-toc-title" packageInfo.Name
                                            _class "text-5xl font-black tracking-tighter mb-12" ]
                                          [ str packageInfo.Name ]
                                      div
                                          [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ]
                                          (owned
                                           |> List.map (fun entity ->
                                               a
                                                   [ _href ("../" + entity.Id + ".html")
                                                     _class "p-4 border border-base-300 rounded-2xl font-bold" ]
                                                   [ str entity.Name ])) ]

                                let packageRoot =
                                    siteRootPath
                                    + String.replicate
                                        ((routePrefix set).Split('/', StringSplitOptions.RemoveEmptyEntries).Length + 2)
                                        "../"

                                let html =
                                    View.layoutWithChrome
                                        (chrome (rootVersionTargets currentVersion allSites set true))
                                        packageInfo.Name
                                        docsSet.Pages
                                        docsSet.Package
                                        config
                                        versions
                                        theme
                                        packageRoot
                                        packageRoot
                                        (routePrefix set + "api/packages/" + Uri.EscapeDataString packageInfo.Name + ".html")
                                        packageContent
                                    |> RenderView.AsString.htmlNode

                                Some(Path.Combine(packageDir, Uri.EscapeDataString packageInfo.Name + ".html"), html)
                            else
                                None)

                    let card entity =
                        a
                            [ _href (entity.Id + ".html")
                              _class "card bg-base-100 border border-base-300 p-5 hover:border-primary transition-all" ]
                            [ h3 [ _class "text-lg font-bold" ] [ str entity.Name ]
                              p [ _class "text-sm opacity-60 mt-2" ] [ str (Presentation.synopsis entity.Summary) ] ]

                    let overview =
                        [ View.h1WithAnchor "api-reference" "API Reference" "text-5xl font-black mb-12 tracking-tighter"
                          div
                              [ _class "grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4 not-prose" ]
                              (allEntities |> List.map card) ]

                    let overviewRoot =
                        siteRootPath + String.replicate ((setApiOutput set).Split('/').Length - 1) "../"

                    let apiHtml =
                        View.layoutWithChrome
                            (chrome (rootVersionTargets currentVersion allSites set true))
                            "API Reference"
                            docsSet.Pages
                            docsSet.Package
                            config
                            versions
                            theme
                            overviewRoot
                            overviewRoot
                            (setApiOutput set)
                            overview
                        |> RenderView.AsString.htmlNode

                    entityWrites @ packageWrites @ [ Path.Combine(apiDir, "index.html"), apiHtml ], Some apiDir
                else
                    [], None

            let indexPath = Path.Combine(destination, setRootOutput set)
            let indexAlreadyExists = Run.orFallback (FileSystem.fileExists indexPath) false

            let indexWrites =
                if not indexAlreadyExists then
                    let rootDepth = setRootOutput set |> fun output -> output.Split('/').Length - 1
                    let rootPath = siteRootPath + String.replicate rootDepth "../"

                    let content =
                        [ View.h1WithAnchor "home" set.Title "text-6xl font-black mb-8 tracking-tighter"
                          if set.Api then
                              a [ _href "api/"; _class "btn btn-primary" ] [ str "Explore API" ] ]

                    let html =
                        View.layoutWithChrome
                            (chrome (rootVersionTargets currentVersion allSites set false))
                            set.Title
                            docsSet.Pages
                            docsSet.Package
                            config
                            versions
                            theme
                            rootPath
                            rootPath
                            (setRootOutput set)
                            content
                        |> RenderView.AsString.htmlNode

                    [ indexPath, html ]
                else
                    []

            writeAll (pageWrites @ apiWrites @ indexWrites)

            apiDirForValidation |> Option.iter validateGeneratedApiLinks

        // Posts may live in any set; the generated blog pages sit at the site root and use the
        // default set's chrome.
        match site.Sets |> List.tryFind _.Set.IsDefault |> Option.orElse (List.tryHead site.Sets) with
        | None -> ()
        | Some defaultSite ->
            let set = defaultSite.Set
            let chrome =
                ({ SetId = set.Id
                   SetTitle = set.Title
                   SetPath = set.Path
                   VersionPath = versionOutputRoot currentVersion site.Version
                   Sets = links
                   Sidebar = set.Sidebar
                   Api = set.Api
                   NavigationPackage = packageForEntityIds defaultSite.Package set.ApiEntityIds
                   ApiRoutes = set.ApiEntityIds |> List.map (fun id -> id, routePrefix set) |> Map.ofList
                   VersionTargets = rootVersionTargets currentVersion allSites set false }
                : View.SiteChrome)
            renderBlogOutputs
                (fun page ->
                    let rootPath = siteRootPath + String.replicate (page.OutputPath.Split('/').Length - 1) "../"
                    PageRenderer.renderPageCore
                        (Some chrome)
                        page
                        { AllPages = defaultSite.Pages
                          Package = defaultSite.Package
                          Config = config
                          Versions = versions
                          Theme = theme
                          RootPath = rootPath
                          SiteRootPath = rootPath })
                destination
                (site.Sets |> List.collect _.Pages)

    /// <summary>Builds one shared shell containing all configured documentation sets.</summary>
    let buildDocsSets currentVersion (sets: DocsSetSite list) config versions theme outputDir =
        let site =
            { Version = currentVersion
              Package = sets.Head.Package
              Sets = sets
              StaticRoot = None
              UsesDocumentationSets = true }

        resetOutputDirectory outputDir
        writeAll [ Path.Combine(outputDir, "llms.txt"), generateLlmsTxt site.Package ]
        renderDocsSetVersion currentVersion versions [ site ] config theme "" outputDir site

    let private versionSiteOutputIdentities (site: DocsSetVersionSite) =
        [ for docsSet in site.Sets do
              yield! docsSet.Pages |> List.map _.OutputPath

              if docsSet.Set.Api then
                  let prefix = routePrefix docsSet.Set
                  yield if site.UsesDocumentationSets then prefix + "api/index.html" else prefix + "api.html"
                  yield! docsSet.Set.ApiEntityIds |> List.map (fun id -> prefix + "api/" + id + ".html")
                  yield!
                      (if isNull (box docsSet.Package.Packages) then [] else docsSet.Package.Packages)
                      |> List.map (fun packageInfo ->
                          prefix + "api/packages/" + Uri.EscapeDataString(packageInfo.Name) + ".html") ]
        |> Set.ofList

    /// Writes every missing redirect stub for one destination as one composed Flow -- a history
    /// build can have many sites times many stable identities, so every existence check and every
    /// write is gathered up front and run exactly once, rather than once per identity.
    let private writeVersionFallbacks (destination: string) (identities: string Set) (fallbackFor: string -> string) =
        let destinationRoot = Path.GetFullPath(destination) + string Path.DirectorySeparatorChar

        let candidates =
            identities
            |> Set.toList
            |> List.map (fun identity ->
                identity, Path.GetFullPath(Path.Combine(destination, identity.Replace('/', Path.DirectorySeparatorChar))))
            |> List.filter (fun (_, output) -> output.StartsWith(destinationRoot, StringComparison.Ordinal))

        let work =
            flow {
                let! existing =
                    candidates
                    |> Flow.traverse (fun (identity, output) ->
                        FileSystem.fileExists output |> Flow.map (fun exists -> identity, output, exists))

                let missing =
                    existing
                    |> List.filter (fun (_, _, exists) -> not exists)
                    |> List.map (fun (identity, output, _) -> identity, output)

                for identity, output in missing do
                    let fallback = fallbackFor identity
                    let target = Path.GetFullPath(Path.Combine(destination, fallback.Replace('/', Path.DirectorySeparatorChar)))
                    let relative = Path.GetRelativePath(Path.GetDirectoryName output, target).Replace('\\', '/')
                    let encoded = Net.WebUtility.HtmlEncode(relative)

                    match Path.GetDirectoryName(output: string) with
                    | null
                    | "" -> ()
                    | directory -> do! FileSystem.createDirectory directory

                    do!
                        FileSystem.writeAllText
                            output
                            $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex\"><meta http-equiv=\"refresh\" content=\"0; url={encoded}\"></head><body><a href=\"{encoded}\">Documentation moved</a></body></html>"
            }

        Run.orRaise FileSystemError.describe $"Could not write version fallbacks under {destination}" work

    let private fallbackForIdentity (site: DocsSetVersionSite) (identity: string) =
        let owningSet =
            site.Sets
            |> List.sortByDescending (fun candidate -> routePrefix candidate.Set |> String.length)
            |> List.tryFind (fun candidate -> identity.StartsWith(routePrefix candidate.Set, StringComparison.Ordinal))
            |> Option.orElseWith (fun () -> site.Sets |> List.tryFind (fun candidate -> candidate.Set.IsDefault))

        match owningSet with
        | Some candidate ->
            let prefix = routePrefix candidate.Set
            let isApiPath = identity.StartsWith(prefix + "api/", StringComparison.Ordinal)
            if isApiPath && candidate.Set.Api then
                if site.UsesDocumentationSets then prefix + "api/index.html" else prefix + "api.html"
            else
                setRootOutput candidate.Set
        | None -> "index.html"

    /// <summary>Renders captured documentation sets for every historical version.</summary>
    let buildDocsSetsHistory currentVersion (sites: DocsSetVersionSite list) config theme outputDir =
        let current =
            sites
            |> List.tryFind (fun site -> site.Version = currentVersion)
            |> Option.defaultWith (fun () -> invalidOp $"Current history version {currentVersion} was not loaded.")

        let versions =
            currentVersion
            :: (sites |> List.map _.Version |> List.filter ((<>) currentVersion))

        resetOutputDirectory outputDir

        let sitesToRender = current :: (sites |> List.filter (fun site -> site.Version <> currentVersion))

        let siteDestinations =
            sitesToRender
            |> List.map (fun site ->
                let siteRootPath, destination =
                    if site.Version = currentVersion then
                        "", outputDir
                    else
                        "../../", Path.Combine(outputDir, "history", site.Version)

                site, siteRootPath, destination)

        // Every history-site destination directory is created up front, as one composed Flow --
        // there are as many of these as there are captured versions, not as many as there are pages.
        let createDestinationsWork =
            siteDestinations
            |> List.map (fun (_, _, destination) -> destination)
            |> Flow.traverse FileSystem.createDirectory

        Run.orRaise FileSystemError.describe $"Could not create history site output directories under {outputDir}" createDestinationsWork
        |> ignore

        for site, siteRootPath, destination in siteDestinations do
            if site.UsesDocumentationSets then
                renderDocsSetVersion currentVersion versions sites config theme siteRootPath destination site
            else
                let legacy = site.Sets.Head

                build
                    { Pages = legacy.Pages
                      Package = legacy.Package
                      Config = config
                      Versions = versions
                      Theme = theme
                      RootPath = ""
                      SiteRootPath = siteRootPath
                      OutputDir = destination }

            site.StaticRoot
            |> Option.iter (fun root -> ContentProvider.copyStaticFiles root destination)

        // Legacy pages built their version links from their own output identity. When a page or
        // symbol did not exist in another release, that produced a dead link. Keep those stable
        // identities resolvable with renderer-owned redirects to the target set's API or home.
        let identities = sites |> List.map versionSiteOutputIdentities |> Set.unionMany

        for site in sites do
            let destination =
                if site.Version = currentVersion then outputDir
                else Path.Combine(outputDir, "history", site.Version)

            writeVersionFallbacks destination identities (fallbackForIdentity site)

        writeAll [ Path.Combine(outputDir, "llms.txt"), generateLlmsTxt current.Package ]

    /// <summary>Builds the current site and computes the version list from history snapshots.</summary>
    /// <param name="historyDir">The directory containing previous package snapshots.</param>
    /// <param name="currentPackage">The latest package model.</param>
    /// <param name="pages">The guide pages to render.</param>
    /// <param name="config">Build-time site configuration.</param>
    /// <param name="theme">The active DaisyUI theme.</param>
    /// <param name="outputDir">The output directory that will receive the rendered site.</param>
    let buildAll (historyDir: string) (currentPackage: PackageModel) (pages: ContentPage list) (config: SiteConfig) (theme: string) (outputDir: string) =
        // The history directory's listing and every captured version's JSON are read up front, as
        // one composed Flow, run exactly once -- rather than once per historical version.
        let historyWork =
            flow {
                let! historyExists = FileSystem.directoryExists historyDir

                if historyExists then
                    let! files = FileSystem.getFiles historyDir "*.json" SearchOption.TopDirectoryOnly

                    return!
                        files
                        |> Array.toList
                        |> Flow.traverse (fun path -> FileSystem.readAllText path |> Flow.map (fun text -> path, text))
                else
                    return []
            }

        let historyFiles = Run.orRaise FileSystemError.describe $"Could not scan history directory {historyDir}" historyWork

        let versions = historyFiles |> List.map (fst >> Path.GetFileNameWithoutExtension)
        let allVersions = currentPackage.Version :: versions |> List.distinct

        build {
            Pages = pages
            Package = currentPackage
            Config = config
            Versions = allVersions
            Theme = theme
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        for vJson, json in historyFiles do
            let v = Path.GetFileNameWithoutExtension(vJson)
            let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(json, FsLiveDocs.Core.Serialization.jsonSettings)
            let vDir = Path.Combine(outputDir, "history", v)
            build {
                Pages = pages
                Package = package
                Config = config
                Versions = allVersions
                Theme = theme
                RootPath = ""
                SiteRootPath = "../../"
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
                RootPath = ""
                SiteRootPath = rootPath
                OutputDir = destination
            }
            ContentProvider.copyStaticFiles docsDir destination

        renderSite "" outputDir current

        for site in sites |> List.filter (fun (version, _, _, _) -> version <> currentVersion) do
            let version, _, _, _ = site
            renderSite "../../" (Path.Combine(outputDir, "history", version)) site
