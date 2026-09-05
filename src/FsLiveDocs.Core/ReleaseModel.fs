namespace FsLiveDocs.Core

/// One canonical Markdown page captured for a documentation release.
type ReleaseContentPage =
    {
        SourcePath: string
        /// The <see cref="ReleaseDocsSet"/> this page belongs to.
        SetId: string
        Metadata: ContentMetadata
        Markdown: string
    }

/// Renderer-neutral identity and API surface for one captured documentation set.
type ReleaseDocsSet =
    {
        Id: string
        Title: string
        /// Repository-relative Markdown root recorded as release provenance.
        Source: string
        /// Route prefix. "" for the site-root default set; otherwise a normalized slash path.
        Path: string
        /// Repository-relative projects selected by the set at capture time.
        Projects: string list
        /// Whether this set renders at the site root.
        IsDefault: bool
        /// Whether the set renders its own isolated sidebar.
        Sidebar: bool
        /// Whether the set renders an API reference.
        Api: bool
        /// Entity ids from the shared API artifact whose pages, sidebar, and index this set exposes.
        ApiEntityIds: string list
        /// Repository-owned F# setup used to check and render this set's F# blocks.
        FSharpPrelude: string option
    }

/// One non-Markdown file stored in the release capsule.
type ReleaseAsset = {
    Path: string
    MediaType: string
    Sha256: string
    Size: int64
}

/// Renderer-neutral documentation content for one release.
type ReleaseContentArtifact =
    {
        SchemaVersion: int
        /// False preserves the historical single-tree route contract; true enables contextual sets.
        UsesDocumentationSets: bool
        Pages: ReleaseContentPage list
        Assets: ReleaseAsset list
        Site: SiteConfig
        /// The documentation sets this release renders. A schema-1 capsule has one implicit default set.
        DocsSets: ReleaseDocsSet list
    }

/// Integrity metadata for one component inside a release capsule.
type ReleaseComponent = {
    SchemaVersion: int
    Path: string
    Sha256: string
    Size: int64
}

/// The self-contained, immutable input to a future history render.
type ReleaseCapsuleManifest = {
    SchemaVersion: int
    ProductVersion: string
    SourceRevision: string
    CaptureToolVersion: string
    Api: ReleaseComponent
    Semantic: ReleaseComponent
    Content: ReleaseComponent
}

/// Inventory counts that make release capture observable without opening the capsule.
type ReleaseCaptureCounts = {
    Entities: int
    Members: int
    Examples: int
    DocumentationNodes: int
    Pages: int
    CodeBlocks: int
    Tooltips: int
    Diagnostics: int
    Assets: int
}

/// Summary returned after a release capsule is written or inspected.
type ReleaseCapsuleReport = {
    Path: string
    Sha256: string
    CompressedSize: int64
    UncompressedSize: int64
    Manifest: ReleaseCapsuleManifest
    Counts: ReleaseCaptureCounts
}

/// One immutable capsule referenced by a history index.
type ReleaseHistoryEntry = {
    Version: string
    CapsulePath: string option
    CapsuleUrl: string option
    CapsuleSha256: string
}

/// A concise local history index for complete release capsules.
type ReleaseHistoryIndex = {
    SchemaVersion: int
    CurrentVersion: string
    Entries: ReleaseHistoryEntry list
}
