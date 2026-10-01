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

## Organize related APIs

FsLiveDocs presents a same-named F# type and companion module as one **API family**. The compiler symbols and cross-reference targets remain distinct, but readers see the representation and its operations together.

Put lightweight placement metadata beside a declaration:

```fsharp
/// <summary>Creates a validated order.</summary>
/// <group ref="construction" />
/// <facet name="audience" value="common" />
let createOrganized subtotal = {| Subtotal = subtotal |}
```

Declare ordered sections, prose, member placement, and inherited facets in the family's API page:

```yaml
---
title: Order
api:
  family: Acme.Docs.Order
  sections:
    - id: construction
      title: Construction
      order: 10
      summary: Create values while preserving Order invariants.
      members: [create, tryCreate]
      facets:
        task: [create]
        audience: [common]
    - id: pricing
      title: Pricing
      order: 20
      members: [subtotal, total]
      facets:
        capability: [pricing]
---
```

Use `symbols` instead of `members` when an overloaded or cross-entity name is ambiguous. Symbol IDs are the generated IDs shown in the reference output.

`task`, `audience`, `capability`, and `lifecycle` express author intent. FsLiveDocs derives objective `kind` and `shape` facets from compiler symbols and signatures. It does not guess whether an API is common, advanced, safe, or preferred.

Members not explicitly placed remain visible in a conservative default section. A section's facets are inherited by the members assigned to it.

## Organize a package landing page

Entity sections organize members inside one family. Package sections organize modules and types on package landing pages, the API overview, and sidebar navigation.

Add `packageSections` to an API page owned by that package:

```yaml
api:
  packageSections:
    - package: Example.Core
      id: domain
      title: Domain APIs
      order: 10
      summary: The values and operations used by application code.
      entities: [Example.Order, Example.Customer]
    - package: Example.Core
      id: integration
      title: Integration
      order: 20
      entities: [Example.Json, Example.Http]
```

Each configured section becomes an expandable sidebar group. Once a package declares sections, every public entity in that package must be assigned. The build reports all omissions instead of creating a vague catch-all.

## Link instead of repeating

Use API pages for purpose, invariants, common operations, failure behavior, and focused examples.

Use a guide when a task crosses several APIs. Link the two so readers can move between learning and lookup without meeting the same explanation twice.

Next, learn how to [author and test examples](01-verified-examples.md), [transclude maintained source](03-transclusion.md), and [link API symbols](04-cross-references.md).
