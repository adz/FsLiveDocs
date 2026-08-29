---
title: Verify documentation in CI
---

# Verify documentation in CI

Run FsLiveDocs on every change so a renamed symbol, a broken example, or a stale
transcript fails the build instead of reaching readers. The same commands publish
the site and its release history.

## What to run

The documentation build has three stages, in order:

1. `dotnet build` — the projects must compile before their documentation can be checked against them.
2. `dotnet livedocs test` — audits every F# block and runs each executable example and transcript.
3. `dotnet livedocs build` — renders the current site to `output/`.

`test` and `build` both perform the full audit, so a pull request that only needs
verification can stop after `test`.

## CI-friendly output

Pass these on every `livedocs` invocation in an automated environment:

```bash
dotnet livedocs test --interactive false --banner false
```

- `--interactive false` — line-oriented logs with no animated progress. FsLiveDocs
  also detects a non-terminal stdout, but wrapper scripts and background jobs can
  defeat that detection, so set it explicitly.
- `--banner false` — suppresses the start-up banner.
- `--verbosity info` (optional) — adds progress messages; `debug` expands every
  audited block and its compiler message.
- `--warn-as-error` (optional) — makes API documentation quality warnings fail the run.

## Generic pipeline

Any CI system that provides the .NET SDK and Node.js can run:

```bash
dotnet tool restore
dotnet build --nologo
dotnet livedocs test --interactive false --banner false
dotnet livedocs build --interactive false --banner false
```

`livedocs build` shells out to `npx pagefind` to build the search index, so Node.js
must be on `PATH`. Publish the `output/` directory as the site artifact.

If you commit a generated snapshot test project (see
[Verify F# examples](verified-examples.md#generate-a-committed-test-project)), run it
as an ordinary test project and add a check that it is up to date:

```bash
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
dotnet livedocs generate-tests --interactive false --banner false
git diff --exit-code tests/FsLiveDocs.SnapshotTests
```

## Releasing: what the tool does and what CI does

FsLiveDocs owns the capsule, the history index, and verification. It never uploads
an asset or moves a Git ref — your CI does that, in steps you can read. A release is:

```bash
# tool
dotnet livedocs capture --version "$V" --output "artifacts/$NAME-$V-livedocs.zip"
dotnet livedocs history-check --capsule "artifacts/$NAME-$V-livedocs.zip" --version "$V"
# CI — upload the capsule + report + .sha256 to a durable HTTPS location
# tool
dotnet livedocs history-add --version "$V" \
  --url "$CAPSULE_URL" --sha256-file "artifacts/$NAME-$V-livedocs.zip.sha256"
dotnet livedocs history-check
# CI — commit .livedocs/history.json to the default branch
```

The committed `.livedocs/history.json` is the source of truth. The site job then runs
`build-history` + `verify-output` from it.

### GitHub Actions

```bash
dotnet livedocs generate-ci
```

writes `.github/workflows/livedocs.yml` with the provider steps (`gh release create`,
`git commit`/`push`) spelled out and interleaved with the `livedocs` calls. It:

| Trigger | Action |
| --- | --- |
| every pull request | `dotnet build`, `livedocs test` |
| push to `main` | the above, then `livedocs build` + `build-history` + `verify-output`, then deploy to GitHub Pages |
| a `v*` tag | `capture`, `history-check`, **`gh release create`**, `history-add`, `history-check`, **commit `history.json` to `main`** |

Assumptions:

- GitHub repository with **Pages** set to "GitHub Actions", default branch `main`;
- releases tagged `v<semver>`;
- .NET SDK `10.0.x`, Node.js `22` — edit the workflow for other versions;
- `GITHUB_TOKEN` may create releases and push to `main` (`permissions: contents: write, pages: write, id-token: write`).

`generate-ci` refuses to overwrite an existing `livedocs.yml`; delete it to regenerate.

### GitLab CI (or any other host)

Replace the two provider steps. Publish however your host does it, then register and commit:

```yaml
release:
  script:
    - dotnet livedocs capture --version "$V" --output "artifacts/$NAME-$V-livedocs.zip" --interactive false --banner false
    - dotnet livedocs history-check --capsule "artifacts/$NAME-$V-livedocs.zip" --version "$V" --interactive false --banner false
    - |
      glab release create "v$V" \
        "artifacts/$NAME-$V-livedocs.zip" "artifacts/$NAME-$V-livedocs.zip.report.json"
    - |
      url="$CI_PROJECT_URL/-/releases/v$V/downloads/$NAME-$V-livedocs.zip"
      dotnet livedocs history-add --version "$V" --url "$url" \
        --sha256-file "artifacts/$NAME-$V-livedocs.zip.sha256" --interactive false --banner false
      dotnet livedocs history-check --interactive false --banner false
    - |
      git add .livedocs/history.json
      git commit -m "Record $V in the release history"
      git push "https://oauth2:$RELEASE_TOKEN@$CI_SERVER_HOST/$CI_PROJECT_PATH.git" HEAD:$CI_DEFAULT_BRANCH
```

Set `history.urlPattern` in `.livedocs/config.json` to drop the explicit `--url`. For a host
without a predictable download URL, upload wherever you like and pass that URL. To keep
`history-sync` working for backfills on a non-GitHub host, set `history.discover` to a command
that prints `version url sha256` lines.

## First-time migration

If your history has so far been rebuilt in CI by `history-sync` rather than committed, run it
once locally and commit the result so the index is complete before switching the site job to
build straight from it:

```bash
dotnet livedocs history-sync <owner/repo> --output .livedocs/history.json
git add .livedocs/history.json && git commit -m "Backfill release history"
```

See [Capture and publish releases](releases.md) for each command and the
[command reference](../cheat-sheet.md) for every flag.
