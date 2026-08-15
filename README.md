# FsLiveDocs

FsLiveDocs builds verified, semantic, versioned documentation for F# libraries.

Use FsLiveDocs to:

- generate API reference pages from compiled projects;
- compile every F# guide example with the selected project settings;
- run examples only when you mark them for execution;
- add compiler-derived types and documentation tooltips to code blocks;
- capture immutable, renderer-neutral release capsules;
- rebuild historical sites without restoring or compiling old projects.

## Install FsLiveDocs

Add FsLiveDocs to a tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install FsLiveDocs --version 0.1.0
```

Restore the tool in CI:

```bash
dotnet tool restore
```

## Create a documentation site

Initialize the repository:

```bash
dotnet livedocs init --discover-projects
```

`--discover-projects` records the discovered `.fsproj` files in `.livedocs/config.json`. Commands use explicit project arguments first, then the configured list, then automatic discovery.

Build your project before FsLiveDocs reads its API:

```bash
dotnet build
```

Audit and build the documentation:

```bash
dotnet livedocs audit
dotnet livedocs build
```

Open a local preview while you edit:

```bash
dotnet livedocs watch --host 127.0.0.1 --port 5000
```

The generated site is in `output/`.

## Verify examples in tests

Generate stable xUnit cases from the same discovery result used by the build:

```bash
dotnet livedocs test
```

Generated tests call one FsLiveDocs runner interface. They don't reproduce coverage, compilation, or execution policy.

## Capture a release

Create a self-contained release capsule after verification succeeds:

```bash
dotnet livedocs capture \
  --version 1.4.0 \
  --output artifacts/your-library-livedocs-1.4.0.zip
```

Capture writes the capsule and a machine-readable `.report.json` file. It prints component sizes, inventory counts, compressed and uncompressed sizes, and the capsule SHA-256.

Validate the process without writing the requested output:

```bash
dotnet livedocs capture \
  --version 1.4.0 \
  --output artifacts/your-library-livedocs-1.4.0.zip \
  --dry-run
```

Inspect an existing capsule:

```bash
dotnet livedocs inspect artifacts/your-library-livedocs-1.4.0.zip
```

The capsule stores API meaning, canonical Markdown and assets, and compiler-derived code semantics. It does not store generated HTML.

## Build version history

Add a local capsule to the history index:

```bash
dotnet livedocs history-add 1.4.0 \
  --capsule artifacts/your-library-livedocs-1.4.0.zip
```

Add a remote capsule with its expected checksum:

```bash
dotnet livedocs history-add 1.4.0 \
  --url https://github.com/example/your-library/releases/download/v1.4.0/your-library-livedocs-1.4.0.zip \
  --sha256 <sha256>
```

Render every indexed version:

```bash
dotnet livedocs build-history .livedocs/history.json
```

FsLiveDocs verifies and caches remote capsules under `.livedocs/releases/`. Historical rendering does not compile the historical project.

## Learn more

- [Get started](docs/introduction.md)
- [Author and verify examples](docs/guides/verified-examples.md)
- [Capture and publish releases](docs/guides/releases.md)
- [Configure semantic code](docs/guides/semantic-code.md)
- [Use the command reference](docs/cheat-sheet.md)
- [Read the complete reference](docs/deep-reference.md)

## Develop FsLiveDocs

Build and test the repository:

```bash
dotnet build FsLiveDocs.sln
dotnet test FsLiveDocs.sln
```

See [Dogfood FsLiveDocs](docs/dogfooding.md) for the self-hosting workflow.
