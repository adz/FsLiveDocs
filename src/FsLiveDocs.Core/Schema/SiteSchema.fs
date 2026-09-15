namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the site-shell configuration and page frontmatter persisted inside a
/// `ReleaseContentArtifact`. Wire-compatible with the pre-Reified Newtonsoft format: see
/// `DocumentationSchema` for the shared compatibility rules.
module SiteSchema =

    let navigationItem : Schema<NavigationItem> =
        schema<NavigationItem> {
            fieldAs "Label" (fun (n: NavigationItem) -> n.Label) { withSchema Schema.text }
            fieldAs "Href" (fun (n: NavigationItem) -> n.Href) { withSchema Schema.text }
            construct (fun label href -> { Label = label; Href = href })
        }

    type private CommentsProviderWire =
        { Kind: string
          Html: string option
          Repo: string option
          RepoId: string option
          Category: string option
          CategoryId: string option
          Theme: string option }

    /// Portable, explicitly tagged comment-provider configuration (`kind` plus that provider's fields).
    let commentsProvider : Schema<CommentsProvider> =
        let wire =
            schema<CommentsProviderWire> {
                field _.Kind
                field _.Html
                field _.Repo
                field _.RepoId
                field _.Category
                field _.CategoryId
                field _.Theme
                construct (fun kind html repo repoId category categoryId theme ->
                    { Kind = kind; Html = html; Repo = repo; RepoId = repoId; Category = category; CategoryId = categoryId; Theme = theme })
            }
        let empty = { Kind = ""; Html = None; Repo = None; RepoId = None; Category = None; CategoryId = None; Theme = None }
        let required name (value: string option) =
            match value with
            | Some text when not (System.String.IsNullOrWhiteSpace text) -> text
            | _ -> invalidOp $"commentsProvider.{name} is required."
        let toProvider (w: CommentsProviderWire) =
            match w.Kind.ToLowerInvariant() with
            | "none" -> NoComments
            | "custom" -> Custom(required "html" w.Html)
            | "giscus" ->
                Giscus
                    { Repo = required "repo" w.Repo
                      RepoId = required "repoId" w.RepoId
                      Category = required "category" w.Category
                      CategoryId = required "categoryId" w.CategoryId
                      Theme = w.Theme }
            | kind -> invalidOp $"Unsupported commentsProvider kind '{kind}'."
        let toWire =
            function
            | NoComments -> { empty with Kind = "none" }
            | Custom html -> { empty with Kind = "custom"; Html = Some html }
            | Giscus g ->
                { empty with
                    Kind = "giscus"
                    Repo = Some g.Repo
                    RepoId = Some g.RepoId
                    Category = Some g.Category
                    CategoryId = Some g.CategoryId
                    Theme = g.Theme }
        wire |> Schema.convert toProvider toWire

    let blogListingOptions : Schema<BlogListingOptions> =
        schema<BlogListingOptions> {
            fieldAs "Layout" (fun (o: BlogListingOptions) -> o.Layout) { withSchema (Schema.option Schema.text) }
            fieldAs "Show" (fun (o: BlogListingOptions) -> o.Show) { withSchema (Schema.listWith Schema.text) }
            fieldAs "Limit" (fun (o: BlogListingOptions) -> o.Limit) { withSchema (Schema.option Schema.int) }
            fieldAs "Tag" (fun (o: BlogListingOptions) -> o.Tag) { withSchema (Schema.option Schema.text) }
            fieldAs "Category" (fun (o: BlogListingOptions) -> o.Category) { withSchema (Schema.option Schema.text) }
            construct (fun layout show limit tag category ->
                { Layout = layout; Show = show; Limit = limit; Tag = tag; Category = category })
        }

    let private dateOnly : Schema<System.DateOnly> =
        Schema.text
        |> Schema.convert
            (fun text -> System.DateOnly.ParseExact(text.Substring(0, 10), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            (fun date -> date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))

    let siteConfig : Schema<SiteConfig> =
        schema<SiteConfig> {
            fieldAs "RepoUrl" (fun (s: SiteConfig) -> s.RepoUrl) { withSchema (Schema.option Schema.text) }
            fieldAs "SiteName" (fun (s: SiteConfig) -> s.SiteName) { withSchema (Schema.option Schema.text) }
            fieldAs "LogoText" (fun (s: SiteConfig) -> s.LogoText) { withSchema (Schema.option Schema.text) }
            fieldAs "LogoPath" (fun (s: SiteConfig) -> s.LogoPath) { withSchema (Schema.option Schema.text) }
            fieldAs "LogoDarkPath" (fun (s: SiteConfig) -> s.LogoDarkPath) { withSchema (Schema.option Schema.text) }
            fieldAs "ShowSiteName" (fun (s: SiteConfig) -> s.ShowSiteName) { withSchema (Schema.option Schema.bool) }
            fieldAs "Stylesheet" (fun (s: SiteConfig) -> s.Stylesheet) { withSchema (Schema.option Schema.text) }
            fieldAs "Themes" (fun (s: SiteConfig) -> s.Themes) { withSchema (Schema.option (Schema.listWith Schema.text)) }
            fieldAs "Navigation" (fun (s: SiteConfig) -> s.Navigation) { withSchema (Schema.option (Schema.listWith navigationItem)) }
            fieldAs "FSharpPrelude" (fun (s: SiteConfig) -> s.FSharpPrelude) { withSchema (Schema.option Schema.text) }
            fieldAs "CommentsProvider" (fun (s: SiteConfig) -> s.CommentsProvider) { withSchema (Schema.option commentsProvider) }
            construct (fun repoUrl siteName logoText logoPath logoDarkPath showSiteName stylesheet themes navigation fsharpPrelude commentsProvider ->
                { RepoUrl = repoUrl
                  SiteName = siteName
                  LogoText = logoText
                  LogoPath = logoPath
                  LogoDarkPath = logoDarkPath
                  ShowSiteName = showSiteName
                  Stylesheet = stylesheet
                  Themes = themes
                  Navigation = navigation
                  FSharpPrelude = fsharpPrelude
                  CommentsProvider = commentsProvider })
        }

    /// The `.livedocs/config.json` spelling of `SiteConfig`: camelCase keys, every one optional.
    /// Deliberately separate from `siteConfig`, whose PascalCase shape is the persisted capsule
    /// wire format.
    let siteConfigFile : Schema<SiteConfig> =
        let navigationFileItem =
            schema<NavigationItem> {
                field _.Label
                field _.Href
                construct (fun label href -> { Label = label; Href = href })
            }
        schema<SiteConfig> {
            field _.RepoUrl
            field _.SiteName
            field _.LogoText
            field _.LogoPath
            field _.LogoDarkPath
            field _.ShowSiteName
            field _.Stylesheet
            field _.Themes
            field _.Navigation { withSchema (Schema.option (Schema.listWith navigationFileItem) |> Schema.mayOmit) }
            field (fun (s: SiteConfig) -> s.FSharpPrelude)
            field _.CommentsProvider { withSchema (Schema.option commentsProvider |> Schema.mayOmit) }
            construct (fun repoUrl siteName logoText logoPath logoDarkPath showSiteName stylesheet themes navigation fsharpPrelude commentsProvider ->
                { RepoUrl = repoUrl
                  SiteName = siteName
                  LogoText = logoText
                  LogoPath = logoPath
                  LogoDarkPath = logoDarkPath
                  ShowSiteName = showSiteName
                  Stylesheet = stylesheet
                  Themes = themes
                  Navigation = navigation
                  FSharpPrelude = fsharpPrelude
                  CommentsProvider = commentsProvider })
        }

    let contentMetadata : Schema<ContentMetadata> =
        schema<ContentMetadata> {
            fieldAs "Title" (fun (m: ContentMetadata) -> m.Title) { withSchema Schema.text }
            fieldAs "Type" (fun (m: ContentMetadata) -> m.Type) { withSchema (Schema.option Schema.text) }
            fieldAs "Project" (fun (m: ContentMetadata) -> m.Project) { withSchema (Schema.option Schema.text) }
            fieldAs "TargetFramework" (fun (m: ContentMetadata) -> m.TargetFramework) { withSchema (Schema.option Schema.text) }
            fieldAs "Platform" (fun (m: ContentMetadata) -> m.Platform) { withSchema (Schema.option Schema.text) }
            fieldAs "Date" (fun (m: ContentMetadata) -> m.Date) { withSchema (Schema.option dateOnly) }
            fieldAs "Tags" (fun (m: ContentMetadata) -> m.Tags) { withSchema (Schema.listWith Schema.text) }
            fieldAs "Category" (fun (m: ContentMetadata) -> m.Category) { withSchema (Schema.option Schema.text) }
            fieldAs "Draft" (fun (m: ContentMetadata) -> m.Draft) { withSchema Schema.bool }
            fieldAs "Summary" (fun (m: ContentMetadata) -> m.Summary) { withSchema (Schema.option Schema.text) }
            fieldAs "Slug" (fun (m: ContentMetadata) -> m.Slug) { withSchema (Schema.option Schema.text) }
            fieldAs "Series" (fun (m: ContentMetadata) -> m.Series) { withSchema (Schema.option Schema.text) }
            fieldAs "SeriesOrder" (fun (m: ContentMetadata) -> m.SeriesOrder) { withSchema (Schema.option Schema.int) }
            fieldAs "Comments" (fun (m: ContentMetadata) -> m.Comments) { withSchema Schema.bool }
            fieldAs "BlogList" (fun (m: ContentMetadata) -> m.BlogList) { withSchema (Schema.option blogListingOptions) }
            construct (fun title type_ project targetFramework platform date tags category draft summary slug series seriesOrder comments blogList ->
                { Title = title
                  Type = type_
                  Project = project
                  TargetFramework = targetFramework
                  Platform = platform
                  Date = date
                  Tags = tags
                  Category = category
                  Draft = draft
                  Summary = summary
                  Slug = slug
                  Series = series
                  SeriesOrder = seriesOrder
                  Comments = comments
                  BlogList = blogList })
        }
