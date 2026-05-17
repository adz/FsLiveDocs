---
title: Dogfooding FsLiveDocs
weight: 4
---

# Dogfooding FsLiveDocs

This project uses its own engine to generate the documentation you are reading right now. This page explains exactly how we setup the documentation for this solution.

## 🛠 Project Configuration

The solution is split into 4 main projects under `./src`:
- `FsLiveDocs.Core`
- `FsLiveDocs.Runner`
- `FsLiveDocs.Renderer`
- `FsLiveDocs.Cli`

All of these are passed to the `build` command to create a unified API reference.

## 🧬 Every API is Documented

We follow a strict rule: **Every public member must have an XML docstring.**

Example from `Say.hello`:

```fsharp
/// <summary>Prints a friendly greeting to the console.</summary>
/// <example name="HelloExample">
/// Say.hello "F#"
/// // EXPECTED: Hello F#
/// </example>
let hello name = ...
```

## 🧪 Verified Examples

The examples above are not just text. When we run `./scripts/preview.sh`, the following happens:
1. `livedocs test` is called.
2. It extracts the `<example>` tags.
3. It generates a temporary .NET 10 project.
4. It executes the code and verifies that the output matches the `EXPECTED:` comment.

## 🔗 Live Transclusion

This very guide uses live code snippets from our source. For example, the `ExampleModel` record definition is pulled directly from `Models.fs`:

{{< snippet id="ExampleModel" >}}

## 🎨 Professional Layout

We use the built-in **Tailwind CSS + DaisyUI** renderer with a 3-column layout:
- **Left Sidebar**: Navigation for Guides and API (grouped by namespace).
- **Center**: Prose content with full-width typography.
- **Right Sidebar**: Dynamic "On This Page" table of contents.

## 🚀 One-Click Preview

To see the latest docs, we simply run:
```bash
./scripts/preview.sh
```
This script handles the build, verification, and starts the hot-reloading dev server.
