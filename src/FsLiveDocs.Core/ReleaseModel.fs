namespace FsLiveDocs.Core

/// One canonical Markdown page captured for a documentation release.
type ReleaseContentPage = {
    SourcePath: string
    Metadata: ContentMetadata
    Markdown: string
}

/// One non-Markdown file stored in the release capsule.
type ReleaseAsset = {
    Path: string
    MediaType: string
    Sha256: string
    Size: int64
}

/// Renderer-neutral documentation content for one release.
type ReleaseContentArtifact = {
    SchemaVersion: int
    Pages: ReleaseContentPage list
    Assets: ReleaseAsset list
    Site: SiteConfig
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
