namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the release-capsule graph: captured content, integrity manifest, and the
/// local history index. Wire-compatible with the pre-Reified Newtonsoft format: see
/// `DocumentationSchema` for the shared compatibility rules.
module ReleaseSchema =

    let contentMetadata : Schema<ContentMetadata> = SiteSchema.contentMetadata
    let siteConfig : Schema<SiteConfig> = SiteSchema.siteConfig

    let releaseContentPage : Schema<ReleaseContentPage> =
        schema<ReleaseContentPage> {
            fieldAs "SourcePath" (fun (p: ReleaseContentPage) -> p.SourcePath) { withSchema Schema.text }
            fieldAs "SetId" (fun (p: ReleaseContentPage) -> p.SetId) { withSchema Schema.text }
            fieldAs "Metadata" (fun (p: ReleaseContentPage) -> p.Metadata) { withSchema contentMetadata }
            fieldAs "Markdown" (fun (p: ReleaseContentPage) -> p.Markdown) { withSchema Schema.text }
            construct (fun sourcePath setId metadata markdown ->
                { SourcePath = sourcePath; SetId = setId; Metadata = metadata; Markdown = markdown })
        }

    let releaseDocsSet : Schema<ReleaseDocsSet> =
        schema<ReleaseDocsSet> {
            fieldAs "Id" (fun (d: ReleaseDocsSet) -> d.Id) { withSchema Schema.text }
            fieldAs "Title" (fun (d: ReleaseDocsSet) -> d.Title) { withSchema Schema.text }
            fieldAs "Source" (fun (d: ReleaseDocsSet) -> d.Source) { withSchema Schema.text }
            fieldAs "Path" (fun (d: ReleaseDocsSet) -> d.Path) { withSchema Schema.text }
            fieldAs "Projects" (fun (d: ReleaseDocsSet) -> d.Projects) { withSchema (Schema.listWith Schema.text) }
            fieldAs "IsDefault" (fun (d: ReleaseDocsSet) -> d.IsDefault) { withSchema Schema.``bool`` }
            fieldAs "Sidebar" (fun (d: ReleaseDocsSet) -> d.Sidebar) { withSchema Schema.``bool`` }
            fieldAs "Api" (fun (d: ReleaseDocsSet) -> d.Api) { withSchema Schema.``bool`` }
            fieldAs "ApiEntityIds" (fun (d: ReleaseDocsSet) -> d.ApiEntityIds) { withSchema (Schema.listWith Schema.text) }
            fieldAs "FSharpPrelude" (fun (d: ReleaseDocsSet) -> d.FSharpPrelude) { withSchema (Schema.option Schema.text) }
            construct (fun id title source path projects isDefault sidebar api apiEntityIds fsharpPrelude ->
                { Id = id
                  Title = title
                  Source = source
                  Path = path
                  Projects = projects
                  IsDefault = isDefault
                  Sidebar = sidebar
                  Api = api
                  ApiEntityIds = apiEntityIds
                  FSharpPrelude = fsharpPrelude })
        }

    let releaseAsset : Schema<ReleaseAsset> =
        schema<ReleaseAsset> {
            fieldAs "Path" (fun (a: ReleaseAsset) -> a.Path) { withSchema Schema.text }
            fieldAs "MediaType" (fun (a: ReleaseAsset) -> a.MediaType) { withSchema Schema.text }
            fieldAs "Sha256" (fun (a: ReleaseAsset) -> a.Sha256) { withSchema Schema.text }
            fieldAs "Size" (fun (a: ReleaseAsset) -> a.Size) { withSchema Schema.``int64`` }
            construct (fun path mediaType sha256 size -> { Path = path; MediaType = mediaType; Sha256 = sha256; Size = size })
        }

    let releaseContentArtifact : Schema<ReleaseContentArtifact> =
        schema<ReleaseContentArtifact> {
            fieldAs "SchemaVersion" (fun (a: ReleaseContentArtifact) -> a.SchemaVersion) { withSchema Schema.``int`` }
            fieldAs "UsesDocumentationSets" (fun (a: ReleaseContentArtifact) -> a.UsesDocumentationSets) { withSchema Schema.``bool`` }
            fieldAs "Pages" (fun (a: ReleaseContentArtifact) -> a.Pages) { withSchema (Schema.listWith releaseContentPage) }
            fieldAs "Assets" (fun (a: ReleaseContentArtifact) -> a.Assets) { withSchema (Schema.listWith releaseAsset) }
            fieldAs "Site" (fun (a: ReleaseContentArtifact) -> a.Site) { withSchema siteConfig }
            fieldAs "DocsSets" (fun (a: ReleaseContentArtifact) -> a.DocsSets) { withSchema (Schema.listWith releaseDocsSet) }
            construct (fun schemaVersion usesDocumentationSets pages assets site docsSets ->
                { SchemaVersion = schemaVersion
                  UsesDocumentationSets = usesDocumentationSets
                  Pages = pages
                  Assets = assets
                  Site = site
                  DocsSets = docsSets })
        }

    let releaseComponent : Schema<ReleaseComponent> =
        schema<ReleaseComponent> {
            fieldAs "SchemaVersion" (fun (c: ReleaseComponent) -> c.SchemaVersion) { withSchema Schema.``int`` }
            fieldAs "Path" (fun (c: ReleaseComponent) -> c.Path) { withSchema Schema.text }
            fieldAs "Sha256" (fun (c: ReleaseComponent) -> c.Sha256) { withSchema Schema.text }
            fieldAs "Size" (fun (c: ReleaseComponent) -> c.Size) { withSchema Schema.``int64`` }
            construct (fun schemaVersion path sha256 size -> { SchemaVersion = schemaVersion; Path = path; Sha256 = sha256; Size = size })
        }

    let releaseCapsuleManifest : Schema<ReleaseCapsuleManifest> =
        schema<ReleaseCapsuleManifest> {
            fieldAs "SchemaVersion" (fun (m: ReleaseCapsuleManifest) -> m.SchemaVersion) { withSchema Schema.``int`` }
            fieldAs "ProductVersion" (fun (m: ReleaseCapsuleManifest) -> m.ProductVersion) { withSchema Schema.text }
            fieldAs "SourceRevision" (fun (m: ReleaseCapsuleManifest) -> m.SourceRevision) { withSchema Schema.text }
            fieldAs "CaptureToolVersion" (fun (m: ReleaseCapsuleManifest) -> m.CaptureToolVersion) { withSchema Schema.text }
            fieldAs "Api" (fun (m: ReleaseCapsuleManifest) -> m.Api) { withSchema releaseComponent }
            fieldAs "Semantic" (fun (m: ReleaseCapsuleManifest) -> m.Semantic) { withSchema releaseComponent }
            fieldAs "Content" (fun (m: ReleaseCapsuleManifest) -> m.Content) { withSchema releaseComponent }
            construct (fun schemaVersion productVersion sourceRevision captureToolVersion api semantic content ->
                { SchemaVersion = schemaVersion
                  ProductVersion = productVersion
                  SourceRevision = sourceRevision
                  CaptureToolVersion = captureToolVersion
                  Api = api
                  Semantic = semantic
                  Content = content })
        }

    let releaseHistoryEntry : Schema<ReleaseHistoryEntry> =
        schema<ReleaseHistoryEntry> {
            fieldAs "Version" (fun (e: ReleaseHistoryEntry) -> e.Version) { withSchema Schema.text }
            fieldAs "CapsulePath" (fun (e: ReleaseHistoryEntry) -> e.CapsulePath) { withSchema (Schema.option Schema.text) }
            fieldAs "CapsuleUrl" (fun (e: ReleaseHistoryEntry) -> e.CapsuleUrl) { withSchema (Schema.option Schema.text) }
            fieldAs "CapsuleSha256" (fun (e: ReleaseHistoryEntry) -> e.CapsuleSha256) { withSchema Schema.text }
            construct (fun version capsulePath capsuleUrl capsuleSha256 ->
                { Version = version; CapsulePath = capsulePath; CapsuleUrl = capsuleUrl; CapsuleSha256 = capsuleSha256 })
        }

    let releaseHistoryIndex : Schema<ReleaseHistoryIndex> =
        schema<ReleaseHistoryIndex> {
            fieldAs "SchemaVersion" (fun (i: ReleaseHistoryIndex) -> i.SchemaVersion) { withSchema Schema.``int`` }
            fieldAs "CurrentVersion" (fun (i: ReleaseHistoryIndex) -> i.CurrentVersion) { withSchema Schema.text }
            fieldAs "Entries" (fun (i: ReleaseHistoryIndex) -> i.Entries) { withSchema (Schema.listWith releaseHistoryEntry) }
            construct (fun schemaVersion currentVersion entries ->
                { SchemaVersion = schemaVersion; CurrentVersion = currentVersion; Entries = entries })
        }
