---
title: Publish a blog
---

# Publish a blog

Any Markdown page with a `date` in its front matter becomes a blog post. Pages without a `date` stay ordinary documentation pages.

```yaml
---
title: Announcing 1.0
date: 2026-09-14
summary: What changed on the road to 1.0.
tags: [release, fsharp]
category: news
slug: announcing-1-0
series: Road to 1.0
seriesOrder: 3
comments: true
---
```

## Post front matter

| Field | Default | Meaning |
| --- | --- | --- |
| `date` | none | Publication date (`yyyy-mm-dd`). Makes the page a post. |
| `tags` | `[]` | Topics. A post can have any number of tags. |
| `category` | none | One optional section of the blog, such as `news`. |
| `draft` | `false` | Leaves the post out of the build unless you pass `--drafts`. |
| `summary` | first paragraph | Short description shown in listings and the feed. |
| `slug` | file name | The post's URL segment. |
| `series` | none | Name of an ordered series of posts. |
| `seriesOrder` | publication date | Position within the series. |
| `comments` | `false` | Shows the site's comments embed on this post. |
| `blogList` | none | Default options for a `{{< posts >}}` listing on this page. |

Tags, categories, and series are independent. Being in a series never implies a tag or category.

## URLs

Each post is published at `blog/<slug>/index.html`. The date is never part of the URL, so correcting a date doesn't break links. Pick a `slug` when a post is first published and keep it.

## Generated pages

A build generates these pages from every non-draft post. They use the same layout, theme, and navigation as the rest of the site.

| Path | Content |
| --- | --- |
| `blog/index.html` | The ten newest posts, with a link to older posts. |
| `blog/page/<n>/index.html` | The next ten posts, with newer and older links. |
| `blog/tags/<tag>.html` | Every post with that tag, newest first. |
| `blog/category/<category>.html` | Every post in that category, newest first. |
| `blog/series/<series>.html` | A series' posts in order, labeled "Part n of m". |
| `blog/feed.xml` | An Atom feed of every post. |

Tag, category, and series names are lower-cased in these paths. Numbered pages such as `blog/page/2/` shift as you publish, so link to posts, not to listing pages.

All links in generated pages and the feed are relative, so the site works when hosted under a sub-path.

Each post shows its date and an estimated reading time, based on the prose in its Markdown at 200 words per minute. Code blocks and shortcodes don't count.

A post in a series links to the previous and next parts of that series. Other posts link to the next newer and older posts.

## Drafts

Posts with `draft: true` are left out of the whole build: their own pages, listings, series, and the feed. Preview them locally with:

```bash
livedocs watch --drafts
```

`livedocs build --drafts` works the same way. Never pass `--drafts` in a publishing build.

## List posts on a page

Use the `posts` shortcode to list posts on any page. Write it on its own line, with a blank line before and after:

```markdown
{{< posts tag="fsharp" limit="5" layout="preview" >}}
```

| Attribute | Default | Meaning |
| --- | --- | --- |
| `tag` | none | Only posts with this tag. |
| `category` | none | Only posts in this category. `tag` and `category` combine. |
| `limit` | none | The maximum number of posts. |
| `layout` | `list` | `list` (titles), `compact` (cards with dates), or `preview` (cards with summaries). |
| `show` | depends on `layout` | Comma-separated fields to show on a card: `date`, `readingtime`, `summary`, `tags`. |

Any attribute you leave off can come from the page's `blogList` front matter. This keeps a page's listing settings with its other metadata:

```yaml
---
title: News
blogList:
  layout: preview
  limit: 6
  category: news
  show: [date, summary]
---

{{< posts >}}
```

An attribute written in the shortcode overrides `blogList`. An unknown `layout` fails the build.

## Show series navigation

Use `{{< series-nav >}}` on a post to list every part of its series. On a page without `series` front matter it renders nothing, and the build prints a warning. With `--warn-as-error` that warning fails the build.

## Comments

FsLiveDocs doesn't store comments. It embeds a third-party provider on posts that set `comments: true`. Configure the provider once in `.livedocs/config.json`.

### Giscus

[Giscus](https://giscus.app) keeps comments in GitHub Discussions. Copy the IDs from the giscus configuration page:

```json
{
  "commentsProvider": {
    "kind": "giscus",
    "repo": "example/library",
    "repoId": "R_kgDO...",
    "category": "Announcements",
    "categoryId": "DIC_kwDO...",
    "theme": "preferred_color_scheme"
  }
}
```

`theme` is optional. It defaults to the site's theme.

### Other providers

Use `custom` to paste any provider's embed snippet:

```json
{
  "commentsProvider": {
    "kind": "custom",
    "html": "<script src=\"https://example.com/embed.js\" async></script>"
  }
}
```

FsLiveDocs inserts this HTML unchanged and doesn't test these providers.

Either way, a post shows a "Comments" heading that links to the embed. With Giscus the heading shows the number of comments once the embed has loaded in the browser. The build can't know the count, so the heading shows a placeholder until then.

Comment settings are never stored in release capsules. When you rebuild historical releases, they use the current configuration.
