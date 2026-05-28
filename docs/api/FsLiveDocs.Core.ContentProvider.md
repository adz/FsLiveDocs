# 🧩 FsLiveDocs.Core.ContentProvider

Provides capabilities to load, parse, and resolve Markdown documentation pages. This module is the backbone of the "Live" aspect of FsLiveDocs, handling snippets, examples, and cross-references.

## Key Functions

- `scanDocs`: Discovers all Markdown files in the `docs/` directory.
- `loadPage`: Processes a single Markdown file, extracting frontmatter and resolving shortcodes.
- `resolveSnippets`: The heart of the transclusion engine. It replaces `{{< snippet >}}` and `{{< example >}}` tags with actual code from your project.

## Examples

### Loading a Page

You can load a documentation page and have its shortcodes resolved automatically.

{{< example id="ResolveSnippetExample" >}}

## How it fits in

The `ContentProvider` works in tandem with the `PackageModel`. It uses the extracted symbols to resolve `xref:` links and `{{< example >}}` tags, ensuring that your documentation is always in sync with your code.
