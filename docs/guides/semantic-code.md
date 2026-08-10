---
title: Semantic and Verified Code Blocks
weight: 3
type: guide
---

# Semantic and Verified Code Blocks

FsLiveDocs treats F# in documentation as code with a declared contract—not as decoration. It expands snippets and
examples first, assigns every resulting block a stable ID, then uses the same discovered source for verification,
semantic hovers, current rendering, and release history.

The useful mental model is:

```text
authored Markdown + transcluded source
              → expanded documentation blocks
              → compilation and explicit execution cases
              → renderer-neutral semantic artifact
              → current or historical HTML
```

This matters because the browser never compiles F#, and rebuilding an old site never needs an old SDK or dependency
graph. A release pays the compiler cost once. Later renderer improvements can restyle the stored tokens and tooltips.

## Choose a block contract

An ordinary block is part of one page-wide compilation unit. Earlier declarations are therefore available to later
blocks, just as a reader experiences a progressive guide. Ordinary blocks compile but do not execute:

````markdown
```fsharp
let addTax rate amount = amount * (1M + rate)
```

```fsharp
let total = addTax 0.1M 20M
```
````

Use one of these modes when the default is not the truth:

| Fence | Contract | Displayed? | Executed? |
| --- | --- | --- | --- |
| `fsharp` | Compile in page context | Yes | No |
| `fsharp prepare` | Add hidden page setup | No | No |
| `fsharp isolated` | Compile as a standalone example | Yes | No |
| `fsharp run` | Compile in page context, then run | Yes | Yes |
| `fsharp transcript` | Verify FSI prompts and output | Yes | Yes |
| `fsharp no-check reason="…"` | Deliberate pseudocode | Yes | No |

`prepare` is useful for concise teaching fixtures:

````markdown
```fsharp prepare
type Order = { Total: decimal }
let sample = { Total = 20M }
```

```fsharp
let discounted = sample.Total * 0.9M
```
````

Use `isolated` when a block genuinely stands alone or redeclares names used elsewhere on the page. Use `run` only
when runtime behaviour is the contract; an ordinary example might access HTTP, files, processes, or clocks and must
not be executed merely to prove it type-checks.

An exclusion always explains itself:

````markdown
```fsharp no-check reason="Abbreviated match branches"
match result with
| Error _ -> ...
```
````

Empty reasons, unknown options, and contradictory modes such as `run isolated` fail discovery.

## Audit before enforcing

Run the audit while migrating an existing documentation set:

```bash
livedocs audit src/MyLibrary/MyLibrary.fsproj
```

The audit evaluates the selected project with MSBuild, including its target framework, project/package references,
conditional symbols, and language version. It then compiles page and isolated units without running ordinary code.
The report uses documentation-relative IDs such as `guides/start.md#fsharp-2` and classifies every expanded block as
page, preparation, isolated, executable, transcript, or explicitly excluded. These IDs are also the unit of failure
reporting, so a compiler diagnostic points back to the authored block rather than only to a generated script. Both
`livedocs test` and production `livedocs build` run this gate.

With multiple projects, the first command-line project is the default. Select a different compiler context for one
page in frontmatter; the path may be repository-relative or docs-root-relative and must also be passed to the command:

```yaml
---
title: Browser integration
project: src/MyLibrary.Browser/MyLibrary.Browser.fsproj
platform: dotnet
targetFramework: net8.0
---
```

An unqualified multi-target project uses its first declared framework. `targetFramework` requests a real inner build
and must name a framework declared by the selected project. `platform: fable` is descriptive but deliberately not
treated as compiler verification: until a Fable adapter is configured, use reasoned `no-check` fences and retain a
separate Fable compilation gate.

Repository-wide imports can be declared as `fsharpPrelude` in `.livedocs/config.json`. They are compiled, persisted
with semantic history, and shown to readers in an expandable setup panel. Page-local `prepare` blocks are shown the
same way.

## Why source and context hashes exist

A block ID identifies its place; its SHA-256 identifies its normalized expanded source and mode. Page checking context
is hashed separately because a hidden `prepare` edit can change the meaning of later code without changing those
blocks. When a release declares semantic data, a missing block or hash mismatch is a build error—not a silent switch
to syntax highlighting.

Old releases created before semantic artifacts remain supported with syntax-only highlighting.

## Release artifacts

Semantic data is stored separately from the API model as `.livedocs/history/<version>.semantic.json`. It contains
FsLiveDocs-owned tokens, plain tooltip content, and mapped diagnostics—not HTML, CSS class names, compiler objects, or
FSharp.Formatting types. The history manifest declares both its path and SHA-256, or neither:

```json
{
  "version": "1.2.0",
  "modelPath": "models/1.2.0.api.json",
  "modelSha256": "<sha256>",
  "semanticPath": "models/1.2.0.semantic.json",
  "semanticSha256": "<sha256>",
  "docsPath": "sources/1.2.0/docs"
}
```

This separation lets API consumers load the API model without downloading code-block data, and lets each schema
evolve explicitly.

## Troubleshooting

- “Not defined” in a later block usually means the setup belongs in an earlier ordinary or `prepare` block.
- Duplicate declarations usually mean one of the examples should be `isolated`.
- A platform-specific page should select its documentation project in frontmatter with `project:`.
- A semantic hash mismatch means the documentation source and release artifact are from different states. Regenerate
  the release artifact; do not weaken the check.
- Use `no-check` only for irreducible pseudocode. Small compiling fixtures and transcluded real source age better.
