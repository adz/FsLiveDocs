namespace FsLiveDocs.Core

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
}

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
