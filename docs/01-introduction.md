---
title: Set up your repository
weight: 2
---

# Set up your repository

This guide takes an existing F# repository from no docs setup to a live local preview.

## Start here

1. Set `<GenerateDocumentationFile>true</GenerateDocumentationFile>` to get API pages.
2. Build the projects before running `dotnet livedocs`, and again after code changes.
3. `docs/index.md` is the home page.
4. `init` sets `siteName` from the solution name or repo folder; change it in `.livedocs/config.json`.
5. Link documentation pages by their real Markdown file names. Links outside the docs root need a source URL setting, or should be plain paths.
6. A leading number in a Markdown file name is dropped from its page URL.

## Before you start

You need the .NET SDK used by the repository. The project should already build.

FsLiveDocs includes the native Pagefind search indexer. Node.js and npm are not required.

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

FsLiveDocs creates or preserves the configuration and history files, and creates `docs/index.md` unless you choose an existing README as the home page:

```text
.livedocs/
  config.json
  history.json
docs/
  index.md
```

It also adds disposable caches and downloaded capsules to `.gitignore`.
`docs/index.md` is the site home page and becomes `output/index.html`. If `docs/README.md` exists without an index, `init` asks whether to use the README as the home page; when selected, the build maps it to `index.html`.
`init` sets `siteName` to the filename of the single solution at the repository root, or to the repository folder name when there is no single root solution. Change it in `.livedocs/config.json` to choose the title shown on the site.

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
`build` warns while `docs/index.md` still contains the unedited starter page.

## Build the library

FsLiveDocs reads compiled assemblies and XML documentation, so build first:

```bash
dotnet build
```

FsLiveDocs warns for each configured project that has no XML documentation file next to its assembly. That project has no API pages; set `GenerateDocumentationFile` to `true` in the project or shared build props. Use `--warn-as-error` to make the warning fail the build. `init --discover-projects` checks the effective project setting and prints the line to add when it is disabled.
After code changes, build the projects again before running FsLiveDocs so it reads the current assemblies and XML docs.

## Start the preview

```bash
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

Open `http://127.0.0.1:5000`. The watcher rebuilds after changes to docs, F# source, project files, configuration, or the configured projects' assemblies and XML docs.

Use a one-off build when you do not need the server:

```bash
dotnet livedocs build
```

The generated site goes to `output/`.
When standard output is redirected, the build uses plain progress output automatically.
If links are broken, FsLiveDocs reports them together so you can fix them in one pass.
Link to a page by its real Markdown file path, including its original capitalization and any leading number. FsLiveDocs maps that source path to the generated URL, where names are lowercase and ordering numbers are removed.

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
When one F# block has several compiler errors, `audit` reports every error with its line and column.

## Next steps

- [Write API and guide pages](guides/api-pages.md).
- [Author and test examples](guides/verified-examples.md).
- [Run the checks in CI](guides/continuous-integration.md).
- [Configure navigation and branding](guides/navigation.md).

If the repository serves separate audiences, see [documentation sets](guides/navigation.md#split-one-site-into-documentation-sets) after the basic site works.
