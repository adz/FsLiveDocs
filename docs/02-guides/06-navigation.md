---
title: Configure navigation and branding
---

# Configure navigation and branding

Edit `.livedocs/config.json` to configure the generated site.

```json
{
  "siteName": "Example Library",
  "repoUrl": "https://github.com/example/library",
  "logoText": "EX",
  "logoPath": "content/logo.svg",
  "logoDarkPath": "content/logo-dark.svg",
  "showSiteName": true,
  "stylesheet": "content/site.css",
  "themes": ["light", "dark"],
  "navigation": [
    { "label": "Guides", "href": "index.html" },
    { "label": "API", "href": "api.html" },
    { "label": "Source", "href": "https://github.com/example/library" }
  ]
}
```

## Add local assets

Place logos, stylesheets, images, and downloads under `docs/`.

Use site-root-relative paths in configuration. FsLiveDocs copies these files into current output and captures them in release capsules.

## Add navigation links

Use generated site paths for internal links and absolute HTTPS URLs for external links.

FsLiveDocs adjusts internal navigation for nested guide and history pages.

## Split one site into documentation sets

Use `docsSets` when one shared site needs distinct guide trees or API audiences. Each set keeps
its own title, route, sidebar, API surface, and F# verification prelude while sharing branding,
themes, search, cross-references, releases, and the version switcher.

```json
{
  "siteName": "Example Platform",
  "docsSets": [
    {
      "id": "public",
      "title": "Public SDK",
      "source": "docs",
      "path": "",
      "projects": ["src/Example.Sdk/Example.Sdk.fsproj"],
      "default": true,
      "sidebar": true,
      "api": true,
      "fSharpPrelude": "open Example.Sdk"
    },
    {
      "id": "operations",
      "title": "Operations handbook",
      "source": "docs/operations",
      "path": "operations",
      "projects": ["src/Example.Operations/Example.Operations.fsproj"],
      "default": false,
      "sidebar": false,
      "api": false,
      "fSharpPrelude": "open Example.Operations"
    }
  ]
}
```

Exactly one set must have `default: true`; it renders at `/`. Other sets default their `path`
to their `id` and render at `/<path>/`. Their API index, when enabled, is at `/<path>/api/`.
`id` is a stable lower-case slug: do not change it merely to rename a set.

`source` and every project path are repository-relative. Source roots may overlap. In the
example, files below `docs/operations/` belong only to `operations`, because the most-specific
source root owns a file. Generated output paths must still be unique.

The set's `projects` determine both its default checking context and the API entities it exposes.
A page's `project:` front matter must name one of those projects. `fSharpPrelude` overrides the
top-level prelude for that set. Set `sidebar` or `api` to `false` to omit that surface entirely.

Links are validated against all sets, so a guide can link to another set. API xrefs use the one
global symbol model and resolve to the set that exposes the target. Search remains site-wide and
records the set id/title as result metadata.

Omit `docsSets` to keep the original `docs/`, `/api.html`, and single-sidebar layout unchanged.
All commands remain site-wide: `audit`, `test`, `generate-tests`, `build`, `watch`, and `capture`
process the union of configured set projects and pages.

## Configure themes

FsLiveDocs renders with [Tailwind CSS](https://tailwindcss.com/) and [DaisyUI](https://daisyui.com/). Set `themes` to the DaisyUI theme names that readers can choose:

```json
{
  "themes": ["light", "dark", "cupcake", "business"]
}
```

The first theme is the default. Override it for one build with `--theme`:

```bash
dotnet livedocs build --theme business
```

See the [DaisyUI theme list](https://daisyui.com/docs/themes/) to preview the built-in themes.

## Add your own styling

Set `stylesheet` to a CSS file under `docs/`:

```json
{
  "stylesheet": "content/site.css"
}
```

Use that file for typography, spacing, brand colors, or component overrides. FsLiveDocs copies it into the site and stores it in release capsules.

DaisyUI themes use CSS variables under a `data-theme` selector. You can define a custom theme in the stylesheet:

```css
[data-theme="example"] {
  color-scheme: light;
  --p: 0.55 0.19 255;
  --s: 0.7 0.14 170;
  --a: 0.72 0.16 70;
  --n: 0.25 0.03 255;
  --b1: 0.98 0.01 255;
}
```

Add `"example"` to `themes` to expose it in the picker. Use the [DaisyUI theme generator](https://daisyui.com/theme-generator/) to choose values, and see [DaisyUI theme CSS](https://daisyui.com/docs/themes/#how-to-add-a-new-custom-theme) for the complete variable set.

FsLiveDocs markup uses DaisyUI component classes and Tailwind utility classes. Prefer theme variables and a small scoped stylesheet over replacing generated markup.

## Add source links

Set `repoUrl` to a GitHub repository URL. API members with source locations link to the corresponding file and line on the `main` branch.
