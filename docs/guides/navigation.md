---
title: Navigation and Ordering
weight: 0
type: explanation
---

# Sidebar Ordering and Sections

FsLiveDocs groups the left sidebar into categories so the documentation reads in a predictable order.

## How ordering works

1. Pages with `type: tutorial` appear first.
2. Pages with `type: how-to` appear next.
3. Pages with `type: explanation` follow.
4. Pages with `type: reference` come after that.
5. Pages without a `type` fall back to the generic Guides bucket.

Within each category, pages are sorted by `weight` and then by title.

## Why this matters

The sidebar is meant to reflect the Diátaxis structure:

1. tutorials for first-time setup,
2. how-to guides for repeatable tasks,
3. explanations for architecture and trade-offs,
4. API reference for the generated code model.

If you add a new guide, set its `type` and `weight` in frontmatter so it lands in the right place automatically.
