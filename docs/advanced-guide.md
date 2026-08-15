---
title: Advanced authoring
---

# Advanced authoring

Use these features after you can build and audit a basic site.

## Document multiple projects

Pass every documented project to each command:

```bash
dotnet livedocs build \
  src/Example.Core/Example.Core.fsproj \
  src/Example.Http/Example.Http.fsproj
```

FsLiveDocs merges the public API graphs and records package provenance. Select a page compiler context with front matter when needed.

## Control terminal output

LiveDocs defaults to concise warning-focused output, the LiveDocs banner, and interactive terminal rendering. During an interactive build the banner allocates its display one line at a time, then rising blocks reveal the wordmark before its letters pulse once. The animation does not delay the build. Each completed stage remains visible with a tick or cross and its duration; the active stage shows a live elapsed time and counts such as `1/12` for project and page work. Control detail, animation, and the banner independently:

```bash
# Stable, concise CI logs
livedocs build src/Example/Example.fsproj --verbosity warnings --interactive false

# Troubleshoot discovery, audit, and watcher behavior
livedocs watch src/Example/Example.fsproj --verbosity debug --interactive false

# Minimal machine-readable surroundings
livedocs build src/Example/Example.fsproj --interactive false --banner false
```

Verbosity levels are `warnings`, `info`, and `debug`. `warnings` groups repeated API issues by file and kind, links each file to GitHub when `repoUrl` is configured, and prints completion summaries. `debug` expands full compiler messages and remedies and includes every audited block and watcher directory.

## Treat API warnings as errors

Extraction reports unnamed parameters, inconsistent signature names, and related API quality issues as warnings.

Fail on those warnings after your project adopts the policy:

```bash
dotnet livedocs build src/Example/Example.fsproj --warn-as-error
```

## Add repository-wide F# setup

Set a prelude in `.livedocs/config.json`:

```json
{
  "fSharpPrelude": "open System\nopen Example"
}
```

Use page-local `prepare` blocks when setup applies to one guide only.

## Customize the site

Configure branding and navigation:

```json
{
  "siteName": "Example",
  "repoUrl": "https://github.com/example/example",
  "logoPath": "content/logo.svg",
  "logoDarkPath": "content/logo-dark.svg",
  "stylesheet": "content/site.css",
  "themes": ["light", "dark"],
  "navigation": [
    { "label": "Guides", "href": "index.html" },
    { "label": "Source", "href": "https://github.com/example/example" }
  ]
}
```

Place referenced files under `docs/`. FsLiveDocs captures them as immutable release assets.

## Capture several projects

```bash
dotnet livedocs capture \
  src/Example.Core/Example.Core.fsproj \
  src/Example.Http/Example.Http.fsproj \
  --version 2.0.0 \
  --output artifacts/example-livedocs-2.0.0.zip
```

Only page-selected projects require compiler evaluation. Other documented projects contribute their built assemblies to the shared reference context.

## Keep compatibility explicit

API, semantic, content, capsule-manifest, and history-index schemas evolve independently.

Loaders accept explicitly supported versions and reject unknown versions. Published capsules are immutable.

See [Capture and publish releases](guides/releases.md) for the complete workflow.
