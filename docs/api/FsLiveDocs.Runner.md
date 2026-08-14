# FsLiveDocs.Runner

`FsLiveDocs.Runner` owns compiler and execution behavior.

It evaluates project settings through MSBuild, creates compiler inputs, maps diagnostics, extracts semantic tokens, runs FSI examples, and executes generated cases.

Keep discovery and persisted models in Core. Keep HTML generation in Renderer.
