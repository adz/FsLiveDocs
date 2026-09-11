# Dogfood Axial and Reified

## Problem

FsLiveDocs hand-rolls the same effects Axial and Reified exist to formalize, and the project wants to prove both
libraries under real load before their 1.0s:

- **Async/Result orchestration** across the compile/check/verify pipeline (`FsLiveDocs.Runner`:
  `DocumentationCompiler.fs`, `GeneratedVerification.fs`, `DocTestRunner.fs`, `FsiTranscriptRunner.fs`,
  `SemanticExtractor.fs`; `FsLiveDocs.Core/SymbolLister.fs`; `FsLiveDocs.Cli`: `PackageExtraction.fs`,
  `DocAnalysis.fs`, `CommandLine.fs`, `ReleaseCapture.fs`, `ReleaseHistoryCommands.fs`, `Workspace.fs`) — 14
  `async {}` blocks and `Result` usage across 12 files, no typed error union, no typed environment.
- **Raw `System.Diagnostics.Process`** shell-outs in four files (`dotnet msbuild` in `DocumentationCompiler.fs`,
  `git` in `ReleaseCapture.fs`, `npx pagefind` in `Program.fs` and `ReleaseHistoryCommands.fs`), each hand-rolling
  `ProcessStartInfo`, stream redirection, blocking `WaitForExit`, and ad hoc mixed-stdout/stderr error detection.
- **~195 raw `File`/`Directory` calls** across 20 files, **Console output** in 4 files, **`HttpClient`** in
  `ReleaseHistoryCommands.fs`/`ReleaseCapsule.fs`, and an environment-variable read in `ReleaseHistoryCommands.fs`.
- **Hand-rolled JSON serialization** (`Core/Serialization.fs`: reflection-based Newtonsoft converters for F#
  unions/options/lists), **manual schema-version `JObject` parsing** with `invalidOp` (`Core/ReleaseCapsule.fs`),
  and **ad hoc path-safety validation** with `invalidOp` (`Core/DocsSetModel.fs`).

`CLAUDE.md` declares persisted release artifacts (`ReleaseCapsule`, `HistoryModel`, etc.) a 1.0 compatibility
boundary — schema/migration changes there are compatibility work per `dev-docs/RELEASE_ARTIFACT_RULES.md`.

## Decision

Adopt Axial for every effect above (async/Result orchestration, Process, FileSystem, Console, HttpClient, env vars)
and Reified for serialization/schema-versioning/validation — full coverage, not partial adoption. Any place where
Axial's or Reified's current API doesn't cleanly fit gets fixed upstream in those repos, not worked around here.

### Environment and error shape

One typed environment **per subsystem** (`RunnerEnvironment`, `CliEnvironment`, ...), composed at call boundaries
via `Flow.localEnv`, not one flat `AppEnvironment` implementing every `IHas*`. A pure `Renderer` flow that cannot
shell out to `git` should not carry `IHasProcess` in its type.

Matching per-subsystem closed error unions (e.g. `RunnerError = ProcessFailed of ProcessError | CheckFailed of ... `),
with a single `AppError` wrapper only at the final reporting boundary (`Program.fs`), mirroring how
`Axial.Process.ProcessError.describe`/`exitCode` already collapse to host-exit-code behavior.

### Verified against real APIs, not assumed

- `Axial.Process` already solves the MSBuild merged-stdout/stderr diagnosis pain `DocumentationCompiler.fs`'s
  `runMsBuild` hand-rolls (`StageResult.StdErrTail`, `ProcessError.StageFailed`) — no gap.
  `ScriptEnvironment`/`Script.run` in `Axial.Process` is the target shape for `Program.fs`'s eventual entry point.
- `Axial.PlatformService.IEnvironmentVariables` already covers the env-var read in `ReleaseHistoryCommands.fs` — no
  gap.
- `Reified.Schema`'s `Contract.fs` already has the stepwise `n-1 -> n` migration model (`Contract.supersedes`,
  `ContractError`) `ReleaseCapsule.fs` needs for its schema-version gate — no gap, though wiring it up is
  compatibility work per `RELEASE_ARTIFACT_RULES.md` and must update schemas/fixtures/migrations/release notes
  together.
- `Axial.Layers` had scoped provisioning but no pooled-resource combinator — real gap, now closed. `Layer.pool` and
  `Pool<'resource>` shipped in Axial 0.9.0 (`dev-docs/releases/0.9.0.md` in the Axial repo); the Runner's
  `checkers: Lazy<FSharpChecker>[]` round-robin pool and `optionsCache` (`DocumentationCompiler.fs:201-222`) should
  be rebuilt on it instead of hand-rolled `Interlocked.Increment` + array indexing.

### Sequencing

1. ~~Add `Layer.pool` to `Axial.Layers`~~ — done, shipped in Axial 0.9.0.
2. Migrate `FsLiveDocs.Runner`'s compile/check/verify pipeline to `Axial.Flow` with a `RunnerEnvironment` wrapping
   an `ICompilerService` (FsLiveDocs-owned for now — FCS-specific, not Axial's concern) built on `Layer.pool`.
3. Migrate Process shell-outs (`DocumentationCompiler.fs`'s `runMsBuild` first — it already shows the exact pain
   `Axial.Process` fixes; `ReleaseCapture.fs`'s git shell-out is a small second target) and FileIO alongside it,
   since both are the same underlying problem (typed-env-free, ad hoc error handling, blocking calls inside async
   code).
4. Migrate `Core/ReleaseCapsule.fs` to `Reified.Schema` (see "Dropping Newtonsoft" below) — highest-value Reified
   target since it's the actual compatibility-boundary artifact — then `Core/DocsSetModel.fs`'s path-safety checks
   to `Reified.Constraint`/`Refinements`.
5. CLI-level (`Program.fs`, `CommandLine.fs`) glues both building blocks together last.

### Dropping Newtonsoft: exact wire-format compatibility

Goal: remove the Newtonsoft dependency from the runtime entirely, with **no schema-version bump** — new Reified
codec output must be byte-identical to today's `Core/Serialization.fs` output, verified rather than assumed.

Reviewed the current format against Reified's actual codec support, piece by piece:

- Records serialize as PascalCase field names (no contract resolver applied today). Reified's schema DSL defaults
  field wire-names to camelCase (`field _.Email`), so every field needs `fieldAs` (or an equivalent bulk
  naming-policy override) to pin the existing PascalCase name — not automatic, must be done deliberately per field.
- `list<'a>` → plain JSON array. Matches Reified's `listEncoder`/`listDecoder` as-is.
- `option<'a>` → `null` for `None`, bare unwrapped value for `Some` (not `{"Some": ...}`). Matches Reified's
  `OptionValueDefinition` (`NoneValue`/`WrapSome`) as-is.
- Fieldless unions (`FSharpUnionConverter` in `Serialization.fs`) → bare JSON string of the case name. This
  converter always writes just `case.Name` regardless of case fields, so it is only correct today because every
  union actually used in the persisted models happens to be fieldless — audit this assumption before migrating, since
  a union with payload data hitting this converter would have silently lost it. Reified has this exact shape as a
  named, built-in profile: `Reified.UnionRepresentations.compactExternal = UnionRepresentation.External(UnionPayloadStyle.Named, true)`,
  documented as "a compact external representation with fieldless cases encoded as strings." Use it directly.
- Not yet verified: `DateTimeOffset`/number formatting round-trip. Check mechanically, not by assumption.

Verification plan: round-trip every persisted model type through the new codec against a corpus of **real historical
capsules** — Axial's own, Reified's own, BinaryParsec's, Elmish.Avalonia.Glue's, and FsLiveDocs' own — diffing bytes.
Only remove Newtonsoft from the runtime once that corpus round-trips clean.

### Compatibility for already-published capsules

No format change requires existing capsules to be rewritten or regenerated. `ReleaseCapsule.fs` already has the
pattern for this: `LegacyContent.migrate` decodes schema-1 bytes with the old shape and deterministically lifts them
to schema-2 at `load` time, explicitly commented as "kept for migration only." Apply the same shape here even without
a version bump:

- New `serialize` (writes) uses the Reified codec from day one.
- `load` (reads) keeps a bounded-lifetime legacy decoder for bytes that don't match the new codec's expectations,
  the same role `Contract.supersedes`/`ContractError` play in `Reified.Schema` for stepwise version migration.
- Consumers never see the difference — `load` always returns the current in-memory model.
- The legacy decoder can be deleted once nothing still needs to read old-format capsules — a later judgment call, not
  a blocker now. A one-time `fslivedocs migrate-history` tool to physically rewrite historical capsules and retire
  the legacy decoder sooner is optional cleanup, not required for correctness, and is out of scope for this pass.

## Follow-up: F# compiler adapter as an agent-facing surface (out of scope for this pass)

`SemanticExtractor.fs`/`SymbolLister.fs` (796 + 211 lines) already derive real value beyond raw FCS: XML-doc
resolution cached by file version, symbols normalized into renderer-neutral records, tooltip signature matching.
FCS's own API is awkward enough at these boundaries that FsLiveDocs already works around it defensively (`FSharpXmlDoc`
unpacked via `FSharpValue.GetUnionFields` reflection, with a comment admitting it's there to tolerate FCS instability).

Extracting this as `Axial.FSharpCompiler` (see `Axial/dev-docs/current-ideas/fsharp-compiler-adapter.md`) is worth
doing not just for internal ergonomics but because it turns FsLiveDocs' internal symbol-extraction into a
**structured, agent-queryable API-surface service** instead of an LLM grepping source:

- Grep gives raw text: no resolved public/internal accessibility, no doc-comment-to-symbol matching, requires the
  agent to re-parse F# syntax and gets it wrong on non-trivial cases (attributes, `[<AutoOpen>]`, extension members,
  multi-case unions).
- The adapter gives a flat, already-resolved `ProjectSymbols` (signature, kind, accessibility, doc comment) —
  cheaper in tokens than dumping source, and ground-truth from the compiler rather than a text-pattern guess.
- Scope grows beyond symbol extraction: also run compiler warning checks and FsLiveDocs' own doc-code-fence
  ("livedocs") checks as named `ICompiler` operations, not just a single diagnostics call.
- Natural end state is a CLI/MCP surface (`fslivedocs symbols <project> --json`, `fslivedocs check <project>`) that
  agents (including sessions like this one) call directly instead of grepping FsLiveDocs' or any target project's
  source — closing the loop, since FsLiveDocs already builds this derived model for human-facing docs today.

Sequence after the migration above lands, informed by real usage rather than speculative design. Ties to the
existing out-of-scope note in `dev-docs/new-ideas/better-dogfooding.md` about a future `FsLiveDocs.Api`/`SDK`
package needing "intentional request and result models, cancellation, progress, error handling, and a separate
compatibility promise."
