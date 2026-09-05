---
title: FsLiveDocs
---

# Documentation that checks itself

FsLiveDocs builds documentation sites for F# libraries. It keeps guides, generated API pages, checked examples, search, and version history in one .NET tool.

The useful bit is simple: examples are checked against the project they explain. Rename an API or change a type, and CI points to the page that needs attention.

Ordinary F# blocks compile without running:

````markdown
```fsharp
let total = [ 20M; 22M ] |> List.sum
```
````

Execution stays explicit. Use `run` for behavior and `transcript` when output is part of the promise.

## Try it

From your repository root:

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs
dotnet livedocs init --discover-projects
dotnet build
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

Open `http://127.0.0.1:5000`. Edit a Markdown page or F# source file and the preview rebuilds.

## What you get

- Markdown guides and generated API reference in one site.
- F# examples checked with your project's compiler settings.
- Optional execution, transcripts, and snapshot tests.
- Source and XML-example transclusion, so examples have one owner.
- Compiler tooltips and API-aware links.
- Release capsules that can render old docs without rebuilding old source.

## Where to go next

1. [Set up your repository](introduction.md).
2. [Write API and guide pages](guides/api-pages.md).
3. [Check examples and manage them as tests](guides/verified-examples.md).
4. [Run documentation checks in CI](guides/continuous-integration.md).
5. [Configure the site](guides/navigation.md).
6. [Capture versioned documentation](guides/releases.md).

Want the background first? Read [Why FsLiveDocs](why-fslivedocs.md). For exact commands and options, use the [command reference](cheat-sheet.md).
