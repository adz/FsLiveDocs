---
title: FsLiveDocs
---

# F# documentation that checks itself

FsLiveDocs can be the whole documentation site for an F# library. You do not need Hugo, Docsy, Docusaurus, or a separate API-reference site.

Write Markdown pages in folders. FsLiveDocs puts them in the same navigation, search, theme, and version history as the generated API reference. The site uses Tailwind CSS and DaisyUI, so you can start with a built-in theme and add your own stylesheet.

It builds on FSharp.Formatting, then adds long-form API pages, API-aware links, source transclusion, compiler tooltips, checked examples, and immutable release history.

## See the difference

A normal Markdown example can go stale without anyone noticing:

````markdown
```fsharp
type Order = { Total: decimal }

module Orders =
    let total orders = orders |> List.sumBy _.Total

let pendingOrders = [ { Total = 20M }; { Total = 22M } ]
let total = Orders.total pendingOrders
```
````

FsLiveDocs compiles that block during `audit`, `test`, `build`, and `capture`. It uses the selected `.fsproj`, target framework, references, and earlier blocks on the page.

If `Orders.total` is renamed or its type changes, the documentation build fails at the page and line that need fixing.

```bash
dotnet livedocs audit src/YourLibrary/YourLibrary.fsproj
```

Ordinary examples compile but do not run. Execution is explicit:

````markdown
```fsharp run
printfn "order-total=%M" (Orders.total pendingOrders)
```
````

Use `transcript` when output is part of the claim:

````markdown
```fsharp transcript
> 20 + 22;;
val it: int = 42
```
````

## One site for guides and API reference

The `docs/` folder is the site structure:

```text
docs/
├── index.md
├── getting-started.md
├── guides/
│   └── configuration.md
└── api/
    └── YourLibrary.Client.md
```

Ordinary Markdown files become guide pages. Files under `docs/api/` add long-form content to generated API pages. Both appear in one sidebar and one search index.

Use `xref` links to connect prose to extracted symbols. Transclude marked source regions and named XML examples instead of copying code. Configure branding, source links, themes, and navigation in `.livedocs/config.json`.

## What else FsLiveDocs adds

### Checked guide examples

Code fences and XML documentation examples are checked against the real project. You choose whether each example compiles, runs, verifies a transcript, or is deliberately excluded.

### Compiler information in the browser

Rendered F# blocks can show inferred types and documentation tooltips. The compiler runs when you build the docs, not in the reader's browser.

### Release documentation that survives toolchain changes

`capture` stores the public API, Markdown, assets, and compiler-derived code information in one immutable capsule:

```bash
dotnet livedocs capture src/YourLibrary/YourLibrary.fsproj \
  --version 1.4.0 \
  --output artifacts/YourLibrary-1.4.0-livedocs.zip
```

A current FsLiveDocs version can render that capsule later. It does not need the old SDK, packages, source tree, or FSharp.Formatting version.

## Where FSharp.Formatting fits

FsLiveDocs uses FSharp.Formatting for API extraction. It keeps the existing F# and XML documentation model, then adds:

- long-form Markdown enrichment for API pages;
- API-aware links from guides;
- source and XML-example transclusion;
- examples that fail when they stop compiling;
- explicit, controlled execution of examples;
- inferred types and compiler documentation on displayed code;
- current rendering of immutable historical documentation;
- one CLI contract for local builds and CI.

If you only need a current API site and do not need checked examples or historical release capsules, FSharp.Formatting may already be enough.

## Start

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs --version 0.1.0
dotnet livedocs init --discover-projects
dotnet build
dotnet livedocs audit
dotnet livedocs build
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

Then continue with:

1. [Get started](introduction.md).
2. [Verify F# examples](guides/verified-examples.md).
3. [Add semantic code tooltips](guides/semantic-code.md).
4. [Capture and publish releases](guides/releases.md).
5. [Use the command reference](cheat-sheet.md).
