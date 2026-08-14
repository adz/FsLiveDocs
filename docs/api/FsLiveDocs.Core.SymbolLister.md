# SymbolLister

`SymbolLister` extracts public symbols and XML documentation from built F# projects.

It converts formatter output immediately:

- signatures and type displays become plain text;
- XML documentation becomes FsLiveDocs-owned nodes;
- compiler references remain semantic symbol IDs;
- examples become `ExampleModel` values.

Use `extractFromProjectWithDiagnostics` when you need API quality diagnostics. Use `merge` to combine several package models with provenance.
