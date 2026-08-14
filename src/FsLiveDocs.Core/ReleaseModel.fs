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

/// Summary returned after a release capsule is written or inspected.
type ReleaseCapsuleReport = {
    Path: string
    Sha256: string
    CompressedSize: int64
    Manifest: ReleaseCapsuleManifest
}

/// One immutable capsule referenced by a history index.
type ReleaseHistoryEntry = {
    Version: string
    CapsulePath: string
    CapsuleSha256: string
}

/// A concise local history index for complete release capsules.
type ReleaseHistoryIndex = {
    SchemaVersion: int
    CurrentVersion: string
    Entries: ReleaseHistoryEntry list
}
