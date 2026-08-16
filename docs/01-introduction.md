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
dotnet tool install FsLiveDocs --version 0.1.0
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

## Preview changes

```bash
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

The watcher rebuilds after source, project, configuration, or documentation changes.

## Continue

- [Verify examples](guides/verified-examples.md).
- [Configure navigation](guides/navigation.md).
- [Capture a release](guides/releases.md).
