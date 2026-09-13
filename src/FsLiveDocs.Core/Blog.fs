namespace FsLiveDocs.Core

open System
open System.Text.RegularExpressions

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

    let estimatedReadingMinutes (page: ContentPage) =
        let text = Regex.Replace(page.ContentHtml, "<[^>]+>", " ")
        let words = Regex.Matches(System.Net.WebUtility.HtmlDecode text, @"\b[\p{L}\p{N}'][\p{L}\p{N}'-]*\b").Count
        max 1 (int (Math.Ceiling(float words / 200.0)))

    let excerpt (page: ContentPage) =
        page.Metadata.Summary
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.defaultWith (fun () ->
            let text = Regex.Replace(page.ContentHtml, "<[^>]+>", " ") |> System.Net.WebUtility.HtmlDecode |> fun value -> Regex.Replace(value, @"\s+", " ").Trim()
            if text.Length <= 240 then text else text.Substring(0, 237).TrimEnd() + "...")
