---
title: Frontmatter Reference
type: reference
---

# Frontmatter Reference

Frontmatter is the YAML block at the top of a Markdown file, wrapped in `---` lines.
FsLiveDocs reads it before rendering the page and uses it for the page title and advisory metadata.

## Basic shape

```yaml
---
title: Verified Examples
type: how-to
---
```

Anything below the closing `---` is normal Markdown content.

## Supported fields

| Field | Type | Required | What it does |
| :--- | :--- | :--- | :--- |
| `title` | string | Yes, when frontmatter is present | Sets the page title shown in navigation and the browser title. |
| `type` | string | No | Advisory metadata for the page itself. It is not used to build the sidebar. |

## How FsLiveDocs uses it

FsLiveDocs currently uses frontmatter for two things:

1. `title` controls how the page appears in the sidebar and in the browser title.
2. `type` describes the kind of page you are writing, but it does not drive sidebar grouping.

The sidebar itself is derived from the `docs/` folder structure, not from `type`.

## Practical examples

### Tutorial page

```yaml
---
title: Introduction
type: tutorial
---
```

Use this for a first-time walkthrough that teaches a reader how to get started.

### How-to page

```yaml
---
title: Verified Examples
type: how-to
---
```

Use this for a task-focused guide that solves a specific problem.

### Explanation page

```yaml
---
title: Navigation and Ordering
type: explanation
---
```

Use this when the page explains concepts, trade-offs, or structure.

### Reference page

```yaml
---
title: Cheat Sheet
type: reference
---
```

Use this for concise lookup material.

## Ordering rules

Files and folders in the same directory are ordered together by a numeric prefix such as `01-`, `02-`, or `03-`.
The prefix is omitted from generated URLs and fallback titles. Items without a prefix follow numbered items and are
ordered by title.

## Defaults and omissions

If a Markdown file has no frontmatter, FsLiveDocs falls back to the file name as the title.

Use numeric file and folder prefixes to keep the sidebar stable and predictable.

## What to avoid

1. Do not use `type` to try to force a sidebar section.
2. Do not omit numeric prefixes if you care about the final sidebar position.
3. Do not leave titles vague if the page name alone will not make sense in the sidebar.

## When to use frontmatter

Use frontmatter when a page needs a clearer title or content type than its filename conveys.
