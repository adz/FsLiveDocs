---
title: Write primary API pages
---

# Write primary API pages

Put the main documentation for each public namespace, module, or type under `docs/api/`. These Markdown files enrich the generated API reference directly, so readers get explanation, checked examples, signatures, and member reference on one page.

Use XML comments for concise member summaries and editor tooltips. Use `docs/api/*.md` for the longer explanation a reader needs to understand and apply an API. This keeps primary API documentation readable without forcing a guide into XML syntax or separating it from the generated member reference.

## Match a generated API entity

Build once to discover the generated entity IDs:

```bash
dotnet build
dotnet livedocs build
```

Open `output/api/` or follow an API link in the preview. Create a Markdown file whose stem matches the generated entity ID. For example:

```text
docs/
└── api/
    ├── YourLibrary.md
    ├── YourLibrary.Client.md
    └── YourLibrary.Client.Options.md
```

`docs/api/YourLibrary.Client.md` enriches `output/api/YourLibrary.Client.html`. Entity IDs are fully qualified, so files remain unambiguous when different namespaces contain types with the same short name.

## Author the page as Markdown

Write ordinary Markdown. The heading, paragraphs, lists, links, transclusions, and F# fences become the long-form body of the generated API page.

This source:

````markdown
# Client

`Client` sends typed requests to the configured endpoint.

## Create an endpoint

```fsharp isolated
let endpoint = System.Uri "https://api.example.test"
```

Use one client per endpoint and reuse it across requests.
````

renders as long-form prose followed by a compiler-checked code block:

`Client` sends typed requests to the configured endpoint.

### Create an endpoint

```fsharp isolated
let endpoint = System.Uri "https://api.example.test"
```

Use one client per endpoint and reuse it across requests.

The generated signatures and member reference remain on the same API page.

## Link and transclude instead of copying

API pages use the same content pipeline as guides:

- link to symbols with `xref` links;
- transclude marked source regions with `snippet` shortcodes;
- transclude named XML examples with `example` shortcodes;
- use ordinary, `isolated`, `run`, `transcript`, `prepare`, and justified `no-check` F# modes;
- use relative Markdown links to connect related guides and API pages.

See [Transclude source and examples](transclusion.md) and [Link guides to APIs](cross-references.md) for the syntax.

## Decide what belongs where

Use an API page when the content explains one namespace, module, or type:

- purpose and boundaries;
- construction and common operations;
- invariants and failure behavior;
- checked examples;
- links to related APIs.

Use a guide when the reader's task crosses several API entities, such as configuring a service, migrating a contract, or publishing a release.

A useful default is:

1. make `docs/api/*.md` the primary explanation of each public API;
2. keep member XML comments concise and locally useful;
3. add guides only for end-to-end tasks and concepts that span API pages.
