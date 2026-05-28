---
title: Navigation and Ordering
weight: 0
type: explanation
---

# Sidebar Ordering and Sections

FsLiveDocs groups the left sidebar automatically from the `docs/` folder structure so the navigation stays predictable as the site grows.

## How ordering works

1. Pages in the `docs/` root appear in the Overview section.
2. Pages in `docs/guides/` appear in the Guides section.
3. Pages in `docs/api/` appear in the API Docs section.
4. Generated API reference pages appear in the API Reference section.

Within each folder-based group, pages are sorted by `weight` and then by title.

## Why this matters

We use the Diátaxis structure to decide what kind of content belongs in the docs:

1. tutorials for first-time setup,
2. how-to guides for repeatable tasks,
3. explanations for architecture and trade-offs,
4. API reference for the generated code model.

That structure informs the content. The sidebar still follows the `docs/` folder layout so it remains automatic and easy to predict.

If you add a new page, place it in the right folder and set its `weight` if you want a specific order. The `type` frontmatter is advisory metadata for the page itself, not the sidebar.
