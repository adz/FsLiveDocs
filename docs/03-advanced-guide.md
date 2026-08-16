---
title: Advanced authoring
---

# Advanced authoring

Use these features after you can build and audit a basic site.

## Document multiple projects

List every documented project once in `.livedocs/config.json`:

```json
{
  "projects": [
    "src/Example.Core/Example.Core.fsproj",
    "src/Example.Http/Example.Http.fsproj"
  ]
}
```

Every command then picks them up automatically:

```bash
dotnet livedocs build
```

FsLiveDocs merges the public API graphs and records package provenance. Select a page compiler context with front matter when needed.

Pass project paths on the command line only to override the configured list for one invocation, for example when checking a single project during a quick edit-check loop.

## Control terminal output

Control log detail and interactivity independently:

```bash
# Stable, concise CI logs
livedocs build --verbosity warnings --interactive false

# Troubleshoot discovery, audit, and watcher behavior
livedocs watch --verbosity debug --interactive false

# Minimal machine-readable surroundings
livedocs build --interactive false --banner false
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

With `projects` configured in `.livedocs/config.json`, `capture` picks them up the same way `build` does:

```bash
dotnet livedocs capture --version 2.0.0 --output artifacts/Example-2.0.0-livedocs.zip
```

Only page-selected projects require compiler evaluation. Other documented projects contribute their built assemblies to the shared reference context.

See [Capture and publish releases](guides/releases.md) for the complete release workflow.
