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

## Publish FsLiveDocs packages

FsLiveDocs itself uses `.github/workflows/release.yml`. A tag named `v<semver>` is the only release trigger. The workflow removes the leading `v`, passes that version to every build and pack, verifies the package set, publishes it to NuGet.org, captures the matching documentation capsule, and creates the GitHub release.

The published package set is:

| Package | Purpose |
| --- | --- |
| `FsLiveDocs` | The `livedocs` .NET tool. |
| `FsLiveDocs.Annotations` | The lightweight, `netstandard2.0` compile-time contract for attributes such as `DocScenario`. |

`FsLiveDocs.Annotations` supplies declarative metadata attached to consumer code. Core, Runner, and Renderer remain internal project boundaries and are bundled into the tool package; they are not separate public NuGet packages. Consumers install `FsLiveDocs` as a tool and add `FsLiveDocs.Annotations` as a library dependency only when they use attributes such as `DocScenario`.

Before the first tag, configure NuGet.org Trusted Publishing:

1. Sign in to NuGet.org and open **Account settings → Trusted publishing**.
2. Add a GitHub Actions policy owned by the NuGet account that will own the packages.
3. Set the GitHub owner to `adz`, repository to `FsLiveDocs`, and workflow path to `.github/workflows/release.yml`.
4. Leave the environment empty because the workflow does not use a GitHub environment.
5. In the GitHub repository, create an Actions variable named `NUGET_USER` containing the NuGet.org username—not an email address.

The workflow requests GitHub's OIDC token and exchanges it through `NuGet/login@v1` for a short-lived publishing credential. No persistent NuGet API key is stored in GitHub. The workflow does not use `--skip-duplicate`: package versions and release tags are immutable, so reusing a published version fails.

Create and push a release tag only from the commit to release:

```bash
git tag -a v0.1.0 -m "FsLiveDocs 0.1.0"
git push origin v0.1.0
```

The version in `Directory.Build.props` is the local-development default. Tagged CI overrides it from the tag and rejects tags that are not stable semantic versions.

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

## Plan storage capacity

Axial provides a representative large project. On 2026-08-14, its inputs contained 82 Markdown files (317,151 bytes), 8 assets (1,490,449 bytes), 942,360 bytes of API JSON, and 1,302,483 bytes of semantic JSON.

The structured inputs compressed to about 264 KB. Assets therefore dominate an estimated 1.75 MB capsule. Axial's release capture was not published because its dirty source and existing build outputs left 9 of 313 blocks uncompilable.

Treat that refusal as the release gate working correctly. Build a clean release commit before using its final report for retention planning.
