---
title: Set up your repository
weight: 2
---

# Set up your repository

This guide takes an existing F# repository from no docs setup to a live local preview.

## Before you start

You need the .NET SDK used by the repository. The project should already build.

Node.js is also needed when FsLiveDocs builds the search index.

## Install FsLiveDocs

A local tool keeps the version with the repository:

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs
```

Commit `.config/dotnet-tools.json`. Teammates and CI can then install the same tools with `dotnet tool restore`.

## Initialize the repository

Run this from the repository root:

```bash
dotnet livedocs init --discover-projects
```

FsLiveDocs creates or preserves:

```text
.livedocs/
  config.json
  history.json
docs/
  index.md
```

It also adds disposable caches and downloaded capsules to `.gitignore`.

`--discover-projects` records the `.fsproj` files it finds. Open `.livedocs/config.json` and remove tests, benchmarks, or apps that should not appear in the public API.

A small setup looks like this:

```json
{
  "siteName": "Example Library",
  "repoUrl": "https://github.com/example/example",
  "projects": [
    "src/Example/Example.fsproj"
  ],
  "navigation": [
    { "label": "Home", "href": "index.html" },
    { "label": "API", "href": "api.html" },
    { "label": "GitHub", "href": "https://github.com/example/example" }
  ]
}
```

`repoUrl` adds source links to generated API members. Project paths are relative to the repository root.

## Build the library

FsLiveDocs reads compiled assemblies and XML documentation, so build first:

```bash
dotnet build
```

If the project does not emit XML documentation, set `GenerateDocumentationFile` to `true` in the project or shared build props.

## Start the preview

```bash
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

Open `http://127.0.0.1:5000`. The watcher rebuilds after changes to docs, F# source, project files, or configuration.

Use a one-off build when you do not need the server:

```bash
dotnet livedocs build
```

The generated site goes to `output/`.

## Add your first guide

Create `docs/getting-started.md`:

````markdown
---
title: Getting started
---

# Getting started

```fsharp isolated
let greeting name = $"Hello, {name}!"
greeting "Ada"
```
````

`isolated` checks this block on its own. Ordinary `fsharp` blocks can build on earlier blocks from the same page.

## Check everything

```bash
dotnet livedocs audit
dotnet livedocs test
```

`audit` checks coverage and compilation without executing examples. `test` also runs explicit `run` blocks and transcripts.

## Next steps

- [Write API and guide pages](guides/api-pages.md).
- [Author and test examples](guides/verified-examples.md).
- [Run the checks in CI](guides/continuous-integration.md).
- [Configure navigation and branding](guides/navigation.md).

If the repository serves separate audiences, see [documentation sets](guides/navigation.md#split-one-site-into-documentation-sets) after the basic site works.
