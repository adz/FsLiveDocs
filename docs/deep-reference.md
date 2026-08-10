---
title: Complete Consumer Deep Reference
weight: 8
type: reference
project: samples/DeepReference/Acme.Docs/Acme.Docs.fsproj
---

# Complete Consumer Deep Reference

This is the end-to-end reference for adopting FsLiveDocs in a real F# repository. It deliberately does not assume
you know FsLiveDocs’ source tree or internal model. The example library is `Acme.Docs`; every F# guide block on this
page is audited against the working sample project in `samples/DeepReference/Acme.Docs`.

Use the shorter [Introduction](introduction.html) for a first pass. Return here when setting up the repository,
choosing an example contract, generating tests, configuring CI, or publishing version history.

## What you get

One workflow produces:

1. API pages from compiled signatures and XML documentation;
2. guides whose ordinary F# blocks are compiler-checked without being run;
3. opt-in executable examples and FSI transcripts;
4. source snippets that stay synchronized with production code;
5. semantic code hovers containing inferred types and XML documentation;
6. deterministic xUnit cases named after the owning page or block;
7. immutable API and semantic release artifacts;
8. historical sites rendered by the newest UI without compiling old projects.

## Complete repository shape

```text
Acme/
├── .config/
│   └── dotnet-tools.json
├── .github/workflows/docs.yml
├── .livedocs/
│   ├── config.json
│   └── models/
├── docs/
│   ├── content/logo.svg
│   ├── index.md
│   └── pricing.md
├── scripts/docs.sh
├── src/Acme.Orders/
│   ├── Acme.Orders.fsproj
│   └── Library.fs
└── tests/FsLiveDocs.SnapshotTests/     # generated
```

FsLiveDocs reads `docs/` and `.livedocs/config.json` from the working directory. Run commands from the repository
root. Project paths passed to a command are the assemblies whose API and documentation context you want included.

## 1. Install and pin the CLI

Install the .NET 10 SDK and Node.js 22 or newer (Pagefind is run through `npx`). Pinning a local tool keeps developer
machines and CI on the same version:

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs
dotnet tool restore
dotnet livedocs --help
```

When working from an FsLiveDocs source checkout before a package is published, build the executable and substitute
`/path/to/FsLiveDocs/artifacts/livedocs` for `dotnet livedocs`:

```bash
./scripts/publish.sh
./artifacts/livedocs --help
```

The rest of this reference uses `livedocs` for readability; use the invocation installed by your repository.

## 2. Create a documented library

The project must emit its XML documentation file. FsLiveDocs also uses its evaluated target framework, project and
package references, conditional symbols, and language version when checking guide code:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
    <PackageReference Include="FsLiveDocs.Abstractions" Version="&lt;same-version-as-the-tool&gt;" />
  </ItemGroup>
</Project>
```

For a project declaring `TargetFrameworks`, an unqualified page is checked against the first declared framework. That
is a baseline, not a claim that every documented API exists on that target. Select a framework explicitly whenever a
page demonstrates a narrower surface:

```yaml
---
title: .NET hosting
project: src/Acme.Hosting/Acme.Hosting.fsproj
platform: dotnet
targetFramework: net8.0
---
```

FsLiveDocs performs a real MSBuild inner evaluation with `TargetFramework=net8.0`; the build fails if the project does
not declare that target. The rendered page shows the platform and target context. This keeps a `netstandard2.1`
baseline from falsely validating a .NET 8 example.

`netstandard2.1` is an API contract implemented by several runtimes. It is not “the Fable target,” and it does not
prove that code survives Fable translation. A page may declare `platform: fable`, but until FsLiveDocs has a Fable
compiler adapter, every F# fence on that page must use `no-check` with an honest reason or be transcluded from code
covered by the repository's separate Fable build gate. FsLiveDocs refuses to label ordinary .NET compiler checking as
Fable verification.

The small, dependency-free Abstractions package is needed only when the library declares `[<DocScenario>]` setup
functions. Keep its version aligned with the `FsLiveDocs` local tool; do not reference the tool’s implementation
assemblies from application code.

A compact but fully documented source file:

```fsharp isolated
namespace Acme.Orders

/// <summary>A customer order.</summary>
type Order = { Id: int; Subtotal: decimal }

/// <summary>Functions for constructing and pricing orders.</summary>
module Order =
    // <snippet:CreateOrder>
    /// <summary>Creates an order after validating its subtotal.</summary>
    let create id subtotal =
        if subtotal < 0M then invalidArg (nameof subtotal) "Subtotal cannot be negative."
        { Id = id; Subtotal = subtotal }
    // </snippet:CreateOrder>

    /// <summary>Calculates a total including tax.</summary>
    /// <example name="CalculateTotal" data-livedocs="snapshot">
    /// > Order.create 7 20M |> Order.total 0.1M;;
    /// val it: decimal = 22.0M
    /// </example>
    let total taxRate order = order.Subtotal * (1M + taxRate)
```

The XML `summary`, parameter, return, remarks, and example elements feed the API reference and semantic tooltips.
Transcript-shaped examples—lines beginning with `> `—are selected for executable verification.

### XML examples that need deterministic setup

Put reusable setup in a public, parameterless function marked with `DocScenario`. Reference its name from the XML
example; FsLiveDocs invokes it immediately before that example:

```fsharp isolated
open FsLiveDocs.Core

module CustomerContext =
    let mutable private discount = 0M

    [<DocScenario("preferred-customer")>]
    let loadPreferredCustomer () =
        discount <- 0.1M

    /// <example name="PreferredCustomerPrice"
    ///          scenario="preferred-customer"
    ///          data-livedocs="snapshot">
    /// > CustomerContext.price 100M;;
    /// val it: decimal = 90.0M
    /// </example>
    let price subtotal = subtotal * (1M - discount)
```

Scenario names must be unique across all supplied projects. Setup should be deterministic and reset every piece of
mutable state it owns, because generated tests can run in any order. A missing scenario, non-callable setup method,
or setup exception fails the named snapshot test.

## 3. Scaffold and configure the site

```bash
livedocs init
```

`init` creates `docs/index.md`, `.livedocs/config.json`, and the history directory without overwriting existing
files. A complete configuration can look like this:

```json
{
  "siteName": "Acme Orders",
  "logoText": "AO",
  "logoPath": "content/logo.svg",
  "showSiteName": true,
  "repoUrl": "https://github.com/acme/orders",
  "stylesheet": "content/site.css",
  "themes": ["light", "dark", "corporate"],
  "navigation": [
    { "label": "Home", "href": "/index.html" },
    { "label": "API", "href": "/api.html" }
  ]
}
```

Non-Markdown files under `docs/` are copied to the output with their relative paths intact. Consumer CSS loads after
FsLiveDocs styles. A missing optional setting uses the built-in default.

## 4. Write a progressive guide

An ordinary `fsharp` fence is displayed and compiled but never automatically executed. Ordinary blocks on the same
page share one compilation unit, so later teaching steps can use earlier declarations:

```fsharp prepare
open Acme.Docs
```

```fsharp
let sample = Order.create 42 100M
```

```fsharp
let total = sample |> Order.total 0.2M
```

The `prepare` block provides shared setup without interrupting the teaching sequence. It is compiled and rendered in
an expandable **Shared setup** panel, so readers can reproduce the example. It affects the page context hash, so a
release artifact becomes stale if that setup changes.

For imports shared by the whole documentation set, configure an explicit repository prelude instead of relying on
namespaces discovered from assemblies:

```json
{
  "fsharpPrelude": "open System\nopen Acme.Orders"
}
```

FsLiveDocs compiles this prefix for every checked page, stores it in the semantic history artifact, and renders it as
an expandable **Repository F# setup** panel on every page containing checked F#. Adding a namespace to the public API
does not change the compilation environment; only editing this reviewed configuration does.

Successful API extraction and semantic checking are cached under `.livedocs/cache/`. Cache keys include project
source and build inputs, expanded block hashes and modes, selected projects/frameworks, the repository prelude,
compiler identity, and artifact schemas. Prose, CSS, and theme edits therefore reuse verified compiler data during
preview. A relevant source, project, setup, framework, or code-block change invalidates the cache and performs a cold
verification. The directory is generated state and should be ignored by version control.

### Standalone code

Use `isolated` when the example must compile without earlier displayed blocks, or when it redeclares a common name:

```fsharp isolated
open Acme.Docs
let sample = Order.create 1 5M
let total = Order.total 0.1M sample
```

### Explicit runtime behaviour

Use `run` only when executing the code is part of its documentation contract:

```fsharp run
printfn "order-total=%M" total
```

Normal page examples might perform HTTP calls, write files, start servers, or depend on clocks. That is why compile
verification and execution are separate decisions.

### FSI transcripts

Use `transcript` for prompts and expected interactive output:

```fsharp transcript
> Acme.Docs.Order.create 7 20M |> Acme.Docs.Order.total 0.1M;;
val it: decimal = 22.0M
```

### Deliberate pseudocode

Pseudocode is an explicit exclusion, not a quietly ignored example:

```fsharp no-check reason="The omitted branch is application-specific"
match result with
| Error _ -> ...
```

The reason is printed by `livedocs audit`. Empty reasons, unknown modes, and contradictions such as `run isolated`
fail discovery before the compiler starts.

## 5. Transclude production source

Place balanced snippet markers in an `.fs` file:

```fsharp isolated
namespace Acme.SnippetExample

module Order =
    // <snippet:CreateOrderReference>
    let create id subtotal =
        if subtotal < 0M then invalidArg (nameof subtotal) "Subtotal cannot be negative."
        id, subtotal
    // </snippet:CreateOrderReference>
```

Then expand it in Markdown:

```text
{{< snippet id="CreateOrder" >}}
```

The resolver searches `.fs` files under the repository root and expansion happens before identity, hashing,
verification, or rendering. Optional snippet contracts are written on the shortcode:

```text
{{< snippet id="CreateOrder" mode="isolated" >}}
{{< snippet id="PartialExcerpt" mode="no-check" reason="Depends on surrounding private helpers" >}}
```

Missing snippets, unsupported modes, and `no-check` without a reason fail the build.

## 6. Reuse XML examples and cross-reference symbols

Transclude a named XML example:

```text
{{< example id="CalculateTotal" >}}
```

The imported block retains XML-example origin. Its named snapshot test owns execution, so rendering the same example
on multiple pages does not run it multiple times.

Use xrefs for generated API links:

```text
xref:T:Acme.Docs.Order
xref:M:Acme.Docs.Order.create
```

An unresolved xref is a build failure. Use ordinary Markdown links for prose pages and assets.

Long module introductions live at `docs/api/{fully-qualified-entity-id}.md`. Their F# blocks follow exactly the same
discovery, mode, verification, and semantic rules as guide blocks.

## 7. Select project context on multi-project sites

The first project passed to the CLI is the default. A platform-specific page selects another evaluated project:

```yaml
---
title: Browser client
project: src/Acme.Browser/Acme.Browser.fsproj
---
```

Pass every selectable project to the command:

```bash
livedocs audit src/Acme.Orders/Acme.Orders.fsproj src/Acme.Browser/Acme.Browser.fsproj
```

FsLiveDocs uses the aggregate documented assembly references while retaining the selected project’s framework,
defines, warning policy, and language version. Use explicit selection when projects have platform-specific APIs.

## 8. Audit, generate tests, test, build, and watch

The normal local loop is:

```bash
dotnet build Acme.sln
livedocs audit src/Acme.Orders/Acme.Orders.fsproj
livedocs generate-tests src/Acme.Orders/Acme.Orders.fsproj
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
livedocs build src/Acme.Orders/Acme.Orders.fsproj
livedocs watch src/Acme.Orders/Acme.Orders.fsproj --host 127.0.0.1 --port 5000
```

`audit` reports stable IDs and mapped compiler locations. `generate-tests` creates deterministic facts such as:

```text
compile pricing.md#page
compile pricing.md#fsharp-3
execute pricing.md#fsharp-4
coverage pricing.md#coverage
xml Acme.Orders#example-CalculateTotal
```

Regenerate after adding, removing, moving, or changing documentation blocks. Commit the generated project and Verify
snapshots so CI discovers the same named cases. Compilation cases never execute operational code; `run`, authored
`transcript`, and selected XML examples have explicit execution owners.

`build` reruns the compiler-backed gate and refuses to render compiler recovery guesses as trustworthy hovers.
`watch` serves the last successful output when a rebuild fails and prints the mapped diagnostic.

## 9. Put the workflow in one script

`scripts/docs.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

projects=(src/Acme.Orders/Acme.Orders.fsproj)

dotnet build Acme.sln --nologo
livedocs generate-tests "${projects[@]}"
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj --nologo
livedocs build "${projects[@]}" --theme corporate
```

Run it from the repository root. The generated static site is `output/`.

## 10. CI

An explicit workflow is easier to audit than a hidden publishing step:

```yaml
name: documentation
on:
  pull_request:
  push:
    branches: [main]

jobs:
  docs:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: dotnet tool restore
      - run: dotnet build Acme.sln --nologo
      - run: dotnet livedocs generate-tests src/Acme.Orders/Acme.Orders.fsproj
      - run: dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj --nologo
      - run: dotnet livedocs build src/Acme.Orders/Acme.Orders.fsproj
      - uses: actions/upload-pages-artifact@v3
        with:
          path: output
```

Add the deployment job required by your host. Never publish when generation, verification, or the production build
fails.

## 11. Extract immutable release artifacts

At release time, compile and semantically analyze the tagged source once:

```bash
livedocs extract src/Acme.Orders/Acme.Orders.fsproj \
  --version 1.4.0 \
  --output .livedocs/models/1.4.0.api.json
```

This writes:

```text
.livedocs/models/1.4.0.api.json
.livedocs/models/1.4.0.semantic.json
```

The API artifact and semantic artifact have independent schemas. Semantic JSON contains FsLiveDocs-owned tokens,
plain tooltip content, mapped warnings, source hashes, and page-context hashes. It contains no generated HTML,
compiler objects, formatter types, CSS classes, or DOM IDs.

Publish both files as immutable release assets and record lowercase SHA-256 values. Do not regenerate a released
version under the same version number.

## 12. Build version history without old compilers

Release automation downloads the immutable artifacts and checks out each tag’s `docs/` and snippet source. It then
materializes a local manifest:

```json
{
  "schemaVersion": 1,
  "currentVersion": "1.4.0",
  "entries": [
    {
      "version": "1.4.0",
      "modelPath": "models/1.4.0.api.json",
      "modelSha256": "<lowercase-sha256>",
      "semanticPath": "models/1.4.0.semantic.json",
      "semanticSha256": "<lowercase-sha256>",
      "docsPath": "sources/1.4.0/docs"
    },
    {
      "version": "1.3.0",
      "modelPath": "models/1.3.0.api.json",
      "modelSha256": "<lowercase-sha256>",
      "semanticPath": null,
      "semanticSha256": null,
      "docsPath": "sources/1.3.0/docs"
    }
  ]
}
```

```bash
livedocs build-history .livedocs/build-history/manifest.json
```

For 1.4.0, FsLiveDocs verifies checksums, schemas, every expanded block’s source hash, and its checking-context hash,
then renders stored tokens with the currently installed UI. It never loads or compiles the historical project. The
older 1.3.0 entry has no semantic artifact and deliberately uses syntax-only highlighting. Declaring only one of
`semanticPath` and `semanticSha256`, changing tagged source, or supplying an unknown schema fails the build.

## 13. Command reference

Run commands from the repository root. Every command that accepts projects accepts one or more `.fsproj` paths:

| Command | Purpose and outputs |
| --- | --- |
| `livedocs init` | Adds starter `docs/`, configuration, and history directories without replacing existing files |
| `livedocs ci` | Adds `.github/workflows/livedocs.yml`; review its project discovery and deployment policy before committing |
| `livedocs audit <projects…>` | Expands content, validates coverage/modes, evaluates project compiler settings, and reports mapped compile results |
| `livedocs generate-tests <projects…>` | Rewrites the generated xUnit/Verify project with stable page, block, and named XML-example facts |
| `livedocs test <projects…>` | Runs the audit plus the legacy direct XML verifier; generated xUnit tests remain the canonical CI/approval path |
| `livedocs build <projects…> [--theme name]` | Gates on audit, creates current semantic data, and writes the static site to `output/` |
| `livedocs watch <projects…> [--host ip] [--port n] [--ignore names]` | Serves and rebuilds the last valid site; `--ignore` is repeatable or comma-separated |
| `livedocs extract <projects…> --version v [--output path]` | Writes independently versioned API and semantic release artifacts |
| `livedocs build-history <manifest>` | Verifies immutable inputs and builds all documented versions without compiling historical projects |

`--theme` also applies to `watch`. The default host is `0.0.0.0`, the default port is `5000`, and built-in watcher
exclusions include `.git`, `.livedocs`, `artifacts`, `bin`, `obj`, `output`, packages, and test results.

## 14. Understand failures

| Failure | Meaning | Correct response |
| --- | --- | --- |
| `X is not defined` | Page setup or project context is incomplete | Add a real earlier/`prepare` declaration or select the right project |
| Duplicate declaration | Progressive blocks redeclare a name | Make the genuinely standalone block `isolated` |
| Missing `reason` | Pseudocode exclusion is unauditable | Add a concrete reason or make the example compile |
| Missing snippet/example/xref | Authored reference is stale | Correct the ID; do not silently omit it |
| Source/context hash mismatch | Docs and semantic artifact are from different states | Use matching tagged inputs or regenerate an unreleased artifact |
| Checksum/schema failure | History input is corrupt or unsupported | Fetch the correct immutable artifact or upgrade FsLiveDocs |
| Transcript mismatch | Runtime output changed | Fix behaviour/docs or deliberately approve the new snapshot |
| Build keeps old preview | Latest watch rebuild failed | Read the mapped terminal diagnostic; last good output remains served |

## Contract summary

| Authoring form | Compiled | Executed | Displayed | Semantic hover |
| --- | ---: | ---: | ---: | ---: |
| `fsharp` | Yes, page unit | No | Yes | Yes |
| `fsharp prepare` | Yes, page unit | No | No | Context only |
| `fsharp isolated` | Yes, own unit | No | Yes | Yes |
| `fsharp run` | Yes, page unit | Yes | Yes | Yes |
| `fsharp transcript` | No source compile | Yes in FSI | Yes | Transcript renderer |
| `fsharp no-check reason="…"` | No | No | Yes | Syntax-only |
| `{{< snippet … >}}` | According to shortcode mode | According to mode | Unless `prepare` | After expansion |
| `{{< example … >}}` | Owned by XML example pipeline | Once by named case | Yes | Transcript/source renderer |

The invariant is simple: every displayed F# block is compiler-verified, explicitly executed, or explicitly excluded
with a reason—and a release claiming semantic data must match the exact expanded source that was verified.
