# 🔍 FsLiveDocs.Core.SymbolLister

The bridge between your F# code and the documentation engine. It leverages `FSharp.Formatting` to extract symbol metadata, XML docstrings, and executable examples.

## Key Functions

- `extractFromProject`: Compiles (if necessary) and scans a `.fsproj` to extract all documented entities.
- `merge`: Combines multiple project models into a unified, hierarchical `PackageModel`.
- `reconstructHierarchy`: Takes a flat list of entities and builds a tree structure based on namespaces and nesting.

{{< example id="ExtractExamplesExample" >}}

## Multi-Project Merging

One of the unique features of `SymbolLister` is its ability to merge symbols from multiple projects, even if they share the same namespace. This allows you to document a large solution as a single coherent API.

```fsharp
let core = SymbolLister.extractFromProject "Core.fsproj" |> Async.RunSynchronously
let plugin = SymbolLister.extractFromProject "Plugin.fsproj" |> Async.RunSynchronously
let unified = SymbolLister.merge [core; plugin]
```

## Namespace Support

`SymbolLister` automatically handles namespace documentation. If you provide a Markdown file matching a namespace ID (e.g., `docs/api/MyNamespace.md`), it will be used as the introduction for that namespace in the API reference.
