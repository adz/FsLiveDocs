---
title: Configure pages with front matter
---

# Configure pages with front matter

Add YAML front matter at the start of a Markdown page.

```yaml
---
title: HTTP client
type: guide
project: src/Example.Http/Example.Http.fsproj
targetFramework: net10.0
platform: dotnet
---
```

## Set the page title

Use `title` to set the browser title, heading metadata, and navigation label.

When you omit `title`, FsLiveDocs derives one from the file name.

## Select a project

Use `project` to select the compiler context for F# blocks on this page.

Resolve the path from the repository root or documentation root. Pass the selected project to every audit, build, test-generation, and capture command.

## Select a target framework

Use `targetFramework` when the selected project targets more than one framework:

```yaml
targetFramework: net10.0
```

The framework must appear in the project. FsLiveDocs fails before compilation when it does not.

## Declare a platform

Use `platform: dotnet` for .NET compiler verification.

Fable compiler verification is not available yet. Pages marked `platform: fable` must exclude each F# block with a specific `no-check` reason.

## Use stable paths

Page paths become stable block identities and release-content paths. Rename a released page only in a new release capsule.
