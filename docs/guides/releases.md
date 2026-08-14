---
title: Capture and publish releases
---

# Capture and publish releases

Capture a release once, then render it with current FsLiveDocs versions without compiling the historical project.

## Understand the capsule

A capsule is a deterministic ZIP archive with four logical components:

- `api.json` contains symbols, plain signatures, and structured documentation.
- `semantic.json` contains tokens, tooltips, diagnostics, and source hashes.
- `content.json` contains canonical Markdown, page metadata, assets, and site configuration.
- `manifest.json` contains provenance, schemas, sizes, and component checksums.

The capsule contains no generated HTML, CSS classes, DOM IDs, or compiler implementation objects.

## Validate a planned capture

Run a dry run before publishing:

```bash
dotnet livedocs capture src/YourLibrary/YourLibrary.fsproj \
  --version 1.4.0 \
  --output artifacts/your-library-livedocs-1.4.0.zip \
  --dry-run
```

The command audits and verifies the same inputs as a real capture. It reports expected component and compressed sizes without writing the requested capsule.

## Capture the release

Build the exact release commit, then run:

```bash
dotnet livedocs capture src/YourLibrary/YourLibrary.fsproj \
  --version 1.4.0 \
  --output artifacts/your-library-livedocs-1.4.0.zip
```

Capture performs these operations:

1. Extract the public API.
2. Expand documentation transclusions.
3. Validate coverage and compile documentation units.
4. Run explicitly executable examples.
5. Store semantic compiler results.
6. Capture canonical Markdown and assets.
7. Create and verify the deterministic archive.

Capture refuses to overwrite an existing output. Publish a released version once.

## Inspect a capsule

```bash
dotnet livedocs inspect artifacts/your-library-livedocs-1.4.0.zip
```

Inspection verifies the archive and every component. It reports provenance, schema versions, checksums, compressed and uncompressed sizes, and inventory counts.

## Publish the capsule

Attach the ZIP and its `.report.json` file to the matching immutable GitHub release.

Generate a starter workflow:

```bash
dotnet livedocs ci
```

The generated tag workflow verifies documentation, captures the release, and creates a GitHub release. It fails instead of replacing an existing release.

## Add a local release to history

```bash
dotnet livedocs history-add 1.4.0 \
  --capsule artifacts/your-library-livedocs-1.4.0.zip
```

FsLiveDocs calculates the checksum and writes `.livedocs/history.json`.

## Add a remote release to history

```bash
dotnet livedocs history-add 1.4.0 \
  --url https://github.com/example/your-library/releases/download/v1.4.0/your-library-livedocs-1.4.0.zip \
  --sha256 <sha256>
```

Remote sources must use HTTPS. FsLiveDocs stores downloads by checksum under `.livedocs/releases/` and reuses verified files.

## Build all versions

```bash
dotnet livedocs build-history .livedocs/history.json
```

FsLiveDocs verifies each outer capsule checksum and every internal checksum before rendering.

The current version appears at the site root. Older versions appear below `history/<version>/`.

## Migrate from loose artifacts

FsLiveDocs still reads the earlier local manifest format with separate API, semantic, and `docs/` paths.

Capture each maintained release into a capsule, add it with `history-add`, and switch `build-history` to `.livedocs/history.json`.

Keep old manifests only while you need the compatibility path. Capsules remove the historical source-tree dependency.
