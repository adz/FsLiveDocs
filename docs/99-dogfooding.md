---
title: How this site dogfoods FsLiveDocs
---

# How this site dogfoods FsLiveDocs

This site is built with the tool it documents. The repository configuration selects two useful API surfaces:

- `FsLiveDocs.Annotations`, the real public library package;
- `Acme.Docs`, a small teaching sample.

The CLI, Core, Runner, and Renderer assemblies are implementation details. They are bundled into the tool and are not presented as supported library APIs.

The authored API pages under `docs/api/` introduce both selected packages. The rest of this site exercises guide pages, checked fences, XML examples, scenarios, transclusion, source links, and custom navigation.

During development we run:

```bash
dotnet build
dotnet run --project src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj -- \
  watch --host 127.0.0.1 --port 5000
```

A release goes through the same capture and history workflow described in [Capture and publish releases](guides/releases.md). Historical rendering uses the capsule alone and does not rebuild the tagged source.

There are no private shortcuts here. If this repository is awkward to document with FsLiveDocs, that is product feedback.
