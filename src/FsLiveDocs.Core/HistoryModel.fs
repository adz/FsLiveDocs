namespace FsLiveDocs.Core

/// <summary>One immutable API model and tagged documentation source in a history build.</summary>
type HistoryEntry = {
    Version: string
    ModelPath: string
    ModelSha256: string
    SemanticPath: string option
    SemanticSha256: string option
    DocsPath: string
}

/// <summary>Local build manifest materialized from a repository's durable release history.</summary>
type HistoryManifest = {
    SchemaVersion: int
    CurrentVersion: string
    Entries: HistoryEntry list
}
