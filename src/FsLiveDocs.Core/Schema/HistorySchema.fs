namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schema for the local build manifest persisted at each history entry's model root.
/// Wire-compatible with the pre-Reified Newtonsoft format: see `DocumentationSchema` for the
/// shared compatibility rules.
module HistorySchema =

    let historyEntry : Schema<HistoryEntry> =
        schema<HistoryEntry> {
            fieldAs "Version" (fun (e: HistoryEntry) -> e.Version) { withSchema Schema.text }
            fieldAs "ModelPath" (fun (e: HistoryEntry) -> e.ModelPath) { withSchema Schema.text }
            fieldAs "ModelSha256" (fun (e: HistoryEntry) -> e.ModelSha256) { withSchema Schema.text }
            fieldAs "SemanticPath" (fun (e: HistoryEntry) -> e.SemanticPath) { withSchema (Schema.option Schema.text) }
            fieldAs "SemanticSha256" (fun (e: HistoryEntry) -> e.SemanticSha256) { withSchema (Schema.option Schema.text) }
            fieldAs "DocsPath" (fun (e: HistoryEntry) -> e.DocsPath) { withSchema Schema.text }
            construct (fun version modelPath modelSha256 semanticPath semanticSha256 docsPath ->
                ({ Version = version
                   ModelPath = modelPath
                   ModelSha256 = modelSha256
                   SemanticPath = semanticPath
                   SemanticSha256 = semanticSha256
                   DocsPath = docsPath }: HistoryEntry))
        }

    let historyManifest : Schema<HistoryManifest> =
        schema<HistoryManifest> {
            fieldAs "SchemaVersion" (fun (m: HistoryManifest) -> m.SchemaVersion) { withSchema Schema.int }
            fieldAs "CurrentVersion" (fun (m: HistoryManifest) -> m.CurrentVersion) { withSchema Schema.text }
            fieldAs "Entries" (fun (m: HistoryManifest) -> m.Entries) { withSchema (Schema.listWith historyEntry) }
            construct (fun schemaVersion currentVersion entries ->
                ({ SchemaVersion = schemaVersion; CurrentVersion = currentVersion; Entries = entries }: HistoryManifest))
        }
