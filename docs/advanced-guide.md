---
title: Advanced Guide
weight: 5
type: explanation
---

# 🧬 Advanced Guide: Under the Hood

This guide covers the advanced architecture of FsLiveDocs and how it builds upon existing F# tooling rather than replacing it.

## 🤝 Relationship with fsdocs

If you are coming from `fsdocs`, you'll find FsLiveDocs familiar but more opinionated about **verification**. While `fsdocs` is a great general-purpose tool, FsLiveDocs focuses on the **execution lifecycle** of your examples.

We use `FSharp.Compiler.Service` just like `fsdocs`, but we treat the resulting symbol tree as a **verified knowledge graph**.

## 🔍 Accessing Symbols & Modules

FsLiveDocs makes it easy to access the underlying API metadata via the `PackageModel`. You can use the `extract` command to get a full JSON dump of your project's documented symbols:

```bash
livedocs extract MyProject.fsproj --version 1.2.0 --output model.json
```

This generates a schema-versioned API model. Use it as an immutable release asset rather than committing generated
HTML or treating a Pages branch as history.

A local history build consumes a materialized manifest:

```json
{
  "schemaVersion": 1,
  "currentVersion": "1.2.0",
  "entries": [
    {
      "version": "1.2.0",
      "modelPath": "models/1.2.0.json",
      "modelSha256": "<lowercase sha256>",
      "docsPath": "sources/1.2.0/docs"
    }
  ]
}
```

```bash
livedocs build-history .livedocs/build-history/manifest.json
```

FsLiveDocs fails when an artifact is absent, its checksum differs, its schema is unsupported, or its embedded version
does not match the manifest. It then renders current docs at the site root and older versions under
`history/{version}/`.

You can use the API model to:
1.  **Generate custom UIs**: Build your own documentation frontend using the verified metadata.
2.  **AI Training**: Feed the JSON into an LLM to help it understand your API signatures.
3.  **Diffing**: Compare two versions of your API to find breaking changes.

## 🛠 Multi-Project Merging

One of the most powerful features is the ability to document an entire solution as a single unit. When you pass multiple `.fsproj` files to the `build` command, FsLiveDocs merges them:

```bash
livedocs build src/Core.fsproj src/Plugin.fsproj src/CLI.fsproj
```

The `SymbolLister.merge` function handles deduplication and provides a unified functional area view in the sidebar.

## 📜 Long-Form Module Introductions

XML docstrings can sometimes feel cramped for large modules. FsLiveDocs lets you produce high-quality module introductions with standalone Markdown files because they can be longer, more structured, and easier to scan than a single docstring.

Simply create a file in `docs/api/{Namespace.Module}.md`. The engine will automatically transclude this file's content as the high-level summary for that module in the generated API reference.

## 🧪 Custom Scenarios

If your code examples require a specific state, use the `[<DocScenario>]` attribute. This lets you define a setup function in source and connect it to examples by name. The generated snapshot project uses that link to run the setup before evaluating the transcript.

{{< example id="DocScenarioUsage" >}}

For the full selection and acceptance workflow, see the [Verified Examples](verified-examples.html) guide.

---

*For more information, check the [Cheat Sheet](cheat-sheet.html).*
