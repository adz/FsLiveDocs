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
livedocs extract MyProject.fsproj
```

This generates a `.livedocs/history/{version}.json` file. You can use this blob to:
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

XML docstrings can sometimes feel cramped for large modules. FsLiveDocs allows you to provide **Elixir-quality** introductions using standalone Markdown files.

Simply create a file in `docs/api/{Namespace.Module}.md`. The engine will automatically transclude this file's content as the high-level summary for that module in the generated API reference.

## 🧪 Custom Scenarios

If your code examples require a specific state (like a logged-in user), use the `[<DocScenario>]` attribute. This allows you to define a setup function that runs before your example code.

{{< example id="DocScenarioUsage" >}}

---

*For more information, check the [Cheat Sheet](cheat-sheet.html).*
