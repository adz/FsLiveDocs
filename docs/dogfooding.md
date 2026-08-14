---
title: Dogfood FsLiveDocs
---

# Dogfood FsLiveDocs

FsLiveDocs documents and releases itself. Use this workflow before publishing version `0.1.0`.

## Build the repository

```bash
dotnet build FsLiveDocs.sln
```

## Run both test suites

```bash
dotnet test tests/FsLiveDocs.Tests/FsLiveDocs.Tests.fsproj
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
```

## Audit every documentation project

The deep reference selects the sample project. Include it with the four product projects:

```bash
projects=(
  src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj
  src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj
  src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj
  src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj
  samples/DeepReference/Acme.Docs/Acme.Docs.fsproj
)

dotnet livedocs audit "${projects[@]}"
```

## Rehearse capture

```bash
dotnet livedocs capture "${projects[@]}" \
  --version 0.1.0 \
  --output artifacts/fslivedocs-0.1.0.zip \
  --dry-run
```

Review the API, semantic, content, and compressed sizes.

## Capture the release

Run capture from the exact tagged commit:

```bash
dotnet livedocs capture "${projects[@]}" \
  --version 0.1.0 \
  --output artifacts/fslivedocs-0.1.0.zip
```

Inspect the result:

```bash
dotnet livedocs inspect artifacts/fslivedocs-0.1.0.zip
```

## Rebuild from the capsule

Add the capsule to a temporary or release history index:

```bash
dotnet livedocs history-add 0.1.0 \
  --capsule artifacts/fslivedocs-0.1.0.zip \
  --output .livedocs/history.json

dotnet livedocs build-history .livedocs/history.json
```

This final build must use only the capsule for historical content and semantics. It must not compile an old project.
