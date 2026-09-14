namespace FsLiveDocs.Core

open System
open System.Text.RegularExpressions
open Markdig
open Markdig.Syntax

/// Renderer-neutral blog projections, recomputed from discovered pages for every build.
module Blog =
    type PostIndex = {
        ByDateDesc: ContentPage list
        ByTag: Map<string, ContentPage list>
        ByCategory: Map<string, ContentPage list>
    }

    type SeriesEntry = {
        Post: ContentPage
        PartNumber: int
        PartCount: int
    }

    type SeriesIndex = { BySeriesName: Map<string, SeriesEntry list> }

    type Navigation = {
        Prev: ContentPage option
        Next: ContentPage option
        SeriesPart: (int * int) option
    }

    let private compareByDateDescending (left: ContentPage) (right: ContentPage) =
        compare right.Metadata.Date left.Metadata.Date

    let private normalizeKey (value: string) = value.Trim().ToLowerInvariant()

    let eligible includeDrafts (page: ContentPage) =
        page.Metadata.Date.IsSome && (includeDrafts || not page.Metadata.Draft)

    let buildPostIndex includeDrafts (pages: ContentPage list) =
        let posts = pages |> List.filter (eligible includeDrafts) |> List.sortWith compareByDateDescending
        let grouped selector =
            posts
            |> List.collect (fun page -> selector page |> List.map (fun key -> normalizeKey key, page))
            |> List.groupBy fst
            |> List.map (fun (key, entries) -> key, entries |> List.map snd |> List.sortWith compareByDateDescending)
            |> Map.ofList
        { ByDateDesc = posts
          ByTag = grouped (fun page -> page.Metadata.Tags |> List.filter (String.IsNullOrWhiteSpace >> not))
          ByCategory = grouped (fun page -> page.Metadata.Category |> Option.toList) }

    let buildSeriesIndex includeDrafts (pages: ContentPage list) =
        pages
        |> List.filter (eligible includeDrafts)
        |> List.choose (fun page -> page.Metadata.Series |> Option.map (fun series -> normalizeKey series, page))
        |> List.groupBy fst
        |> List.map (fun (series, entries) ->
            let ordered =
                entries
                |> List.map snd
                |> List.sortBy (fun page -> page.Metadata.SeriesOrder, page.Metadata.Date, page.OutputPath)
            let count = ordered.Length
            series,
            (ordered |> List.mapi (fun index post -> { Post = post; PartNumber = index + 1; PartCount = count })))
        |> Map.ofList
        |> fun bySeriesName -> { BySeriesName = bySeriesName }

    let navigation (page: ContentPage) (posts: PostIndex) (series: SeriesIndex) =
        let neighboring entries current =
            match entries |> List.tryFindIndex (fun item -> item.OutputPath = current.OutputPath) with
            | None -> None, None
            | Some index ->
                (if index > 0 then Some entries.[index - 1] else None),
                (if index + 1 < entries.Length then Some entries.[index + 1] else None)

        match page.Metadata.Series |> Option.map normalizeKey |> Option.bind (fun key -> series.BySeriesName |> Map.tryFind key) with
        | Some entries ->
            let seriesPosts = entries |> List.map _.Post
            let prev, next = neighboring seriesPosts page
            let part = entries |> List.tryFind (fun entry -> entry.Post.OutputPath = page.OutputPath) |> Option.map (fun entry -> entry.PartNumber, entry.PartCount)
            { Prev = prev; Next = next; SeriesPart = part }
        | None ->
            let prev, next = neighboring posts.ByDateDesc page
            { Prev = prev; Next = next; SeriesPart = None }

    let private markdownPipeline = Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

    let private shortcodePattern = Regex(@"{{<.*?>}}", RegexOptions.Singleline)

    /// Parses the canonical Markdown body with shortcodes removed, so neither excerpts nor reading
    /// time count shortcode syntax as prose.
    let private parseBody (page: ContentPage) =
        Markdig.Markdown.Parse(shortcodePattern.Replace(page.Markdown, " "), markdownPipeline)

    let rec private inlineText (inline': Markdig.Syntax.Inlines.Inline) =
        match inline' with
        | :? Markdig.Syntax.Inlines.LiteralInline as literal -> literal.Content.ToString()
        | :? Markdig.Syntax.Inlines.CodeInline as code -> code.Content
        | :? Markdig.Syntax.Inlines.LineBreakInline -> " "
        | :? Markdig.Syntax.Inlines.HtmlInline -> ""
        | :? Markdig.Syntax.Inlines.ContainerInline as container -> container |> Seq.map inlineText |> String.concat ""
        | _ -> ""

    let private leafText (block: Markdig.Syntax.LeafBlock) =
        match block.Inline with
        | null -> ""
        | inlines -> inlines |> Seq.map inlineText |> String.concat "" |> fun text -> Regex.Replace(text, @"\s+", " ").Trim()

    let private wordPattern = Regex(@"\b[\p{L}\p{N}'][\p{L}\p{N}'-]*\b")

    /// Estimated minutes to read the post's prose (paragraphs, headings, lists, quotes; code blocks
    /// excluded) at 200 words per minute, never less than one.
    let estimatedReadingMinutes (page: ContentPage) =
        let words =
            (parseBody page).Descendants<LeafBlock>()
            |> Seq.filter (fun block -> not (block :? Markdig.Syntax.CodeBlock))
            |> Seq.sumBy (fun block -> wordPattern.Matches(leafText block).Count)
        max 1 (int (Math.Ceiling(float words / 200.0)))

    /// The authored summary, or the first Markdown paragraph truncated at a word boundary.
    let excerpt (page: ContentPage) =
        page.Metadata.Summary
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.defaultWith (fun () ->
            let document = parseBody page
            let text =
                document.Descendants<Markdig.Syntax.ParagraphBlock>()
                |> Seq.map leafText
                |> Seq.tryFind (String.IsNullOrWhiteSpace >> not)
                |> Option.defaultValue ""
            if text.Length <= 240 then text
            else
                let cut = text.LastIndexOf(' ', 237)
                text.Substring(0, (if cut > 0 then cut else 237)).TrimEnd() + "...")

    /// Whether the page's Markdown uses a shortcode as its own paragraph (the form the renderer
    /// expands), ignoring code spans and code blocks that merely show the shortcode's syntax.
    let private usesShortcodeOutsideCode (name: string) (page: ContentPage) =
        let pattern = Regex(@"^{{<\s*" + Regex.Escape name + @"\b.*>}}$", RegexOptions.Singleline)
        let rec literalText (inline': Inlines.Inline) =
            match inline' with
            | :? Inlines.LiteralInline as literal -> literal.Content.ToString()
            | :? Inlines.CodeInline -> " "
            | :? Inlines.ContainerInline as container -> container |> Seq.map literalText |> String.concat ""
            | _ -> " "
        Markdown.Parse(page.Markdown, markdownPipeline).Descendants<LeafBlock>()
        |> Seq.exists (fun block ->
            match block with
            | :? CodeBlock -> false
            | _ when isNull block.Inline -> false
            | :? ParagraphBlock -> pattern.IsMatch((block.Inline |> Seq.map literalText |> String.concat "").Trim())
            | _ -> false)

    /// Authoring problems that do not stop a build but produce surprising output.
    let diagnostics (pages: ContentPage list) =
        pages
        |> List.choose (fun page ->
            if page.Metadata.Series.IsNone && usesShortcodeOutsideCode "series-nav" page then
                Some $"{page.FilePath}: {{{{< series-nav >}}}} is used on a page without 'series' frontmatter, so it renders nothing."
            else
                None)
