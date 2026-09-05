---
title: Repository and discovery
---

# Repository and discovery

FsLiveDocs resolves a small set of repository inputs before it checks or renders anything. Those inputs decide which APIs appear, which compiler context checks each page, and which files become part of a release.

## Repository layout

`livedocs init` creates or preserves:

```text
docs/
  index.md
.livedocs/
  config.json
  history.json
```

Commit `config.json` and `history.json`. Keep `.livedocs/cache/` and `.livedocs/releases/` out of Git; `init` adds both paths to `.gitignore`.

## Project selection

Commands resolve projects in this order:

1. project paths passed on the command line;
2. top-level `projects` and every `docsSets[].projects` entry in `.livedocs/config.json`;
3. discovered `.fsproj` files.

Pass every project that contributes public API symbols or appears in page front matter.

Projects must be built before extraction. FsLiveDocs reads their assemblies and XML documentation. A page-selected project is also evaluated for compiler options and references.

Projects that are not selected by a page still contribute their built assemblies to the shared API and reference context.

## Page discovery

FsLiveDocs scans Markdown below each documentation source root in deterministic path order.

The discovery pass:

1. assigns each file to a documentation set;
2. parses front matter;
3. expands source and XML-example transclusions;
4. finds top-level F# fences;
5. validates each fence mode;
6. selects the compiler context;
7. assigns stable verification cases and block IDs.

Audit, test generation, builds, capture, and semantic extraction consume this same result.

## Front matter

A page may select its title, project, target framework, and platform:

```yaml
---
title: HTTP client
project: src/Example.Http/Example.Http.fsproj
targetFramework: net10.0
platform: dotnet
---
```

The project must be part of the page's documentation set. The target framework must appear in that project.

`platform: dotnet` enables compiler verification. Fable verification is not available; every F# block on a `fable` page needs a specific `no-check` reason.

## Stable block identity

A block ID combines the normalized documentation path with its F# fence ordinal:

```text
guides/start.md#fsharp-2
```

Line endings and documentation-relative paths are normalized. Meaningful source whitespace remains part of the source hash.

Renaming a page or reordering its fences changes block identity. Released content changes only through a new release capsule.

## Transclusion

Source snippets and XML examples are expanded before verification and capture. Canonical release Markdown contains the expanded content, so historical rendering does not need the original source.

Semantic `xref` identifiers remain in canonical content. The renderer resolves them against the API graph and current route layout.
