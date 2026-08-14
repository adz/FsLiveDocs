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

Set `themes` to the names exposed by the theme picker. The first configured or command-selected theme becomes the initial theme.

## Add source links

Set `repoUrl` to a GitHub repository URL. API members with source locations link to the corresponding file and line on the `main` branch.
