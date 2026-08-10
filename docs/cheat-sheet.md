---
title: Cheat Sheet
weight: 9
type: reference
---

# FsLiveDocs Cheat Sheet

## 🛠 Project Structure

- `./src/`: Core, Runner, Renderer, and CLI projects.
- `./tests/`: xUnit test suite.
- `./artifacts/`: Build outputs (managed by .NET 10).
- `./scripts/`: Automation scripts (`publish.sh`).

## 🚀 CLI Commands

| Command | Description |
| :--- | :--- |
| `livedocs init` | Scaffold a new project structure (`docs/` folder). |
| `livedocs ci` | Generate GitHub Actions workflow. |
| `livedocs generate-tests <fsproj...>` | Generate a Verify-based snapshot test project. |
| `livedocs test <fsproj>` | Run the legacy direct docstring verifier. |
| `livedocs audit <fsproj...>` | MSBuild-evaluate and compile all expanded F# blocks; report mapped modes, exclusions, and failures. |
| `livedocs build <fsproj>` | Generate the static documentation site. |
| `livedocs watch <fsproj>` | Start dev server with live rebuilds. |
| `livedocs theme <name>` | Set DaisyUI theme (e.g., `dark`, `cupcake`). |

## 🧪 Verifying Docstrings

Add examples directly to your function documentation. Transcript-style examples are picked up automatically; if you want to be explicit, add `data-livedocs="snapshot"`:

```fsharp
/// <summary>Adds two integers.</summary>
/// <example name="AddTest" data-livedocs="snapshot">
/// > Math.add 1 2;;
/// val it: int = 3
/// </example>
let add x y = x + y
```

## 🔗 Shortcodes

| Shortcode | Description |
| :--- | :--- |
| `{{< snippet id="X" >}}` | Pull code from source file marked with `<snippet:X>`. |
| `{{< example id="X" >}}` | Pull verified example with name `X`. |
| `xref:M:Namespace.Func` | Create a semantic link to an API member. |

## F# Fence Contracts

| Fence info | Meaning |
| :--- | :--- |
| `fsharp` | Compile in the shared page context; do not execute. |
| `fsharp prepare` | Hidden setup for later page blocks. |
| `fsharp isolated` | Compile alone. |
| `fsharp run` | Compile, then explicitly execute. |
| `fsharp transcript` | Verify an FSI transcript. |
| `fsharp no-check reason="…"` | Display deliberate pseudocode with an audit reason. |

## 🎨 DaisyUI Themes

Available themes include: `light`, `dark`, `cupcake`, `bumblebee`, `emerald`, `corporate`, `synthwave`, `retro`, `cyberpunk`, `valentine`, `halloween`, `garden`, `forest`, `aqua`, `lofi`, `pastel`, `fantasy`, `wireframe`, `black`, `luxury`, `dracula`, `cmyk`, `autumn`, `business`, `acid`, `lemonade`, `night`, `coffee`, `winter`.

---

## 🏗 Build & Publish

```bash
# Full build and test
mise x -- dotnet test

# Build production binary
./scripts/publish.sh

# Run production binary
./artifacts/livedocs --help
```
