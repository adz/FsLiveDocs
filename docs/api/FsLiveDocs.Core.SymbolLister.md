# 🔍 FsLiveDocs.Core.SymbolLister

The bridge between your F# code and the documentation engine. It leverages `FSharp.Formatting` to extract symbol metadata, XML docstrings, and executable examples.

## Key Functions

- `extractFromProject`: Compiles (if necessary) and scans a `.fsproj` to extract all documented entities.
- `merge`: Combines multiple project models into a unified, hierarchical `PackageModel`.
- `reconstructHierarchy`: Takes a flat list of entities and builds a tree structure based on namespaces and nesting.

{{< example id="ExtractExamplesExample" >}}

## Multi-Project Merging

One of the unique features of `SymbolLister` is its ability to merge symbols from multiple projects, even if they share the same namespace. This allows you to document a large solution as a single coherent API.

{{< example id="ExtractExamplesExample" >}}

If you run that in FSI, the binding names and the merged `PackageModel` are exactly what you would expect from the transcript style used elsewhere in the docs.

## Namespace Support

`SymbolLister` automatically handles namespace documentation. If you provide a Markdown file matching a namespace ID (e.g., `docs/api/MyNamespace.md`), it will be used as the introduction for that namespace in the API reference.
