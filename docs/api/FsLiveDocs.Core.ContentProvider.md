# ContentProvider

`ContentProvider` loads Markdown, parses page metadata, expands transclusions, resolves semantic references, validates links, and copies documentation assets.

Capture uses `expandTransclusions` to create canonical Markdown while preserving `xref` identifiers.

Rendering uses `resolveSnippets` to resolve remaining semantic references and format matched semantic code blocks.

Use `scanDocsWithOptions` when you already have a semantic artifact. Use `scanDocs` for current syntax-only or default behavior.
