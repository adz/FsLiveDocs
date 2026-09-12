namespace FsLiveDocs.Renderer.Rendering

open System
open System.Text.RegularExpressions
open Giraffe.ViewEngine
open FsLiveDocs.Core
open FsLiveDocs.Renderer

/// Renders one guide page or one API entity page to HTML, and summarizes a package as
/// llms.txt. The deep-module internals behind SiteBuilder's multi-version build orchestration:
/// this module owns page templating, SiteBuilder owns deciding which pages to render where.
module PageRenderer =

    /// <summary>Shared inputs for rendering a documentation page.</summary>
    type SiteRenderContext = {
        AllPages: ContentPage list
        Package: PackageModel
        Config: SiteConfig
        Versions: string list
        Theme: string
        RootPath: string
        SiteRootPath: string
    }

    /// <summary>Renders a single Markdown guide page.</summary>
    /// <param name="page">The processed content page to render.</param>
    /// <returns>The rendered HTML document as a string.</returns>
    let internal renderPageCore chrome (page: ContentPage) (context: SiteRenderContext) =
        let blogShortcodes =
            let posts = Blog.buildPostIndex true context.AllPages
            let attribute name (args: string) =
                let matched = Regex.Match(args, name + "=\"(?<value>[^\"]*)\"")
                if matched.Success then Some matched.Groups.["value"].Value else None
            // Markdig renders a shortcode written as its own paragraph as escaped text. Matching only that
            // whole-paragraph form leaves shortcode syntax shown inside code spans and blocks untouched.
            let listing = Regex.Replace(page.ContentHtml, @"<p>{{&lt;\s*posts\b(?<args>.*?)&gt;}}</p>", fun matched ->
                let args = Net.WebUtility.HtmlDecode matched.Groups.["args"].Value
                let options = page.Metadata.BlogList
                let configured field = options |> Option.bind field
                let tag = attribute "tag" args |> Option.orElseWith (fun () -> configured _.Tag) |> Option.map _.Trim().ToLowerInvariant()
                let category = attribute "category" args |> Option.orElseWith (fun () -> configured _.Category) |> Option.map _.Trim().ToLowerInvariant()
                let limit =
                    attribute "limit" args
                    |> Option.bind (fun value -> match Int32.TryParse value with | true, number when number >= 0 -> Some number | _ -> None)
                    |> Option.orElseWith (fun () -> configured _.Limit)
                let layout = attribute "layout" args |> Option.orElseWith (fun () -> configured _.Layout) |> Option.defaultValue "list" |> _.Trim().ToLowerInvariant()
                let show =
                    attribute "show" args
                    |> Option.map (fun value -> value.Split(',') |> Array.map _.Trim().ToLowerInvariant() |> Set.ofArray)
                    |> Option.orElseWith (fun () -> configured (fun value -> if List.isEmpty value.Show then None else Some(value.Show |> List.map (fun item -> item.Trim().ToLowerInvariant()) |> Set.ofList)))
                    |> Option.defaultValue (match layout with | "preview" -> set [ "date"; "readingtime"; "summary"; "tags" ] | "compact" -> set [ "date" ] | _ -> Set.empty)
                if not (set [ "list"; "compact"; "preview" ] |> Set.contains layout) then invalidOp $"Unsupported blogList layout '{layout}' on {page.FilePath}."
                let selected =
                    posts.ByDateDesc
                    |> List.filter (fun post -> tag |> Option.forall (fun value -> post.Metadata.Tags |> List.exists (fun item -> item.Trim().ToLowerInvariant() = value)))
                    |> List.filter (fun post -> category |> Option.forall (fun value -> post.Metadata.Category |> Option.exists (fun item -> item.Trim().ToLowerInvariant() = value)))
                    |> fun values -> limit |> Option.map (fun number -> values |> List.truncate number) |> Option.defaultValue values
                let item post =
                    let link = "<a href=\"" + context.RootPath + Net.WebUtility.HtmlEncode post.OutputPath + "\">" + Net.WebUtility.HtmlEncode post.Metadata.Title + "</a>"
                    let date = if show.Contains "date" then "<span class=\"livedocs-post-date\">" + post.Metadata.Date.Value.ToString("yyyy-MM-dd") + "</span>" else ""
                    let reading = if show.Contains "readingtime" then "<span class=\"livedocs-post-reading-time\">" + string (Blog.estimatedReadingMinutes post) + " min read</span>" else ""
                    let summary = if show.Contains "summary" then "<p>" + Net.WebUtility.HtmlEncode(Blog.excerpt post) + "</p>" else ""
                    let tags = if show.Contains "tags" then "<span class=\"livedocs-post-tags\">" + (post.Metadata.Tags |> List.map Net.WebUtility.HtmlEncode |> String.concat ", ") + "</span>" else ""
                    if layout = "list" then "<li>" + link + "</li>" else "<article class=\"livedocs-post-" + layout + "\"><h3>" + link + "</h3>" + date + reading + summary + tags + "</article>"
                if layout = "list" then "<ul class=\"livedocs-post-list\">" + (selected |> List.map item |> String.concat "") + "</ul>"
                else "<div class=\"livedocs-post-list livedocs-post-list-" + layout + "\">" + (selected |> List.map item |> String.concat "") + "</div>")
            let series = Blog.buildSeriesIndex true context.AllPages
            Regex.Replace(listing, @"<p>{{&lt;\s*series-nav\s*&gt;}}</p>", fun _ ->
                match page.Metadata.Series |> Option.map _.Trim().ToLowerInvariant() |> Option.bind (fun name -> series.BySeriesName |> Map.tryFind name) with
                | None -> ""
                | Some entries -> "<ol class=\"livedocs-series-nav\">" + (entries |> List.map (fun entry -> "<li><a href=\"" + context.RootPath + Net.WebUtility.HtmlEncode entry.Post.OutputPath + "\">Part " + string entry.PartNumber + ": " + Net.WebUtility.HtmlEncode entry.Post.Metadata.Title + "</a></li>") |> String.concat "") + "</ol>")
        let blogChrome =
            if page.Metadata.Date.IsNone then ""
            else
                let posts = Blog.buildPostIndex true context.AllPages
                let series = Blog.buildSeriesIndex true context.AllPages
                let navigation = Blog.navigation page posts series
                let link label target = target |> Option.map (fun item -> $"<a href=\"{context.RootPath}{Net.WebUtility.HtmlEncode item.OutputPath}\">{label}: {Net.WebUtility.HtmlEncode item.Metadata.Title}</a>") |> Option.defaultValue ""
                let seriesPart = navigation.SeriesPart |> Option.map (fun (number, count) -> $"<p>Part {number} of {count}</p>") |> Option.defaultValue ""
                let date = page.Metadata.Date.Value.ToString("yyyy-MM-dd")
                let newer = link "Newer" navigation.Prev
                let older = link "Older" navigation.Next
                "<aside class=\"livedocs-blog-meta\"><p>" + date + " · " + string (Blog.estimatedReadingMinutes page) + " min read</p>" + seriesPart + "<nav>" + newer + " " + older + "</nav></aside>"
        let comments =
            if not page.Metadata.Comments then ""
            else
                let encode = Net.WebUtility.HtmlEncode
                // One provider-agnostic toggle and one embed region. Only the region's contents differ by
                // provider; the count is unknown at build time, so it stays a placeholder until provider
                // script reports it in the browser.
                let section (embed: string) =
                    "<section id=\"comments\" class=\"livedocs-comments not-prose mt-10\">"
                    + "<h2 class=\"text-xl font-semibold\"><a class=\"livedocs-comments-toggle link link-hover\" href=\"#comments\" aria-controls=\"livedocs-comments-embed\">Comments <span class=\"livedocs-comments-count\" data-state=\"loading\" aria-live=\"polite\">(…)</span></a></h2>"
                    + "<div id=\"livedocs-comments-embed\" class=\"livedocs-comments-embed mt-4\">" + embed + "</div></section>"
                match context.Config.CommentsProvider with
                | Some (Giscus settings) ->
                    let theme = settings.Theme |> Option.defaultValue context.Theme
                    section (
                        $"<script src=\"https://giscus.app/client.js\" data-repo=\"{encode settings.Repo}\" data-repo-id=\"{encode settings.RepoId}\" data-category=\"{encode settings.Category}\" data-category-id=\"{encode settings.CategoryId}\" data-mapping=\"pathname\" data-emit-metadata=\"1\" data-theme=\"{encode theme}\" crossorigin=\"anonymous\" async></script>"
                        + "<script>(function(){var count=document.querySelector('#comments .livedocs-comments-count');window.addEventListener('message',function(event){if(event.origin!=='https://giscus.app'||!event.data||!event.data.giscus)return;var discussion=event.data.giscus.discussion;if(!count)return;var total=discussion?(discussion.totalCommentCount||0)+(discussion.totalReplyCount||0):0;count.textContent='('+total+')';count.setAttribute('data-state','ready');});})();</script>")
                | Some (Custom html) -> section html
                | _ -> ""
        let content = [ div [] [ rawText (blogChrome + blogShortcodes + comments) ] ]

        match chrome with
        | Some value ->
            View.layoutWithChrome
                value
                page.Metadata.Title
                context.AllPages
                context.Package
                context.Config
                context.Versions
                context.Theme
                context.RootPath
                context.SiteRootPath
                page.OutputPath
                content
        | None ->
            View.layout
                page.Metadata.Title
                context.AllPages
                context.Package
                context.Config
                context.Versions
                context.Theme
                context.RootPath
                context.SiteRootPath
                page.OutputPath
                content
        |> fun node -> RenderView.AsString.htmlNode node

    let renderPage page context = renderPageCore None page context

    /// <summary>Renders a single API entity page (Module or Type).</summary>
    /// <param name="e">The entity to render.</param>
    /// <returns>The rendered HTML document as a string.</returns>
    let internal renderEntityPageCore chrome (e: EntityModel) (context: SiteRenderContext) =
        let entityTargets =
            match chrome with
            | Some(value: View.SiteChrome) ->
                value.ApiRoutes
                |> Map.map (fun id route -> context.RootPath + value.VersionPath + route + "api/" + id + ".html")
            | None -> Map.empty

        let renderDocumentation nodes =
            if entityTargets.IsEmpty then
                Presentation.renderDocumentationHtml context.Package nodes
            else
                Presentation.renderDocumentationHtmlWithTargets context.Package entityTargets nodes
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
                let rendered = renderDocumentation summary

                div
                    [ _class "prose prose-lg max-w-none mb-12 bg-base-200/30 p-8 rounded-3xl border border-base-300" ]
                    [ rawText rendered ]

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
            div
                [ _class ""; _id ent.Id ]
                [ h1
                      [ _class
                            "text-4xl font-black mb-8 pb-4 border-b-8 border-primary/10 tracking-tight group scroll-mt-24 flex items-center gap-3"
                        attr "data-toc-title" ent.Name ]
                      [ span [ _class "leading-tight" ] [ str ent.Name ]
                        span [ _class "badge badge-primary opacity-50 font-mono text-[10px]" ] [ str (string ent.Kind) ]
                        a
                            [ _href ("#" + ent.Id)
                              _class
                                  "anchor-link opacity-0 group-hover:opacity-60 transition-opacity no-underline inline-flex items-center justify-center w-6 h-6 text-base-content/60 hover:text-primary"
                              attr "aria-label" $"Copy link to {ent.Name}"
                              attr "title" $"Copy link to {ent.Name}" ]
                            [ i [ _class "bi bi-link-45deg text-base" ] [] ] ]

                  renderPackageBadges ent

                  renderSummaryBlock ent.Summary

                  let ownContents =
                      ent.Entities |> List.filter (fun ne -> not (otherPackageRootIds.Contains ne.Id))

                  let contentsCard (ne: EntityModel) =
                      a
                          [ _href (ne.Id + ".html")
                            _class
                                "flex items-center justify-between p-4 bg-base-100 border border-base-300 rounded-2xl hover:border-primary hover:shadow-md transition-all group" ]
                          [ span [ _class "font-bold group-hover:text-primary transition-colors" ] [ str ne.Name ]
                            span [ _class "badge badge-sm opacity-40 font-mono text-[10px]" ] [ str (string ne.Kind) ] ]

                  if not ownContents.IsEmpty then
                      // Two independent projects can both add members directly to the same shared
                      // namespace (e.g. "Axial" and "Axial.Telemetry" both declare things in namespace
                      // "Axial.Telemetry"). Split Contents by the owning project and give each group an
                      // anchor, so a sidebar link scoped to one project can land on that project's own
                      // members instead of the page just looking like it belongs to a different one.
                      let packagesFor (childId: string) =
                          (if isNull (box context.Package.Packages) then
                               []
                           else
                               context.Package.Packages)
                          |> List.filter (fun package -> package.EntityIds |> List.contains childId)
                          |> List.map (fun package -> package.Name)

                      let groupedByPackage =
                          ownContents
                          |> List.groupBy (fun ne -> packagesFor ne.Id |> List.tryHead |> Option.defaultValue "")
                          |> List.sortBy fst

                      div
                          [ _class "mb-16" ]
                          [ View.h2WithAnchor
                                (ent.Id + "-contents")
                                "Contents"
                                "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                            if groupedByPackage.Length > 1 then
                                div
                                    [ _class "flex flex-col gap-8" ]
                                    (groupedByPackage
                                     |> List.map (fun (packageName, items) ->
                                         div
                                             [ _class "flex flex-col gap-4" ]
                                             [ if packageName <> "" then
                                                   h3
                                                       [ _id ("package-" + packageName)
                                                         _class
                                                             "scroll-mt-24 text-[10px] font-black uppercase tracking-widest opacity-40" ]
                                                       [ str packageName ]
                                               div
                                                   [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ]
                                                   (items |> List.map contentsCard) ]))
                            else
                                div
                                    [ _class "grid grid-cols-1 md:grid-cols-2 gap-4 not-prose" ]
                                    (ownContents |> List.map contentsCard) ]

                  if ent.Kind <> EntityKind.Module && not ent.Members.IsEmpty then
                      div
                          [ _class "mb-16 not-prose" ]
                          [ View.h2WithAnchor
                                (ent.Id + "-spec")
                                "Specification"
                                "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                            div
                                [ _class "rounded-3xl border border-base-300 bg-base-100 shadow-sm overflow-hidden" ]
                                [ div
                                      [ _class "grid grid-cols-1 md:grid-cols-3 gap-0 border-b border-base-300" ]
                                      [ div
                                            [ _class "p-5 md:p-6" ]
                                            [ div
                                                  [ _class
                                                        "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ]
                                                  [ str "Kind" ]
                                              div [ _class "text-lg font-black" ] [ str (string ent.Kind) ] ]
                                        div
                                            [ _class "p-5 md:p-6 border-t md:border-t-0 md:border-l border-base-300" ]
                                            [ div
                                                  [ _class
                                                        "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ]
                                                  [ str "Members" ]
                                              div [ _class "text-lg font-black" ] [ str (string ent.Members.Length) ] ]
                                        div
                                            [ _class "p-5 md:p-6 border-t md:border-t-0 md:border-l border-base-300" ]
                                            [ div
                                                  [ _class
                                                        "text-[10px] uppercase tracking-[0.3em] opacity-40 mb-2 font-black" ]
                                                  [ str "Examples" ]
                                              div
                                                  [ _class "text-lg font-black" ]
                                                  [ str (string (Presentation.entityExamples ent).Length) ] ] ]
                                  div
                                      [ _class "p-5 md:p-6 space-y-3" ]
                                      (ent.Members
                                       |> List.take (min 5 ent.Members.Length)
                                       |> List.map (fun m ->
                                           div
                                               [ _class
                                                     "flex flex-col gap-2 rounded-2xl border border-base-300 bg-base-200/20 p-4" ]
                                               [ div
                                                     [ _class "flex items-center justify-between gap-4" ]
                                                     [ span [ _class "font-bold text-primary" ] [ str m.Name ]
                                                       span
                                                           [ _class
                                                                 "text-[10px] uppercase tracking-[0.3em] opacity-40 font-black" ]
                                                           [ str "Signature" ] ]
                                                 div
                                                     [ _class "font-mono text-sm text-accent overflow-x-auto" ]
                                                     [ rawText (Presentation.highlightSignatureHtml m.Signature) ] ])) ] ]

                  if not ent.Members.IsEmpty then
                      div
                          [ _class "mb-16 not-prose" ]
                          [ View.h2WithAnchor
                                (ent.Id + "-summary")
                                "Summary"
                                "text-xl font-black mb-6 opacity-30 uppercase tracking-widest"
                            div
                                [ _class "overflow-x-auto rounded-2xl border border-base-300 shadow-sm" ]
                                [ table
                                      [ _class "table table-zebra w-full" ]
                                      [ thead
                                            [ _class "bg-base-200/50" ]
                                            [ tr
                                                  []
                                                  [ th
                                                        [ attr
                                                              "style"
                                                              "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ]
                                                        [ str "Name" ]
                                                    th
                                                        [ attr
                                                              "style"
                                                              "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ]
                                                        [ str "Signature" ]
                                                    th
                                                        [ attr
                                                              "style"
                                                              "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important;" ]
                                                        [ str "Synopsis" ] ] ]
                                        tbody
                                            []
                                            (ent.Members
                                             |> List.map (fun m ->
                                                 tr
                                                     []
                                                     [ td
                                                           [ attr
                                                                 "style"
                                                                 "padding-left: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ]
                                                           [ a
                                                                 [ _href ("#" + m.Id)
                                                                   _class "font-bold text-primary hover:underline" ]
                                                                 [ str m.Name ] ]
                                                       td
                                                           [ attr
                                                                 "style"
                                                                 "padding-left: 1rem !important; padding-right: 1rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ]
                                                           [ span
                                                                 [ _class
                                                                       "font-mono text-xs text-secondary bg-secondary/5 px-2 py-0.5 rounded" ]
                                                                 [ rawText m.Signature ] ]
                                                       td
                                                           [ _class "text-sm opacity-80 leading-relaxed"
                                                             attr
                                                                 "style"
                                                                 "padding-right: 1.5rem !important; padding-top: 0.75rem !important; padding-bottom: 0.75rem !important; vertical-align: top !important;" ]
                                                           [ str (Presentation.synopsis m.Summary) ] ])) ] ] ]

                  div
                      [ _class "space-y-12" ]
                      (ent.Members
                       |> List.map (fun memberModel ->
                           if entityTargets.IsEmpty then
                               View.apiCard context.Package context.Config.RepoUrl memberModel
                           else
                               View.apiCardWithTargets context.Package entityTargets context.Config.RepoUrl memberModel))

                  let examples = Presentation.entityExamples ent

                  if not examples.IsEmpty then
                      div
                          [ _class "mt-24 border-t border-base-300 pt-16" ]
                          [ View.h2WithAnchor
                                (ent.Id + "-examples")
                                "Examples"
                                "text-3xl font-black mb-10 tracking-tighter"
                            div
                                [ _class "space-y-12" ]
                                (examples
                                 |> List.map (fun ex ->
                                     let exampleId =
                                         let slug =
                                             Regex.Replace(ex.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')

                                         if String.IsNullOrWhiteSpace slug then "example" else slug

                                     div
                                         [ _class "not-prose" ]
                                         [ if ex.Name <> "Example" then
                                               View.h3WithAnchor
                                                   (ent.Id + "-example-" + exampleId)
                                                   ex.Name
                                                   "text-sm font-black mb-4 opacity-40 tracking-[0.3em]"
                                           pre
                                               [ _class
                                                     "bg-neutral text-neutral-content p-6 rounded-2xl text-sm font-mono overflow-x-auto border-0 shadow-md" ]
                                               [ code [ _class "language-fsharp" ] [ str ex.Content ] ] ])) ] ]

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
            [ match e.Kind with
              | EntityKind.Record -> renderRecordEntity e
              | _ -> renderGenericEntity e ]

        match chrome with
        | Some value ->
            View.layoutWithChrome
                value
                e.Name
                context.AllPages
                context.Package
                context.Config
                context.Versions
                context.Theme
                context.RootPath
                context.SiteRootPath
                ("api/" + e.Id + ".html")
                content
        | None ->
            View.layout
                e.Name
                context.AllPages
                context.Package
                context.Config
                context.Versions
                context.Theme
                context.RootPath
                context.SiteRootPath
                ("api/" + e.Id + ".html")
                content
        |> fun node -> RenderView.AsString.htmlNode node

    let renderEntityPage entity context =
        renderEntityPageCore None entity context

    /// <summary>Generates a text-based summary of the API for LLM consumption.</summary>
    /// <param name="package">The package model to summarize.</param>
    /// <returns>A plaintext `llms.txt` document.</returns>
    /// <example name="GenerateLlmsTxtExample" data-livedocs="snapshot">
    /// > open FsLiveDocs.Core;;
    ///
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
