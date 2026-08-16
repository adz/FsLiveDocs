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
