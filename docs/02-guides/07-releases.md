---
title: Capture and publish releases
---

# Capture and publish releases

Capture your library's documentation when you release the library. FsLiveDocs can later render that documentation with a current renderer, without rebuilding the old library version.

## Understand the capsule

A capsule is a deterministic ZIP archive with four logical components:

- `api.json` contains symbols, plain signatures, and structured documentation.
- `semantic.json` contains tokens, tooltips, diagnostics, and source hashes.
- `content.json` contains canonical Markdown, page/set identity, resolved documentation-set models, assets, and site configuration.
- `manifest.json` contains provenance, schemas, sizes, and component checksums.

The capsule stores documentation content and meaning rather than generated site files. This lets newer FsLiveDocs versions render an older release with current templates and styling.

## Validate a planned capture

Run a dry run before publishing:

```bash
dotnet livedocs capture --version 1.4.0 --output artifacts/YourLibrary-1.4.0-livedocs.zip --dry-run
```

The command audits and verifies the same inputs as a real capture. It reports expected component and compressed sizes without writing the requested capsule.

## Capture the release

Build the library commit you are releasing, then run:

```bash
dotnet livedocs capture --version 1.4.0 --output artifacts/YourLibrary-1.4.0-livedocs.zip
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
Example.Library-1.4.0-livedocs.zip.sha256
```

Capture writes three files: the capsule, its JSON report, and a `.sha256` file holding the
bare checksum. Keep all three beside each other. The `generate-ci` workflow uses the GitHub
repository name as the package prefix.

## Inspect a capsule

```bash
dotnet livedocs inspect artifacts/YourLibrary-1.4.0-livedocs.zip
```

Inspection verifies the archive and every component. It reports provenance, schema versions, checksums, compressed and uncompressed sizes, and inventory counts.

## Check the candidate

Before publishing anywhere, verify the capsule renders alongside every prior version:

```bash
dotnet livedocs history-check --capsule artifacts/YourLibrary-1.4.0-livedocs.zip --version 1.4.0
```

`history-check` loads the committed `.livedocs/history.json`, splices the local capsule in as
the release under test, renders the whole history into a temporary directory, and runs the
same entry-point, switcher, and local-link checks as `verify-output`. It does not modify the
index or call a hosting provider's API, but it downloads remote capsules listed in the index.
Run it with no arguments to re-check the committed history as-is.

## The publisher contract

FsLiveDocs never uploads an asset or moves a Git ref. It may read provider APIs during
`history-sync` and download indexed capsules. To publish a release you:

1. put the capsule and its `.report.json` at a durable HTTPS URL — a GitHub or GitLab release
   asset, an S3 object, your own server;
2. record it with `history-add --url <that URL> --sha256-file <the .sha256>`;
3. commit the updated `.livedocs/history.json`.

The committed index is the source of truth. Anything that satisfies those three steps works —
[Verify documentation in CI](continuous-integration.md) has ready-made snippets for GitHub and
GitLab.

## Record a release in history

From a URL (the normal path — the asset is already uploaded):

```bash
dotnet livedocs history-add --version 1.4.0 \
  --url https://github.com/example/your-library/releases/download/v1.4.0/YourLibrary-1.4.0-livedocs.zip \
  --sha256-file artifacts/YourLibrary-1.4.0-livedocs.zip.sha256
```

`--sha256 <hex>` works in place of `--sha256-file`. If `.livedocs/config.json` sets a URL
pattern you can omit `--url`:

```json
{
  "history": {
    "urlPattern": "https://github.com/example/your-library/releases/download/v{version}/YourLibrary-{version}-livedocs.zip"
  }
}
```

`{version}` and `{tag}` (`v{version}`) are the placeholders. From a local file — for offline
history builds — use `--capsule <path>` instead; the checksum is then computed.

Published entries are immutable: `history-add` refuses a version already in the index.

## Synchronize from a host

`history-sync` is optional — a convenience for backfilling entries you have not committed, or a
one-time migration. It is **one-way**: it reads published releases and merges them into the
index, and never changes a release.

```bash
dotnet livedocs history-sync example/your-library --output .livedocs/history.json
```

The positional argument is a GitHub `owner/repo`; it discovers assets named
`your-library-<version>-livedocs.zip` and requires GitHub's SHA-256 digest. For any other host,
supply a lister command that prints `version url sha256` lines:

```bash
dotnet livedocs history-sync --from "glab release list -R example/your-library ..." \
  --output .livedocs/history.json
```

or set `history.discover` in `.livedocs/config.json`. Either way, the oldest committed entry is
the compatibility floor: synchronization extends history without admitting older capsule
formats. In GitHub Actions, pass `github.token` as `GH_TOKEN`.

## Build all versions

```bash
dotnet livedocs build-history .livedocs/history.json --retry 3
dotnet livedocs verify-output .livedocs/history.json --output output
```

`build-history` retries transient downloads and verifies each outer capsule checksum and every
internal checksum before rendering. Checksum mismatches are deterministic and never retried.

`verify-output` then requires every version entry point, checks every generated local `href`
and `src` (excluding the search tool's `pagefind/` directory), and confirms the version
switcher lists all releases newest-first.

The current version appears at the site root. Older versions appear below `history/<version>/`.
Version switching keeps the current page and set when that identity exists in the target release,
then falls back to that set's API/root and finally the site root. Historical releases always use
the sets captured in their capsule, not the current repository configuration. Content schema 1
capsules migrate deterministically to one implicit legacy set and retain their original routes.

## Migrate from loose artifacts

FsLiveDocs still reads the earlier local manifest format with separate API, semantic, and `docs/` paths.

Capture each maintained release into a capsule, add it with `history-add`, and switch `build-history` to `.livedocs/history.json`.

Keep old manifests only while you need the compatibility path. Capsules remove the historical source-tree dependency.
