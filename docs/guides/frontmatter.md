---
title: Frontmatter Reference
weight: 0
type: reference
---

# Frontmatter Reference

Frontmatter is the YAML block at the top of a Markdown file, wrapped in `---` lines.
FsLiveDocs reads it before rendering the page, and uses it to decide the page title, page metadata, and sidebar order.

## Basic shape

```yaml
---
title: Verified Examples
weight: 1
type: how-to
---
```

Anything below the closing `---` is normal Markdown content.

## Supported fields

| Field | Type | Required | What it does |
| :--- | :--- | :--- | :--- |
| `title` | string | Yes, when frontmatter is present | Sets the page title shown in navigation and the browser title. |
| `weight` | integer | Yes, when frontmatter is present | Sorts pages within the same folder-based sidebar group. Lower numbers come first. |
| `type` | string | No | Advisory metadata for the page itself. It is not used to build the sidebar. |

## How FsLiveDocs uses it

FsLiveDocs currently uses frontmatter for three things:

1. `title` controls how the page appears in the sidebar and in the browser title.
2. `weight` controls the order inside a folder group.
3. `type` describes the kind of page you are writing, but it does not drive sidebar grouping.

The sidebar itself is derived from the `docs/` folder structure, not from `type`.

## Practical examples

### Tutorial page

```yaml
---
title: Introduction
weight: 1
type: tutorial
---
```

Use this for a first-time walkthrough that teaches a reader how to get started.

### How-to page

```yaml
---
title: Verified Examples
weight: 1
type: how-to
---
```

Use this for a task-focused guide that solves a specific problem.

### Explanation page

```yaml
---
title: Navigation and Ordering
weight: 0
type: explanation
---
```

Use this when the page explains concepts, trade-offs, or structure.

### Reference page

```yaml
---
title: Cheat Sheet
weight: 9
type: reference
---
```

Use this for concise lookup material.

## Ordering rules

Pages in the same folder are ordered by:

1. `weight`
2. `title`

That means two pages with the same weight fall back to alphabetical title order.

## Defaults and omissions

If a Markdown file has no frontmatter, FsLiveDocs falls back to the file name as the title and uses a weight of `0`.

If frontmatter is present, keep the fields explicit so the sidebar stays stable and predictable.

## What to avoid

1. Do not use `type` to try to force a sidebar section.
2. Do not rely on implicit ordering if you care about the final sidebar position.
3. Do not leave titles vague if the page name alone will not make sense in the sidebar.

## When to use frontmatter

Use frontmatter on any page that should appear in the sidebar or needs a clear page title.
The more pages you add, the more important it becomes to set `weight` deliberately.
