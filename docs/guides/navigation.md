---
title: Navigation and Ordering
type: explanation
---

# Sidebar Ordering and Sections

FsLiveDocs groups the left sidebar automatically from the `docs/` folder structure so the navigation stays predictable as the site grows.

## How ordering works

1. Pages in the `docs/` root appear in the Overview section.
2. Pages in `docs/guides/` appear in the Guides section.
3. Pages in `docs/api/` appear in the API Docs section.
4. Generated API reference pages appear in the API Reference section.

Subfolders become nested, collapsible groups at any depth. A subfolder's `_index.md` supplies its displayed title;
otherwise FsLiveDocs derives the title from the folder name. Each group starts closed unless it contains the current
page. Files and child folders participate in one ordering, using their numeric filename prefixes. FsLiveDocs removes
those prefixes from generated URLs and fallback titles.

For example, `01-overview.md`, `02-tutorials/`, and `03-reference.md` appear in that order. Inside `02-tutorials/`,
use the same convention (`01-first-steps.md`, `02-next-steps.md`, and so on).

## Why this matters

We use the Diátaxis structure to decide what kind of content belongs in the docs:

1. tutorials for first-time setup,
2. how-to guides for repeatable tasks,
3. explanations for architecture and trade-offs,
4. API reference for the generated code model.

That structure informs the content. The sidebar still follows the `docs/` folder layout so it remains automatic and easy to predict.

If you add a page or folder, place it in the right directory and give it a numeric prefix for its position. The `type`
frontmatter is advisory metadata for the page itself, not the sidebar.

For a field-by-field reference, see [Frontmatter Reference](frontmatter.html).
