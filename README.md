# FsLiveDocs

FsLiveDocs is a "verified documentation" engine for F#. It treats your documentation as a first-class citizen of your codebase, ensuring every example compiles and runs exactly as shown.

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](#)
[![Target](https://img.shields.io/badge/.NET-10.0-blue)](#)

## Key Features

- **Verified Docstrings**: Code in `/// <example>` tags is extracted and run against your actual project.
- **Snippet Transclusion**: Use `{{< snippet id="Name" >}}` to pull live code from `.fs` files.
- **Semantic Cross-References**: Link to API members using `xref:M:Namespace.Type.Method`.
- **Version History**: Keep multiple versions of your API documentation using fast JSON snapshots.
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

## 📜 Publishing

Use the provided publish script to generate a production-ready artifact:
```bash
./scripts/publish.sh
```
The output will be located in `./artifacts/livedocs`.
