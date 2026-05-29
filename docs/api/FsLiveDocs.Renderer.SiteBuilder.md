# 🏗️ FsLiveDocs.Renderer.SiteBuilder

The assembly line for your documentation. It takes the `PackageModel` and `ContentPage` list and transforms them into a beautiful, static HTML site.

## Design Philosophy

The `SiteBuilder` uses **Giraffe.ViewEngine** for fast, type-safe HTML generation. It prioritizes:
- **Performance**: Static site generation means instant load times.
- **Aesthetics**: Built-in support for DaisyUI and TailwindCSS.
- **Searchability**: Automatically indexes content using Pagefind.

## Key Functions

- `build`: Renders the entire site, including guides, API pages, and the search index.
- `renderEntityPage`: Generates the detailed view for a specific module or type.
- `generateLlmsTxt`: Produces an `llms.txt` file for AI-assisted development.

{{< example id="GenerateLlmsTxtExample" >}}

## Customization

You can control the look and feel of your site by passing a `theme` parameter (e.g., `emerald`, `dark`, `retro`).

```fsharp
SiteBuilder.build package pages versions "emerald" "" "output"
```
