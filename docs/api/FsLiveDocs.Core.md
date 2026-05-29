# 🧬 FsLiveDocs.Core

The heart of the documentation engine. This project contains the fundamental data models and logic used to extract information from F# codebases.

## What lives here

- `Models.fs`: the shared data contracts for packages, entities, members, examples, and scenarios.
- `SymbolLister`: extracts symbols and examples from compiled projects.
- `ContentProvider`: resolves snippets, xrefs, and markdown links.
- `Library.fs`: small core helpers and the `DocScenarioAttribute`.

## Key Concepts

- **PackageModel**: The root of all documented knowledge.
- **SymbolLister**: The bridge to the F# compiler.
- **ContentProvider**: The Markdown processor.

## Example Overview

The `ExampleModel` type includes a `CreateExample` sample in the source docs so the API page stays close to the data shape that powers verified examples.

{{< example id="CreateExample" >}}

That transcript shows a real multiline record literal, a binding, and a property access the way an FSI user would type it.

The same transcript style is used when an example depends on a setup scenario:

{{< example id="UserGreeting" >}}

This keeps the page anchored in actual source code instead of inventing a separate narrative for the documentation site.
