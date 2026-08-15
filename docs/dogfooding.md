---
title: Dogfood FsLiveDocs
---

# Dogfood FsLiveDocs

FsLiveDocs documents and releases itself. Use this workflow before publishing version `0.1.0`.

## Build the repository

```bash
dotnet build FsLiveDocs.slnx
```

## Run both test suites

```bash
dotnet test tests/FsLiveDocs.Tests/FsLiveDocs.Tests.fsproj
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
```

## Verify every documentation project

FsLiveDocs discovers documentable projects outside ignored build and test directories. The deep-reference sample is included automatically:

```bash
dotnet livedocs test
```

## Rehearse capture

```bash
dotnet livedocs capture \
  --version 0.1.0 \
  --output artifacts/fslivedocs-0.1.0.zip \
  --dry-run
```

Review the API, semantic, content, and compressed sizes.

## Capture the release

Run capture from the exact tagged commit:

```bash
dotnet livedocs capture \
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
