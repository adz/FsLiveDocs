# FsLiveDocs API

FsLiveDocs is split into four packages with explicit responsibilities.

- `FsLiveDocs.Core` owns persisted models, discovery, content, extraction, history, and capsules.
- `FsLiveDocs.Runner` owns compiler evaluation, semantic extraction, transcript execution, and generated verification.
- `FsLiveDocs.Renderer` converts renderer-neutral models into the current static site.
- `FsLiveDocs.Cli` coordinates user commands and release workflows.

Start with [FsLiveDocs.Core](FsLiveDocs.Core.md) when you integrate models or content.

Use [FsLiveDocs.Runner](FsLiveDocs.Runner.md) for verification and semantic analysis.

Use [FsLiveDocs.Renderer](FsLiveDocs.Renderer.md) for site generation.
