namespace FsLiveDocs.Core

open System

/// <summary>Build-time configuration for the generated documentation site.</summary>
type NavigationItem = {
    /// <summary>Text displayed in the top navigation.</summary>
    Label: string
    /// <summary>Root-relative site path or absolute URL.</summary>
    Href: string
}

/// <summary>Build-time configuration for the generated documentation site.</summary>
type SiteConfig = {
    /// <summary>Optional repository URL used to build source links for members.</summary>
    RepoUrl: string option
    /// <summary>Optional consumer name used in the navbar and page titles.</summary>
    SiteName: string option
    /// <summary>Optional short consumer mark used in the navbar.</summary>
    LogoText: string option
    /// <summary>Optional root-relative or absolute image used as the navbar logo.</summary>
    LogoPath: string option
    /// <summary>Optional dark-theme variant of <c>LogoPath</c>.</summary>
    LogoDarkPath: string option
    /// <summary>Whether to display the site name beside the navbar mark. Defaults to true.</summary>
    ShowSiteName: bool option
    /// <summary>Optional root-relative or absolute consumer stylesheet loaded after FsLiveDocs styles.</summary>
    Stylesheet: string option
    /// <summary>Optional DaisyUI themes exposed by the theme picker. Defaults to the built-in theme set.</summary>
    Themes: string list option
    /// <summary>Optional top-level navigation. Defaults to Home and API.</summary>
    Navigation: NavigationItem list option
    /// <summary>Repository-owned F# setup compiled and shown on every page containing checked F#.</summary>
    FSharpPrelude: string option
    /// <summary>Optional settings for comments embedded on posts that opt in.</summary>
    CommentsProvider: CommentsProvider option
}

and GiscusSettings = {
    Repo: string
    RepoId: string
    Category: string
    CategoryId: string
    Theme: string option
}

and CommentsProvider =
    | NoComments
    | Giscus of GiscusSettings
    | Custom of rawHtml: string

/// <summary>Selection and presentation defaults for a page that renders a post listing.</summary>
[<CLIMutable>]
type BlogListingOptions = {
    Layout: string option
    Show: string list
    Limit: int option
    Tag: string option
    Category: string option
}

/// <summary>Resolved project paths and namespace information used by the doc-test runner.</summary>
type ResolvedProject = {
    /// <summary>The path to the source project file.</summary>
    ProjectPath: string
    /// <summary>The path to the built assembly used by FSI.</summary>
    AssemblyPath: string
    /// <summary>The project namespace opened before executing examples.</summary>
    ProjectNamespace: string
}

/// <summary>Metadata extracted from Markdown frontmatter.</summary>
[<CLIMutable>]
type ContentMetadata = {
    /// <summary>Title of the page.</summary>
    Title: string
    /// <summary>Optional category or type identifier.</summary>
    Type: string option
    /// <summary>Optional documentation project used to compile code blocks on this page.</summary>
    Project: string option
    /// <summary>Optional target framework used to compile code blocks on this page.</summary>
    TargetFramework: string option
    /// <summary>Optional runtime/compiler platform described by this page, such as fable.</summary>
    Platform: string option
    /// <summary>Optional publication date. Dated pages participate in blog output.</summary>
    Date: DateOnly option
    /// <summary>Free-form blog topics.</summary>
    Tags: string list
    /// <summary>Optional primary blog classification.</summary>
    Category: string option
    /// <summary>Whether this page is omitted unless draft rendering is enabled.</summary>
    Draft: bool
    /// <summary>Optional authored excerpt for blog listings and feeds.</summary>
    Summary: string option
    /// <summary>Optional stable flat permalink segment for a blog post.</summary>
    Slug: string option
    /// <summary>Optional ordered narrative grouping.</summary>
    Series: string option
    /// <summary>Optional explicit order inside a series.</summary>
    SeriesOrder: int option
    /// <summary>Whether the configured comments provider is rendered for this page.</summary>
    Comments: bool
    /// <summary>Optional defaults consumed by a <c>{{&lt; posts &gt;}}</c> shortcode on this page.</summary>
    BlogList: BlogListingOptions option
}

/// Defaults for pages without frontmatter and explicit schema migrations.
module ContentMetadata =
    let empty title =
        { Title = title
          Type = None
          Project = None
          TargetFramework = None
          Platform = None
          Date = None
          Tags = []
          Category = None
          Draft = false
          Summary = None
          Slug = None
          Series = None
          SeriesOrder = None
          Comments = false
          BlogList = None }

/// <summary>A processed documentation page.</summary>
type ContentPage = {
    /// <summary>Frontmatter metadata.</summary>
    Metadata: ContentMetadata
    /// <summary>Rendered HTML content.</summary>
    ContentHtml: string
    /// <summary>Relative file path from the docs root.</summary>
    FilePath: string
    /// <summary>Relative HTML output path, with documentation ordering prefixes removed.</summary>
    OutputPath: string
    /// <summary>Ordering prefix of the top-level documentation section.</summary>
    SectionOrder: int
}
