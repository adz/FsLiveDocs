---
title: Why FsLiveDocs
weight: 1
---

# Why FsLiveDocs

FsLiveDocs is a modern documentation tool for F# libraries. Think of it as
[fsdocs](https://fsprojects.github.io/FSharp.Formatting/) plus verification,
versioned docs, and the conveniences you would otherwise assemble by hand.

## Where it came from

This project started with fsdocs, then grew a requirement for versioned
documentation. That led through Docusaurus and later Docsy — each with its own
Node or Go toolchain — combined with FSharp.Formatting for the API reference and a
layer of scripts to hold it together. It worked, but it was clunky.

The clunkiness was one motivation. The larger one was verification:

- What if every example in the docs compiled in CI, so a rename or a signature
  change could not silently break them?
- Then why not check FSI transcripts too?
- Then why not actually run examples, capture their output, and show that output
  back in the docs?

At that point executable documentation starts behaving like a test suite.
FsLiveDocs is that idea built into one F#/.NET tool.

For a full example, see the [Reified documentation](https://adz.github.io/Reified/getting-started/index.html).

## Modern

- A `dotnet tool`: `dotnet livedocs`.
- Current F#/.NET implementation and SDK tooling.
- Tailwind CSS and DaisyUI, with a responsive UI and a theme picker.
- Long-form API pages that merge prose with the generated member reference.
- One search index and one sidebar across guides and API.
- Code presentation built on FSharp.Formatting, with inferred types and compiler
  tooltips on rendered blocks.
- First-class versioned documentation.

## Conveniences

- A small CLI: `init`, `audit`, `test`, `build`, `watch`.
- File-based navigation: number files under `docs/` to set sidebar order.
- API enrichment by convention: `docs/api/<Namespace>.<Type>.md` merges into the
  generated page for that entity.
- API-aware links (`xref:`) from prose to extracted symbols.
- Transclude examples from real source files and XML docs instead of copying them.
- A version switcher with a "latest" alias.
- Immutable release documentation.
- Batteries included: very little site configuration to get started.

## Verification

- Ordinary F# blocks are compile-checked against the actual project being
  documented, with failures reported at the page and line.
- Execution is explicit per block: compile-only, `run`, `transcript`, or a
  justified opt-out.
- Release documentation is verified against the corresponding library version.

See [Verify F# examples](guides/verified-examples.md).

## Versioning

Versioning is deliberately unusual.

On release, CI captures a **release capsule** — the public API symbols, the
documentation semantics, the Markdown, and the assets — and stores it somewhere
durable such as GitHub Releases. The site then regenerates HTML for every version
from those capsules.

This means a change to templates, CSS, or navigation updates the whole site,
including old versions, without rebuilding the old versions of the library. The
alternative — freezing the generated HTML of each release forever — is what this
avoids.

Each step is a `livedocs` command — `capture`, `history-check`, `history-add`,
`build-history`, `verify-output` — and the committed `.livedocs/history.json` is the
record of what has shipped. The tool does not upload capsules or push the index. Those are explicit CI steps, so
the publication flow works with GitHub, GitLab, or your own server. See [Capture and publish releases](guides/releases.md) and
[Verify documentation in CI](guides/continuous-integration.md).

## Where FSharp.Formatting fits

FsLiveDocs uses FSharp.Formatting for API extraction and keeps its F# and XML
documentation model. If you only need a current API site, without checked examples
or historical release capsules, FSharp.Formatting on its own may already be enough.

## Try it

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs
dotnet livedocs init --discover-projects
dotnet build
dotnet livedocs audit
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

Then continue with [Get started](introduction.md). If FsLiveDocs does not fit your
library well, that is considered a bug worth reporting.
