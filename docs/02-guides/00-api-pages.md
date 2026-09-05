---
title: Write API and guide pages
---

# Write API and guide pages

Use guides for tasks that cross the library. Use API pages to explain one package, namespace, module, or type where readers will look it up.

## Organize the guide tree

Every Markdown file outside `docs/api/` becomes a guide page. Folders become sidebar groups, and numeric file prefixes control order without appearing in URLs.

```text
docs/
├── index.md
├── 01-getting-started.md
├── 02-guides/
│   └── 00-configuration.md
└── api/
```

Add front matter when a friendly title is useful:

```yaml
---
title: Configure the client
---
```

## Keep member docs near the code

Use XML comments for short summaries, parameters, return values, and examples. They stay useful in editor tooltips and become the generated member reference.

```fsharp
/// <summary>Creates a client configuration for one endpoint.</summary>
/// <param name="endpoint">The service base address.</param>
let create endpoint = {| Endpoint = endpoint |}
```

Longer explanations are easier to read and maintain as Markdown.

## Add an API landing page

Build once, then inspect `output/api/` for generated entity IDs:

```bash
dotnet build
dotnet livedocs build
```

Create a file under `docs/api/` whose stem matches an entity ID:

```text
docs/api/YourLibrary.md
docs/api/YourLibrary.Client.md
docs/api/YourLibrary.Client.Options.md
```

`YourLibrary.md` usually explains the root namespace. It also supplies the introduction for that package's generated landing page under `api/packages/`.

When the package name and root namespace differ, FsLiveDocs uses the first documented namespace in that package. Give that namespace a clear orientation page.

A good package or namespace page answers four quick questions:

- What is this package for?
- Should I install or reference it?
- Where should I start?
- Which parts are examples or implementation details?

## Explain a type or module

`docs/api/YourLibrary.Client.md` is merged into `output/api/YourLibrary.Client.html`. Generated signatures and members stay on the same page.

Write ordinary Markdown:

````markdown
# Client

`Client` sends typed requests to one endpoint.

## Create a client

```fsharp isolated
let endpoint = System.Uri "https://api.example.test"
let clientName = $"Client for {endpoint.Host}"
```

Reuse a client instead of creating one per request.
````

Checked fences, links, and transclusions work here exactly as they do in guides.

## Link instead of repeating

Use API pages for purpose, invariants, common operations, failure behavior, and focused examples.

Use a guide when a task crosses several APIs. Link the two so readers can move between learning and lookup without meeting the same explanation twice.

Next, learn how to [author and test examples](verified-examples.md), [transclude maintained source](transclusion.md), and [link API symbols](cross-references.md).
