# 🎨 FsLiveDocs.Renderer

The visual layer of FsLiveDocs. This namespace contains the logic for rendering the hierarchical `PackageModel` into a modern, responsive static website.

## Key Modules

- **View**: Functional HTML templates using Giraffe.ViewEngine.
- **SiteBuilder**: Orchestrates the multi-page build process.
- **Url**: Utility module for generating stable, cross-referenced URLs.

## Features

- **Responsive Design**: Looks great on mobile and desktop.
- **Dark Mode**: Integrated DaisyUI themes.
- **Search**: Client-side full-text search.

## Example Output

The renderer also has a documented example that shows the summary page it produces for LLM-friendly browsing:

{{< example id="GenerateLlmsTxtExample" >}}

That transcript stays close to the actual `SiteBuilder` implementation, so the page is documenting real output rather than a synthetic toy sample.
