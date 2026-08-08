# FsLiveDocs

FsLiveDocs is a "verified documentation" engine for F#. It treats your documentation as a first-class citizen of your codebase, ensuring every example compiles and runs exactly as shown.

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](#)
[![Target](https://img.shields.io/badge/.NET-10.0-blue)](#)

## Key Features

- **Verified Docstrings**: Code in `/// <example>` tags is extracted and run against your actual project.
- **Snippet Transclusion**: Use `{{< snippet id="Name" >}}` to pull live code from `.fs` files.
- **Semantic Cross-References**: Link to API members using `xref:M:Namespace.Type.Method`.
- **Version History**: Rebuild complete historical sites from checksum-verified API models and tagged documentation trees.
- **Modern Search**: Integrated **Pagefind** for lightning-fast, zero-config static search.
- **🤖 LLM Ready**: Automatically generates `llms.txt` to help AI assistants understand your API.
- **⚡ Hot Reload**: Use `livedocs watch` for a live-rebuilding dev server.

## 🛠 Installation & Usage

1. **Setup Environment**:
   Ensure you have the .NET 10 SDK installed. We recommend using `mise`.
   ```bash
   mise use dotnet@10.0
   ```

2. **Initialize**:
   ```bash
   livedocs init
   ```

3. **Verify Examples**:
   ```bash
   livedocs test path/to/your/project.fsproj
   ```

4. **Build Static Site**:
   ```bash
   livedocs build path/to/your/project.fsproj
   ```

   Consumer branding can be configured in `.livedocs/config.json`:
   ```json
   {
     "siteName": "Example Library",
     "logoText": "EL",
     "logoPath": "content/logo-light.svg",
     "logoDarkPath": "content/logo-dark.svg",
     "showSiteName": true,
     "stylesheet": "content/site.css",
     "themes": ["light", "dark"]
   }
   ```
   Image paths may be root-relative site paths or absolute URLs. `logoText` remains the fallback when `logoPath` is
   absent. Files below the consumer's `docs/` tree are copied into the generated site, so a path such as
   `content/logo-light.svg` can be sourced from `docs/content/logo-light.svg`.

5. **Build release history**:
   ```bash
   livedocs extract path/to/your/project.fsproj --version 1.2.0 --output model.json
   livedocs build-history path/to/local-history-manifest.json
   ```

`extract` writes a schema-versioned API artifact. `build-history` verifies every artifact against the SHA-256 and
version declared by its local manifest, then renders each version from its own Markdown/assets using the currently
installed FsLiveDocs templates. Release automation is responsible for downloading immutable models and checking out
the matching tagged documentation trees before invoking the build.

## 📜 Publishing

Use the provided publish script to generate a production-ready artifact:
```bash
./scripts/publish.sh
```
The output will be located in `./artifacts/livedocs`.
