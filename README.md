# FsLiveDocs

[![CI](https://github.com/adz/FsLiveDocs/actions/workflows/ci.yml/badge.svg)](https://github.com/adz/FsLiveDocs/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/FsLiveDocs.svg?logo=nuget)](https://www.nuget.org/packages/FsLiveDocs)
[![Documentation](https://img.shields.io/badge/docs-GitHub%20Pages-blue?logo=github)](https://adz.github.io/FsLiveDocs/)
[![License](https://img.shields.io/github/license/adz/FsLiveDocs)](LICENSE)

FsLiveDocs can be the whole documentation site for an F# library: Markdown guides and generated API reference in one navigation and search index, without Hugo, Docsy, or Docusaurus.

It builds on FSharp.Formatting with richer API pages, API-aware links, source transclusion, checked examples, compiler tooltips, and immutable release documentation.

An ordinary F# fence is compiled against the project it documents:

````markdown
```fsharp
type Order = { Total: decimal }
let total orders = orders |> List.sumBy _.Total
```
````

If the API changes, the documentation build fails at the page and line that need fixing. Examples run only when explicitly marked `run` or `transcript`.

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs
dotnet livedocs init --discover-projects
dotnet build
dotnet livedocs audit
dotnet livedocs build
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

**[Read the documentation](https://adz.github.io/FsLiveDocs/)**
