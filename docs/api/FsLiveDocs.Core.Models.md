# FsLiveDocs.Core models

Core models separate release data from diagnostics, runner results, and rendered site state.

`ApiModelArtifact` stores the public API graph and `DocumentationNode` values.

`SemanticDocumentationArtifact` stores compiler-derived code meaning.

`ReleaseContentArtifact` stores canonical Markdown, metadata, assets, and site configuration.

`ReleaseCapsuleManifest` binds components to a product version, source revision, sizes, and checksums.

Each persisted component owns its schema version.
