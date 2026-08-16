---
title: Capture and publish releases
---

# Capture and publish releases

Capture your library's documentation when you release the library. FsLiveDocs can later render that documentation with a current renderer, without rebuilding the old library version.

## Understand the capsule

A capsule is a deterministic ZIP archive with four logical components:

- `api.json` contains symbols, plain signatures, and structured documentation.
- `semantic.json` contains tokens, tooltips, diagnostics, and source hashes.
- `content.json` contains canonical Markdown, page metadata, assets, and site configuration.
- `manifest.json` contains provenance, schemas, sizes, and component checksums.

The capsule stores documentation content and meaning rather than generated site files. This lets newer FsLiveDocs versions render an older release with current templates and styling.

## Validate a planned capture

Run a dry run before publishing:

```bash
dotnet livedocs capture src/YourLibrary/YourLibrary.fsproj \
  --version 1.4.0 \
  --output artifacts/YourLibrary-1.4.0-livedocs.zip \
  --dry-run
```

The command audits and verifies the same inputs as a real capture. It reports expected component and compressed sizes without writing the requested capsule.

## Capture the release

Build the library commit you are releasing, then run:

```bash
dotnet livedocs capture src/YourLibrary/YourLibrary.fsproj \
  --version 1.4.0 \
  --output artifacts/YourLibrary-1.4.0-livedocs.zip
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

Name the capsule so it is distinguishable from packages and application archives:

```text
<package>-<version>-livedocs.zip
```

For example:

```text
Example.Library-1.4.0-livedocs.zip
Example.Library-1.4.0-livedocs.zip.report.json
```

Keep the generated report beside the capsule. The `generate-ci` workflow uses the GitHub repository name as the package prefix.

## Inspect a capsule

```bash
dotnet livedocs inspect artifacts/YourLibrary-1.4.0-livedocs.zip
```

Inspection verifies the archive and every component. It reports provenance, schema versions, checksums, compressed and uncompressed sizes, and inventory counts.

## Publish the capsule

Attach the ZIP and its `.report.json` file to the matching immutable GitHub release.

Generate a starter workflow:

```bash
dotnet livedocs generate-ci
```

The generated workflow verifies documentation, publishes tagged capsules using the recommended filename, and deploys the current site from `main`. It fails instead of replacing an existing release.

To enable deployment in GitHub:

1. Open the repository's **Settings → Pages**.
2. Under **Build and deployment**, select **GitHub Actions** as the source.
3. Push the generated `.github/workflows/livedocs.yml` workflow to the default branch.
4. Allow the first deployment to create the `github-pages` environment.
5. If the environment has protection rules, permit the default branch to deploy.

A project repository named `Example.Library` is published at `https://<owner>.github.io/Example.Library/`. FsLiveDocs emits relative links, so no repository-path option is required.

The generated workflow publishes the current site. To publish release history, keep `.livedocs/history.json` in the repository and run `build-history` before uploading the Pages artifact.

## Add a local release to history

```bash
dotnet livedocs history-add 1.4.0 \
  --capsule artifacts/YourLibrary-1.4.0-livedocs.zip
```

FsLiveDocs calculates the checksum and writes `.livedocs/history.json`.

## Add a remote release to history

```bash
dotnet livedocs history-add 1.4.0 \
  --url https://github.com/example/your-library/releases/download/v1.4.0/YourLibrary-1.4.0-livedocs.zip \
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
