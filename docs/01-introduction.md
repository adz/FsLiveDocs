---
title: Get started
weight: 2
---

# Get started

This guide creates a local documentation site for one F# project.

## Before you begin

You need:

- the .NET SDK used by your project;
- a successful project build;
- FsLiveDocs installed as a local or global .NET tool.

## Install the tool

Create a tool manifest and install FsLiveDocs:

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs
```

## Initialize the repository

Run this command from the repository root:

```bash
dotnet livedocs init --discover-projects
```

`--discover-projects` writes the repository's documentable `.fsproj` files to the configuration. Review that list when the repository also contains benchmarks, probes, or applications that should not appear in the API reference.

The command creates:

- `.livedocs/config.json` for site configuration;
- `.livedocs/history.json` for release capsules;
- `docs/index.md` as a starter page;
- cache and release-download entries in `.gitignore`.

The command does not replace existing files. Project arguments passed to later commands override the configured list; without either, FsLiveDocs discovers projects for that run.

For a repository with several documentation audiences, configure `docsSets` instead. Commands
still operate on the whole site, using the union of every set's projects; see
[Configure navigation and branding](guides/navigation.md#split-one-site-into-documentation-sets).

## Write the primary API pages

Use Markdown under `docs/api/` for the main documentation of public namespaces, modules, and types. XML comments remain useful for concise member documentation and editor tooltips, but they do not need to carry an entire guide.

Start with [Write primary API pages](guides/api-pages.md), then add task-oriented guides for workflows that cross several API entities.

## Add a guide page

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

The `isolated` mode checks the block independently. Use an ordinary `fsharp` fence when later blocks depend on earlier blocks on the same page.

## Audit the documentation

Build your project, then audit every F# block:

```bash
dotnet build
dotnet livedocs audit
```

Fix compilation failures or mark deliberate pseudocode with a reason:

````markdown
```fsharp no-check reason="The omitted branch is application-specific"
match result with
| Ok value -> save value
| Error _ -> ...
```
````

## Build the site

```bash
dotnet livedocs build
```

The generated site is in `output/`.

## Add source links

`init` writes an empty `.livedocs/config.json`. Set `repoUrl` so API members link to
their source:

```json
{
  "repoUrl": "https://github.com/your-org/your-library"
}
```

FsLiveDocs builds links as `<repoUrl>/blob/main/<file>#L<line>`, so this assumes the
default branch is `main` and that source paths resolve from the repository root.
[Configure navigation and branding](guides/navigation.md) covers `siteName`, logos,
themes, and the rest of the file.

## Preview changes

```bash
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

The watcher rebuilds after source, project, configuration, or documentation changes.

## Continue

- [Write primary API pages](guides/api-pages.md).
- [Verify examples](guides/verified-examples.md).
- [Configure navigation](guides/navigation.md).
- [Verify documentation in CI](guides/continuous-integration.md).
- [Capture a release](guides/releases.md).
