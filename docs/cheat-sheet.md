# FsLiveDocs (Atlas) Cheat Sheet

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
| `livedocs test <fsproj>` | Extract and run all docstring examples. |
| `livedocs build <fsproj>` | Generate the static documentation site. |
| `livedocs watch <fsproj>` | Start dev server with live rebuilds. |
| `livedocs theme <name>` | Set DaisyUI theme (e.g., `dark`, `cupcake`). |

## 🧪 Verifying Docstrings

Add examples directly to your function documentation:

```fsharp
/// <summary>Adds two integers.</summary>
/// <example name="AddTest">
/// let result = Math.add 1 2
/// // EXPECTED: 3
/// </example>
let add x y = x + y
```

## 🔗 Shortcodes

| Shortcode | Description |
| :--- | :--- |
| `{{< snippet id="X" >}}` | Pull code from source file marked with `<snippet:X>`. |
| `{{< example id="X" >}}` | Pull verified example with name `X`. |
| `xref:M:Namespace.Func` | Create a semantic link to an API member. |

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
