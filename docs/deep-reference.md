---
title: FsLiveDocs reference
project: samples/DeepReference/Acme.Docs/Acme.Docs.fsproj
platform: dotnet
---

# FsLiveDocs reference

Use this page when you need command behavior, verification invariants, artifact contracts, or failure policy.

## Repository layout

`livedocs init` creates or preserves these paths:

```text
docs/
  index.md
.livedocs/
  config.json
  history.json
```

The command adds `.livedocs/cache/` and `.livedocs/releases/` to `.gitignore`.

Commit `config.json` and `history.json`. Do not commit analysis caches or downloaded capsules.

## Project inputs

Commands accept one or more `.fsproj` paths. Pass every project that contributes API symbols or is selected by page front matter.

Build projects before extraction. FsLiveDocs reads built assemblies and XML documentation, then evaluates page-selected projects for compiler options and references.

Only page-selected projects require full compiler evaluation. Other passed projects contribute their built assemblies to the aggregate reference context.

## Page discovery

FsLiveDocs scans Markdown files below `docs/` in deterministic path order.

It parses front matter, expands source and XML-example transclusions, assigns stable F# block IDs, validates modes, and selects a project context.

Block IDs use the normalized documentation path and F# fence ordinal:

```text
guides/start.md#fsharp-2
```

Capture stores canonical expanded Markdown. It preserves semantic `xref` identifiers for the current renderer.

## Verification modes

### Page mode

An ordinary `fsharp` fence joins the page compilation unit. It can use declarations from earlier page and `prepare` blocks.

### Prepare mode

`fsharp prepare` adds shared setup. It affects page context and appears as shared setup in rendered documentation.

### Isolated mode

`fsharp isolated` creates a separate compilation unit with the selected project references and repository prelude.

### Run mode

`fsharp run` compiles in its page context and then executes. A selected generated test still compiles before execution when its companion compile fact is filtered out.

### Transcript mode

`fsharp transcript` parses FSI input and expected output, then executes it through the transcript runner.

### No-check mode

`fsharp no-check reason="..."` creates a deliberate syntax-only exclusion. The reason must be nonempty.

## Generated verification contract

`livedocs generate-tests` creates stable xUnit cases from the same documentation discovery result used by audit, build, and capture.

Run the generated test project in CI. FsLiveDocs validates coverage, rejects stale cases, compiles owning units, executes explicit blocks, and maps diagnostics back to the authored documentation.

## API extraction

The API component contains:

- package and assembly provenance;
- entity and member IDs;
- entity hierarchy and kinds;
- plain signatures, parameter types, and return types;
- structured summaries, remarks, and parameter documentation;
- examples and source locations.

Documentation uses FsLiveDocs-owned nodes for text, paragraphs, code, lists, symbol references, external links, line breaks, and canonical Markdown.

API JSON contains no generated HTML. The renderer encodes text, validates external URLs, resolves symbol IDs, and constructs markup.

## Semantic extraction

FsLiveDocs evaluates real MSBuild properties and reference paths for the selected project and target framework.

Page and isolated units produce semantic tokens, tooltips, mapped diagnostics, source hashes, and context hashes.

Release capture fails on unexpected compiler errors. Warnings remain in the semantic component and can be promoted by command policy.

## Release capsule layout

The deterministic ZIP archive contains:

```text
manifest.json
api.json
semantic.json
content.json
assets/<documentation-relative-path>
```

Every ZIP entry uses a normalized relative path, stable ordering, a fixed timestamp, fixed attributes, and optimal compression.

Capture rejects duplicate, absolute, empty, current-directory, or parent-directory paths. Loading also rejects directories, symbolic links, more than 10,000 entries, entries over 64 MiB, and archives that expand past 256 MiB.

Capture refuses to overwrite an existing output.

## Capsule manifest

The manifest records:

- capsule manifest schema;
- product version;
- Git source revision;
- capture tool version;
- each component's schema, path, size, and SHA-256.

The API product version must match the manifest product version.

## Content component

The content component stores:

- canonical expanded Markdown pages;
- renderer-neutral page metadata;
- site configuration;
- normalized asset paths, media types, sizes, and checksums.

The archive stores asset bytes separately. History materialization verifies each asset before writing it below a validated destination.

## Schema policy

API, semantic, content, capsule-manifest, and history-index schemas evolve independently.

Loaders accept an explicit supported version. They reject unknown versions instead of relying on empty defaults from permissive deserialization.

Add a deterministic migration before claiming support for an older schema. Do not use reflection-based generic migration.

## Capture command

```bash
livedocs capture <projects...> \
  --version <version> \
  --output <capsule.zip>
```

Capture requires a Git commit for provenance. It performs audit, explicit execution, API extraction, semantic extraction, content capture, archive validation, and report generation.

The adjacent `.report.json` contains provenance, schemas, checksums, compressed and uncompressed sizes, and inventory counts.

Inventory covers entities, members, documentation nodes, examples, pages, code blocks, tooltips, diagnostics, and assets.

Use `--dry-run` to perform the same validation and size calculation without writing the requested output or report.

## Inspect command

```bash
livedocs inspect <capsule.zip>
```

Inspection verifies ZIP structure, manifest schema, component size and checksum, component schemas, product version, and every asset size and checksum.

## History index

A history index uses this shape:

```json
{
  "SchemaVersion": 1,
  "CurrentVersion": "1.4.0",
  "Entries": [
    {
      "Version": "1.4.0",
      "CapsulePath": null,
      "CapsuleUrl": "https://example.com/releases/1.4.0/docs.zip",
      "CapsuleSha256": "<64 lowercase hexadecimal characters>"
    }
  ]
}
```

Each entry declares exactly one local path or HTTPS URL. Versions are unique, and one entry must match `CurrentVersion`.

`history-add` rejects an existing version because published entries are immutable.

## Remote acquisition

Remote capsules must use HTTPS and include an expected SHA-256.

FsLiveDocs downloads to a temporary file, verifies it, and moves it into `.livedocs/releases/<sha256>.livedocs.zip`.

An existing cache entry is verified before reuse. A checksum mismatch never enters the cache.

## History rendering

`build-history` verifies each capsule, materializes canonical content in a temporary directory, and renders with the current renderer.

It never restores, loads, or compiles the historical project. The temporary content directory is removed after the build.

The current release provides site configuration. Current output uses the site root; older versions use `history/<version>/`.

## Legacy manifest compatibility

`build-history` still accepts the earlier local manifest with separate API, semantic, checksum, and docs-tree fields.

Use this path only to migrate existing history. New releases should use complete capsules.

## Integrity and security

Treat every capsule value as untrusted at render time.

FsLiveDocs:

- validates and confines archive paths;
- checks duplicate entries;
- verifies outer and inner checksums;
- encodes documentation text;
- allows only HTTP, HTTPS, and mail links in structured API docs;
- resolves internal references through stored symbol IDs;
- disables raw HTML in stored Markdown documentation nodes.

Checksums prove integrity, not authorship. Publish capsules from trusted CI and add signing or provenance attestation when your release policy requires it.

## Failure behavior

Capture and history builds fail for:

- incomplete project lists;
- invalid fence modes or uncovered blocks;
- compiler errors;
- failed explicit examples;
- stale source or context hashes;
- unsupported schemas;
- missing or duplicate capsule entries;
- unsafe archive paths;
- checksum, size, or product-version mismatches;
- unresolved authored documentation links.

Fix the release inputs. Do not bypass these checks for published artifacts.
