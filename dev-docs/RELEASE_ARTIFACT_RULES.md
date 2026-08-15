# Release artifact rules

## Scope and compatibility promise

These rules define the FsLiveDocs 1.0 persisted release interface. They apply to release extraction, persisted models, schema evolution, documentation discovery, history builds, and rendering.

A conflicting change requires an architecture decision and a documented migration. Renderer convenience does not justify weakening this interface.

FsLiveDocs analyzes a project at release time and stores the meaning required to render that release again. Future versions must render it without compiling the historical project or restoring its toolchain.

The reproducible unit is a **release capsule**. Capsule content and compiler-derived meaning are immutable. Generated HTML is not immutable.

## Release capsule contents

A complete release capture contains four independently versioned, checksum-protected components:

| Component | Required content |
| --- | --- |
| API artifact | Public symbol graph and structured API documentation |
| Semantic artifact | Compiler-derived information for displayed F# blocks |
| Content artifact | Canonical documentation pages, assets, and site metadata |
| Manifest | Product version, source revision, schema versions, paths, sizes, and SHA-256 checksums |

Components may remain separate during local development. Publish them as one archive so an incomplete or mismatched release cannot appear valid.

## Persisted data requirements

Persist only FsLiveDocs-owned, renderer-neutral meaning.

Do not persist:

- generated HTML or page fragments;
- CSS classes, styles, themes, or template markup;
- DOM or tooltip element identifiers;
- browser interaction choices;
- FSharp.Formatting, FSharp.Compiler.Service, compiler, Markdown parser, or template-engine objects;
- URLs derived from the current page layout.

### API artifact

Store plain signature and type text plus structured documentation nodes. Nodes may represent text, paragraphs, code, lists, symbol references, and external links.

Do not store presentation fields such as `SummaryHtml`, `RemarksHtml`, or `DescriptionHtml`.

### Semantic artifact

Store:

- original token text;
- FsLiveDocs-owned classifications;
- block-local tooltip references;
- structured or plain semantic documentation;
- diagnostics;
- stable block IDs;
- source and context hashes.

Unknown future token classifications may degrade to plain text only when the schema compatibility rules permit it.

### Content artifact

Store canonical renderer-neutral Markdown, assets, source and provenance metadata, and version-specific site configuration.

Prefer Markdown produced after deterministic shortcode and transclusion expansion. Stored expanded content prevents later shortcode changes or missing repository source from changing an old release.

## Documentation discovery and verification

Use one deterministic discovery pipeline for Markdown fences, transcluded snippets, API enrichment, and XML examples.

Build, audit, generated verification, semantic extraction, release capture, and rendering must consume the same discovery result.

Discovery emits stable verification cases. Execute every case through one public generated-verification interface. Callers must not reconstruct action ordering or composition.

Ordinary blocks compile but do not execute. Only explicitly executable modes run.

Stable identity requires all of the following:

- normalize documentation-relative paths and line endings;
- preserve meaningful whitespace;
- assign stable block IDs;
- hash displayed source with its semantic mode;
- hash preparation and page context that can change meaning;
- reject duplicate, missing, or mismatched blocks.

If a release declares semantic data, a mismatch is an error. Do not silently fall back to syntax-only rendering.

Releases created before semantic artifacts existed may use the documented syntax-only fallback.

## Capture requirements

Release capture may:

- compile projects;
- evaluate MSBuild;
- load compiler services;
- expand source transclusions;
- verify examples.

Capture must fail instead of publishing incomplete or untrustworthy semantic data.

The `capture` command performs audit, verification, extraction, packaging, size reporting, and checksum generation. It must not upload unless the user invokes an explicit publishing workflow.

## Historical rendering requirements

A history render may:

- verify checksums and supported schemas;
- apply explicit deterministic migrations;
- parse stored structured documentation and Markdown;
- resolve stored symbol references against the stored API graph;
- match semantic blocks by stable identity and hashes;
- generate current HTML, CSS, JavaScript, links, and accessible tooltip behavior.

A history render must not compile, restore, or load the historical project. It must not require historical SDKs, packages, binaries, source generators, or FSharp.Formatting versions.

## Schema compatibility

Each artifact component owns its schema version. A version describes an exact persisted contract. Do not rely on missing fields receiving empty values during deserialization.

Follow these rules:

- Make an explicit compatibility decision for additive and incompatible changes.
- Increment the component schema version for breaking representation changes.
- Support an explicit set of known versions.
- Reject unknown versions with a clear error.
- Migrate supported old versions with small, deterministic migrations.
- Do not use reflection-based generic migration or best-effort deserialization.
- Never regenerate a published artifact under the same product version.
- Verify every published component and archive with SHA-256 before rendering.

Compatibility preserves historical content and semantic meaning. It does not require byte-identical HTML.

Renderer pinning, if added, is a separate optional reproducibility feature.

## Storage and distribution

Use the following ownership model:

| Location | Purpose |
| --- | --- |
| Git tag | Human-authored source and provenance |
| Immutable release asset | Complete compressed capsule and checksum |
| Documentation host | Generated static output |
| `.livedocs/cache/` | Disposable local analysis caches |
| `.livedocs/releases/` | Downloaded capsules; ignored by Git |

A Git tag provides provenance but is not the reproduction unit. The capsule must render without reconstructing the historical build environment.

Do not commit generated artifacts to the main development branch by default.

## Security

Treat all artifact text as untrusted input during rendering.

Renderers must encode plain text, validate external URLs, resolve symbol references through owned identifiers, and construct markup instead of trusting stored HTML.

Archive extraction must reject path traversal and files outside the destination.

Checksums establish integrity, not authorship. Signing and build provenance may be added without changing renderer-neutral component schemas.

## Default CLI workflow

The normal user path is:

```text
livedocs init
livedocs capture <projects...> --version <version> --output <capsule>
livedocs build-history <history-index>
```

Generated CI must attach the capsule to the matching immutable release. It must not publish when verification or capture fails.
