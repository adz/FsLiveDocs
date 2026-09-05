---
title: Release capsules
---

# Release capsules

A release capsule freezes documentation meaning at the same commit as a library release. It is the unit FsLiveDocs verifies, publishes, downloads, and renders later.

The capsule stores source-derived artifacts rather than generated site files. HTML, CSS, and templates remain free to improve in newer renderers.

## Capture command

```bash
livedocs capture <projects...> \
  --version <version> \
  --output <capsule.zip>
```

Capture requires a Git commit for provenance and refuses to overwrite an existing output.

It performs discovery, audit, compilation, explicit execution, API extraction, semantic extraction, content capture, archive creation, and archive validation.

`--dry-run` performs the same analysis and size calculation without writing the requested capsule or report.

## Archive layout

A capsule contains:

```text
manifest.json
api.json
semantic.json
content.json
assets/<documentation-relative-path>
```

Entries use normalized relative paths, stable ordering, fixed timestamps and attributes, and deterministic compression settings.

## Components

### API

`api.json` stores the public symbol graph, plain signatures, package provenance, examples, source locations, and structured API documentation.

### Semantic

`semantic.json` stores checked block identity, tokens, tooltips, diagnostics, source hashes, context hashes, and repository prelude.

### Content

`content.json` stores canonical expanded Markdown, page metadata, documentation-set identity, resolved set models, site configuration, and asset metadata.

Asset bytes live under `assets/`. Each asset records its normalized path, media type, size, and checksum.

### Manifest

`manifest.json` records:

- capsule-manifest schema version;
- product version;
- Git source revision;
- capture tool version;
- component schema versions;
- component paths, sizes, and SHA-256 checksums.

The API product version must match the manifest product version.

## Adjacent files

Capture writes three files:

```text
Example-1.4.0-livedocs.zip
Example-1.4.0-livedocs.zip.report.json
Example-1.4.0-livedocs.zip.sha256
```

The report contains provenance, schemas, checksums, compressed and uncompressed sizes, and inventory counts.

Inventory includes entities, members, documentation nodes, examples, pages, code blocks, tooltips, diagnostics, and assets.

The `.sha256` file contains the bare capsule checksum for publication and `history-add`.

## Inspection

```bash
livedocs inspect <capsule.zip>
```

Inspection verifies archive structure, manifest and component schemas, product versions, component sizes and checksums, and every asset size and checksum.

It then reports provenance, schemas, sizes, and inventory totals.

## Schema policy

API, semantic, content, capsule-manifest, and history-index schemas evolve independently.

Loaders accept an explicit set of known versions and reject unknown versions. Missing fields do not quietly become defaults.

Compatibility with an older representation requires a small deterministic migration. Published artifacts are never regenerated under the same product version.

## Immutability

A published capsule is immutable. Fixes to released documentation require a new product version and a new capsule.

Checksums protect integrity. They do not prove authorship; signing and provenance attestation belong to the publication policy.
