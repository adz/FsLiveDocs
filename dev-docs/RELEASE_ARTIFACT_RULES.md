# Release artifact rules

## Status

Normative repository rules for the FsLiveDocs 1.0 persisted release interface.

These rules govern release extraction, persisted models, schema evolution, documentation
discovery, history builds, and rendering. A change that conflicts with them requires an explicit
architecture decision and a documented migration; convenience inside the current renderer is not
enough reason to weaken them.

## Product promise

FsLiveDocs analyzes a project once at release time and stores the meaning needed to render that
release again. A later FsLiveDocs version must be able to render the stored release with its current
HTML, CSS, JavaScript, accessibility behavior, and visual design without compiling the historical
project or restoring its historical toolchain.

The reproducible unit is a release capsule. Its content and compiler-derived meaning are immutable;
its generated HTML is deliberately not immutable.

## Required release inputs

A complete release capture consists of independently versioned, checksum-protected inputs:

1. An API artifact containing the public symbol graph and structured API documentation.
2. A semantic artifact containing compiler-derived information for displayed F# blocks.
3. A content artifact containing canonical documentation pages, assets, and site metadata.
4. A manifest identifying the product version, source revision, component schema versions, paths,
   sizes, and SHA-256 checksums.

These components may be separate during local development. Published releases should be packaged
as one archive so a partially uploaded or mismatched release cannot appear valid.

## Persist meaning, never presentation

Persisted release artifacts must contain only FsLiveDocs-owned, renderer-neutral data.

They must not contain:

- generated HTML or pre-rendered page fragments;
- CSS class names, styles, theme choices, or template markup;
- DOM identifiers, tooltip element identifiers, or browser interaction choices;
- FSharp.Formatting, FSharp.Compiler.Service, compiler, Markdown-parser, or template-engine objects;
- renderer-specific URLs derived from the current page layout.

The API artifact stores plain signature/type text and structured documentation nodes. Those nodes
represent meaning such as text, paragraphs, code, lists, symbol references, and external links.
They do not store `SummaryHtml`, `RemarksHtml`, `DescriptionHtml`, or equivalent presentation fields.

The semantic artifact stores original token text, FsLiveDocs-owned classifications, block-local
tooltip references, structured or plain semantic documentation, diagnostics, stable block IDs, and
source/context hashes. Unknown future token classifications degrade to plain text only when the
schema's documented compatibility rules permit it.

The content artifact stores canonical renderer-neutral Markdown (preferably after deterministic
shortcode and transclusion expansion), assets, source/provenance metadata, and version-specific site
configuration. Persisting canonical expanded content prevents future shortcode behavior or missing
repository source from changing an old release.

## One canonical discovery result

Markdown fences, transcluded snippets, API enrichment, and XML examples enter one deterministic
documentation discovery pipeline. Build, audit, generated verification, semantic extraction,
release capture, and rendering consume that same result.

Callers must not reconstruct the ordering or composition of verification actions. Discovery emits
stable verification-case values, and every case is executed through one public generated-verification
interface. Ordinary blocks compile but do not execute; only explicitly executable modes run.

Stable identities and hashes are part of the artifact contract:

- normalize documentation-relative paths and line endings;
- preserve meaningful whitespace;
- assign stable block IDs;
- hash displayed source together with its semantic mode;
- hash all preparation and page context that can change a block's meaning;
- fail on duplicates, missing blocks, or mismatches.

When a release declares semantic data, a mismatch is an error. It must never silently fall back to
syntax-only rendering. Releases created before semantic artifacts existed may use the explicitly
documented syntax-only fallback.

## Capture and rendering boundary

Release capture may compile projects, evaluate MSBuild, load compiler services, expand source
transclusions, and verify examples. It must fail rather than publish incomplete or untrustworthy
semantic data.

A history render may:

- verify checksums and supported schemas;
- apply explicit deterministic migrations;
- parse stored structured documentation and Markdown;
- resolve stored symbol references against the stored API graph;
- match semantic blocks by stable identity and hashes;
- generate current HTML, CSS, JavaScript, links, and accessible tooltip behavior.

A history render must not compile, restore, or load the historical project. It must not require the
historical SDK, packages, binaries, source generator, or FSharp.Formatting version.

## Schema compatibility

Each artifact component owns its schema version. Schema versions describe exact persisted
contracts, not an assumption that arbitrary older records will deserialize because missing fields
happen to receive empty values.

- Additive and incompatible changes both require an explicit compatibility decision.
- A breaking representation change increments that component's schema version.
- Loaders support an explicit set of known versions and reject unknown versions clearly.
- Supported old versions use small deterministic migrations into the current in-memory model.
- Reflection-based generic migration and best-effort deserialization are forbidden.
- A published artifact is immutable and is never regenerated under the same product version.
- Every published component and archive is SHA-256 verified before rendering.

The compatibility promise preserves historical content and semantic meaning. It does not promise
byte-identical rendered HTML. Renderer pinning, if added, is a separate optional reproducibility
feature.

## Storage and distribution

Recommended ownership:

- the Git tag records human-authored source and provenance;
- an immutable release asset stores the complete compressed FsLiveDocs capsule and checksum;
- the documentation host stores generated static output;
- `.livedocs/cache/` stores disposable local analysis caches;
- `.livedocs/releases/` stores downloaded capsules and is ignored by Git.

Git tags are provenance, not the sole reproduction mechanism: a release capsule must be sufficient
to render without reconstructing the historical build environment. Generated artifacts should not
be committed to the main development branch by default.

## Security and integrity

All artifact text is untrusted input at render time. Renderers encode plain text, validate external
URLs, resolve symbol references through owned identifiers, and construct markup rather than trusting
stored HTML. Archive extraction must reject path traversal and files outside the destination.

Checksums establish integrity, not authorship. Signing or build provenance may be added without
changing the renderer-neutral component schemas.

## User-facing defaults

The normal user path should be:

```text
livedocs init
livedocs capture <projects...> --version <version> --output <capsule>
livedocs build-history <history-index>
```

`capture` performs audit, verification, extraction, packaging, size reporting, and checksum
generation. It does not upload unless the user invokes an explicit publishing workflow. Generated
CI should attach the capsule to the matching immutable release and should never publish when
verification or capture fails.
