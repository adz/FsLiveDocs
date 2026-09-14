# Blog paradigm feature spec

Status: draft. This document specifies an optional "blog paradigm" layered on top of
FsLiveDocs' existing documentation pipeline (frontmatter → `ContentMetadata` /
`ContentPage` → discovery → rendering). It does not describe a new site type; blog
behavior is additive to ordinary content pages.

Before implementing any part of this spec, read
[`RELEASE_ARTIFACT_RULES.md`](RELEASE_ARTIFACT_RULES.md). Several pieces here touch
persisted release artifacts and are compatibility work, not ordinary feature work.

## Overview / goals

FsLiveDocs currently treats all Markdown under `docs/` as reference documentation:
`ContentMetadata` carries only `Title`, `Type`, `Project`, `TargetFramework`, `Platform`
(`src/FsLiveDocs.Core/SiteModel.fs:46`), and pages are ordered purely by numeric folder
prefixes (`ContentProvider.fs:263`, `SiteModel.fs:60`). This spec adds the metadata,
derived indices, navigation, and generated pages needed to author a chronological blog
(or changelog, or news section) using the same discovery/shortcode/rendering pipeline,
without disturbing the existing reference-documentation behavior for pages that don't
opt in.

Goals:

- Let authors write dated, tagged, categorized posts as ordinary Markdown files with
  frontmatter, using the existing `ContentProvider` discovery walk.
- Derive listing, grouping, and navigation structures as pure functions over the
  existing `ContentPage list`, computed at build time, not persisted as HTML.
- Reuse the existing `{{< ... >}}` shortcode mechanism for in-page embedding rather
  than inventing a second templating layer.
- Produce standard blog affordances: index/tag/category/series pages, RSS/Atom, reading
  time, prev/next navigation, and (via embed only) comments.
- Keep every addition compatible with the release-capsule compatibility boundary:
  additive optional metadata fields, no new HTML persisted, no formatter-owned types
  introduced into `ContentMetadata` or `ContentPage`.

## Non-goals

- Native comments or any comment backend/storage. Comments are a third-party embed
  only (see below).
- A related-posts algorithm (content similarity, tag-overlap scoring, etc.).
- Full-text search. Pagefind already provides client-side search
  (`View.fs:447,740,799-800`), indexed as a post-build step; nothing here changes that.

## Data model changes

All changes are additive optional fields on `ContentMetadata`
(`src/FsLiveDocs.Core/SiteModel.fs:46`). None replace or rename existing fields.

```fsharp
type ContentMetadata = {
    Title: string
    Type: string option
    Project: string option
    TargetFramework: string option
    Platform: string option

    // New, all optional, all additive:
    Date: DateOnly option
    Tags: string list           // defaults to []
    Category: string option     // singular primary classification, distinct from Tags
    Draft: bool                 // defaults to false
    Summary: string option      // falls back to an auto-derived excerpt when absent
    Slug: string option         // overrides filename/title-derived URL segment
    Series: string option
    SeriesOrder: int option     // ordering within Series; falls back to Date order
    Comments: bool              // defaults to false; toggles the Giscus embed
    BlogList: BlogListingOptions option  // defaults for a {{< posts >}} listing on this page
}

type BlogListingOptions = {
    Layout: string option       // "list" (default), "compact", or "preview"
    Show: string list           // card fields: date, readingtime, summary, tags
    Limit: int option
    Tag: string option
    Category: string option
}
```

Notes per field:

- `Date`: `DateOnly`, not `DateTime` — a post has a publication date, not a timestamp.
  Pages without `Date` are not eligible for blog indices, RSS, or chronological
  nav, but remain ordinary documentation pages exactly as today.
- `Tags`: unordered, zero or more. Empty list is equivalent to "not tagged."
- `Category`: at most one, and optional — most lean blogs skip it entirely and rely
  on tags only. It is coarse-grained, answering "which section of the blog," not a
  topic taxonomy.
- `Draft`: excluded from build output and all derived indices unless the build is
  invoked with a `--drafts` flag (new CLI flag on the build/serve commands; out of
  scope for the persisted-artifact rules since it only affects what gets built).
- `Summary`: authored short description. When absent, an excerpt is derived from the
  first paragraph of the parsed Markdown body (plain text, truncated at a word boundary
  to at most 240 characters) at render/index time — this derivation
  is not persisted as a separate field; it's recomputed from the canonical Markdown
  each time it's needed, consistent with "store canonical renderer-neutral Markdown"
  in the release rules.
- `Slug`: when present, replaces the filename/title-derived segment used in
  `OutputPath` construction. **Settled permalink strategy: flat slugs**, e.g.
  `/blog/my-post/`, derived from `Slug` (or a title-derived slug) alone — not
  date-based paths like `/blog/2026/09/my-post/`. `Date` is used for sorting and
  display metadata only, never baked into the URL. Rationale: flat slugs are
  simpler and more durable — a post's URL doesn't change if its date is corrected,
  and readers/search engines get a stable link independent of publication
  chronology. `OutputPath` derivation remains a render-time computation from
  persisted fields (`Slug`, `FilePath`), never a persisted literal URL, per the
  release-artifact rules.
- `Series` / `SeriesOrder`: see Navigation and Derived Indices below.
- `Comments`: pure rendering flag; see Comments (Pluggable, Giscus first-class)
  section.
- `BlogList`: default attributes for a `{{< posts >}}` shortcode on the same page (see
  Shortcodes). Plain data like every other field; an attribute on the shortcode itself
  overrides the matching `BlogList` value.

**Category, Tags, and Series are independent axes with no implied relationship
between them:**

- **Tags** are free-form, many-per-post, fine-grained — the primary topical
  classification mechanism.
- **Category** is coarse-grained and optional, at most one per post — "which
  section of the blog," not a topic taxonomy. Most lean blogs skip it and rely on
  tags alone.
- **Series** is an orthogonal narrative-ordering axis, unrelated to category or
  tags. A post in a series carries its own tags/category independently; series
  membership never implies or defaults a category, and vice versa.

Templates render series navigation (part label, prev/next-in-series) visually
distinct from tag/category chips, since they communicate different things:
membership in an ordered narrative vs. topical/sectional classification.

### Compatibility notes (RELEASE_ARTIFACT_RULES.md)

`ContentMetadata` is part of the Content artifact's site metadata, which the release
rules require to be renderer-neutral, FsLiveDocs-owned meaning
(`RELEASE_ARTIFACT_RULES.md:59-63`). These new fields comply:

- All new fields are plain data (dates, strings, bools, lists, ints) — no HTML,
  no formatter- or compiler-owned types, no template markup.
- All fields are optional or default-valued, so existing content artifacts remain
  structurally valid: this is an additive schema change, not a breaking
  representation change, per `RELEASE_ARTIFACT_RULES.md:119-127`.
- Per the schema-compatibility rules, "do not rely on missing fields receiving empty
  values during deserialization" — this must still be an **explicit** compatibility
  decision, not an assumption. Concretely this means:
  - Bump the Content artifact's schema version for this change.
  - Add an explicit, deterministic migration from the prior schema version that
    populates `Date = None`, `Tags = []`, `Category = None`, `Draft = false`,
    `Summary = None`, `Slug = None`, `Series = None`, `SeriesOrder = None`,
    `Comments = false`, `BlogList = None` for capsules captured before this change.
  - Add capsule-only fixture(s)/tests that load an old-schema capsule through the
    migration and assert the defaulted values, per the "update schemas, fixtures,
    migrations, release guidance, and capsule-only tests together" instruction in
    `CLAUDE.md`.
- `Summary`'s auto-derived-excerpt fallback must be computed from the **stored
  canonical Markdown** at render time (or at index-build time from the same
  canonical Markdown), never persisted as pre-rendered HTML — this keeps it inside
  the "prefer Markdown produced after deterministic shortcode expansion" rule
  (`RELEASE_ARTIFACT_RULES.md:63`).
- The derived indices (`PostIndex`, `SeriesIndex`, reading time, prev/next) described
  below are computed from `ContentPage list` at build/render time and are **not**
  persisted into the release capsule at all — they are pure functions over already-
  persisted, renderer-neutral data, recomputable identically by any future renderer
  version. Nothing new needs to be added to the Content artifact schema to support
  them beyond the `ContentMetadata` fields above.
- `OutputPath` derivation (`SiteModel.fs:69`) is explicitly called out as
  URL-derivation logic. The release rules forbid persisting "URLs derived from the
  current page layout" — so whatever permalink strategy is chosen (see Open
  Decisions), it must remain a rendering-time derivation from persisted fields
  (`Slug`, `Date`, `FilePath`), not a persisted computed URL.

## Derived indices

Pure functions over `ContentPage list`, computed once per build alongside the existing
discovery pipeline (not persisted; see compatibility notes above).

```fsharp
type PostIndex = {
    ByDateDesc: ContentPage list                       // all posts with Date, sorted desc
    ByTag: Map<string, ContentPage list>                // tag -> posts, each sorted desc by date
    ByCategory: Map<string, ContentPage list>           // category -> posts, sorted desc by date
}

type SeriesEntry = {
    Post: ContentPage
    PartNumber: int          // 1-based position within the series
    PartCount: int           // total posts in the series
}

type SeriesIndex = {
    BySeriesName: Map<string, SeriesEntry list>
    // ordered by SeriesOrder ascending when present; posts without SeriesOrder
    // are interleaved by Date ascending relative to their SeriesOrder neighbors
}

val buildPostIndex: ContentPage list -> PostIndex
val buildSeriesIndex: ContentPage list -> SeriesIndex
```

Both exclude `Draft = true` posts unless the build was invoked with `--drafts`, applied
at the same point discovery already filters/orders pages, so drafts never leak into
derived indices, generated pages, or RSS by accident.

## Navigation behavior

- **Global prev/next**: chronological neighbors in `PostIndex.ByDateDesc` (the post
  immediately newer / older by date).
- **Series-local prev/next**: when a post has `Series = Some s`, its prev/next links
  are the adjacent `SeriesEntry` in `SeriesIndex.BySeriesName.[s]` (by `SeriesOrder`,
  falling back to date) — e.g. "Part 2 of 5" — and this **overrides** global
  chronological prev/next for that post. A post outside any series always uses global
  prev/next.
- Both are computed as a single function taking a `ContentPage`, the `PostIndex`, and
  the `SeriesIndex`, returning `{ Prev: ContentPage option; Next: ContentPage option }`
  plus, when in a series, the part label (`PartNumber`/`PartCount`) for display.

## Shortcodes

Both blog shortcodes must stand alone as their own Markdown paragraph; the renderer
expands them from the rendered page after the post and series indices exist, and leaves
the same syntax inside code spans and code blocks untouched.

Reuses the existing `{{< ... >}}` transclusion mechanism
(`ContentProvider.fs:28-36,275-421`); these are new shortcode kinds recognized by the
same expansion pass, expanded deterministically before content is persisted into the
Content artifact (consistent with `RELEASE_ARTIFACT_RULES.md:63` — stored Markdown is
post-expansion).

- `{{< posts tag="..." category="..." limit="..." layout="..." show="..." >}}` — embeds a
  listing of matching posts on any page. `tag` and `category` are optional filters
  (AND'd if both given); `limit` truncates the list. `layout` is `list` (titles only,
  default), `compact` (cards with the date), or `preview` (cards with date, reading
  time, summary, and tags); `show` overrides which card fields appear. Any attribute
  omitted from the shortcode falls back to the page's `BlogList` frontmatter; an unknown
  layout fails the build. Backed by `PostIndex`.
- `{{< series-nav >}}` — embeds the series part-navigation (or, on a series' own index
  page, the full ordered list of parts) for the current post. Backed by `SeriesIndex`
  and the current page's `Series` metadata. On a page without `Series` it renders nothing
  and the build reports a warning (an error under `--warn-as-error`); occurrences inside
  code spans or code blocks are documentation of the syntax and are ignored.

Because expansion happens deterministically at discovery time and the expanded result
is what gets stored, a later change to shortcode rendering does not change historical
capsules — consistent with the discovery-pipeline rules
(`RELEASE_ARTIFACT_RULES.md:65-71`).

## Generated pages & routing

New generated pages, produced by `SiteBuilder.fs` alongside existing page/index/API
generation:

| Path | Content |
| --- | --- |
| `/blog/index.html` | Latest N non-draft posts with a `Date` (e.g. 10), newest first |
| `/blog/page/<n>/index.html` | Next N posts, for `n >= 2`, cursor-chained (see Pagination below) |
| `/blog/tags/<tag>.html` | Posts with that tag, newest first, unpaginated |
| `/blog/category/<category>.html` | Posts in that category, newest first, unpaginated |
| `/blog/series/<name>.html` | Posts in that series, in series order, with part labels, unpaginated |

These are index-style generated pages, analogous to the existing API index generation
in `SiteBuilder.fs`, rendered through the same site layout (theme, navigation, and
documentation-set chrome) as guide pages, with every link relative to the generated
page so the site works under any hosting sub-path. They are not persisted content — they're regenerated from `PostIndex` /
`SeriesIndex` on every render, including historical re-renders of an old capsule.

Per-post `OutputPath` (the individual post's own permalink) uses the flat-slug
strategy described in Data model changes above.

### Pagination

**Settled: cursor-style "Older posts / Newer posts" links, not numbered pagination.**
Numbered pagination (page 1/2/3/.../N with total-count logic) was rejected as
low-value for a blog-scale content set.

- `/blog/index.html` shows the latest N posts (e.g. 10) from `PostIndex.ByDateDesc`.
- When more posts exist, the index carries a "← Older posts" link to
  `/blog/page/2/`, which shows the next N posts and carries both a "Newer posts →"
  link back and, if further posts remain, its own "← Older posts" link to
  `/blog/page/3/`, and so on.
- This is purely static-generated chunking of the sorted `PostIndex.ByDateDesc`
  into pages of N — no page-count or total-pages computation is needed; each page
  only needs to know whether a previous/next chunk exists.
- Tag, category, and series pages remain unpaginated for now (single page,
  however long) — they're expected to stay small relative to the full index.

**Settled tradeoff — page URLs are a moving window, not a stable permalink.** The
content at `/blog/page/2/` shifts over time as new posts are published (today's
page 2 becomes tomorrow's page 3, etc.), exactly as with WordPress, Jekyll, and
Hugo's equivalent pagination. This is acceptable because only individual post
URLs (flat slugs, see above) need to be stable and linkable; nobody treats "page
2 of the blog index" as a durable reference the way they treat a post's own URL.

## RSS / Atom

An XML feed generator (`/blog/feed.xml`, Atom preferred with RSS 2.0 as a secondary
target if both are wanted) derived purely from `PostIndex.ByDateDesc`: title, link,
publish date, and `Summary` (authored or derived excerpt) per entry. Generated at
render time from the same data as the HTML index pages — no separate persisted feed
state. Links in the feed are relative to the feed's own location (`blog/feed.xml`),
which readers resolve against the URL they fetched it from, because the site has no
configured absolute base URL; entry IDs are `urn:fslivedocs:post:<output path>`.
**Settled: drafts are excluded from RSS.** This use case treats the RSS
feed as not public, so no separate draft-preview mechanism is needed — drafts are
excluded from the feed by the same `Draft` flag (and `--drafts` gating) that
excludes them from the rest of the build, with no independent RSS-specific
draft policy.

## Reading time

Computed as a word-count-based estimate (words / 200 per minute, minimum one) from the
parsed Markdown AST of the post body, counting prose in paragraphs, headings, lists, and
quotes but not code blocks or shortcodes — the same parse already produced for HTML
rendering, not a separate persisted field. Displayed on post pages and in listings.
Recomputed at render time; not persisted, so a future change to the estimation
constant changes display for historical capsules without needing a migration.

## Comments (Pluggable, Giscus first-class)

Third-party embed only — no persisted model, no backend, and explicitly **outside**
the release-artifact compatibility boundary, since nothing here is stored in the
capsule.

**Settled: build one first-class, tested provider (Giscus) now, with an explicit
extension point so users aren't locked into it.** Site configuration is modeled
as a discriminated union (naming illustrative, not binding):

```fsharp
type CommentsProvider =
    | Giscus of GiscusSettings   // repo, category, theme, etc. — typed
    | Custom of rawHtml: string  // pass-through escape hatch, untested
    | NoComments
```

- **Giscus** (GitHub Discussions-backed) is the first-class, tested integration:
  typed settings (repo, repo ID, category, category ID, theme mapping) sourced from
  `SiteConfig`, themeable to match FsLiveDocs' DaisyUI theming.
- **Custom** is a raw-HTML/JS escape hatch: users can paste any third-party embed
  snippet (Disqus, utterances, Cusdis, a self-hosted widget, etc.) into the same
  slot the Giscus block would otherwise occupy. FsLiveDocs does not test or
  support these providers — it's a pure pass-through of user-supplied markup.
- **NoComments** disables the embed entirely (equivalent to the old `Comments =
  false` default).
- `Comments: bool` in frontmatter (default `false`) still toggles whether the
  configured provider's embed is emitted on that post's page; the *which
  provider* question is a site-level `SiteConfig` setting, not per-post.
- Rendering stays two composable pieces in `View.fs`: a provider-agnostic
  comments toggle/count link (a heading-level anchor, e.g. "Comments (3)") and an
  expanded embed region below it, populated by whichever provider is configured.
  Only the embed region's contents differ between `Giscus` and `Custom`; the
  toggle affordance is identical either way.
- The "count" shown in the toggle link is **not** static build output — comment
  counts are driven by provider JavaScript at runtime (e.g. Giscus's client-side
  reaction/comment count), unknown at build time. The toggle renders a
  placeholder/loading state until the provider's script populates it in-browser.
- No F# model represents comment content; the configured provider (GitHub via
  Giscus, or whatever the `Custom` snippet embeds) owns that data entirely.

## Open decisions

None remaining. `Category` is settled as optional (see Data model changes above),
consistent with the independent-axes design — no validation requires a dated post
to carry a `Category`.

## Implementation phasing

- **Phase 1 — metadata + indices + basic pages.** Add the new `ContentMetadata`
  fields (schema bump + migration + fixtures/tests per compatibility notes), implement
  `Draft` filtering and `--drafts` flag, implement `PostIndex` and the
  `/blog/index.html` (with cursor pagination to `/blog/page/<n>/`),
  `/blog/tags/<tag>.html`, `/blog/category/<cat>.html` generated pages, using the
  flat-slug permalink strategy.
- **Phase 2 — series + navigation.** Implement `SeriesIndex`, series-local prev/next
  overriding global prev/next, `/blog/series/<name>.html`, and the `{{< series-nav >}}`
  shortcode.
- **Phase 3 — RSS/Atom + reading time.** Implement the feed generator from
  `PostIndex`, reading-time estimation from the parsed AST, and the `{{< posts ... >}}`
  listing shortcode (can move earlier if useful once `PostIndex` exists in Phase 1).
- **Phase 4 — comments.** Add `SiteConfig` `CommentsProvider` configuration
  (`Giscus`/`Custom`/`NoComments`), the `Comments` frontmatter flag, and the
  `View.fs` toggle/embed templates, with Giscus as the tested first-class path.
