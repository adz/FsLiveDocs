# Refactor plan: model split and legacy verification retirement

**Status: implemented.** Kept as the record of why each change was made.

| Part | Outcome |
|---|---|
| Shared page walk | `65fc2d3` — audit, build and generated tests share `documentationPages` |
| A — split `Models.fs` | `e7b2c01` — seven files, verified as a pure move |
| B — retire legacy verifier | `ba7a3d8` — `verifyExamples` deleted, `test` re-based on the current path |
| C — verify examples by default | `4e8d327`, `621a07c`, `000f3aa`, `4621842` — steps 1-3; step 4 declined |

Deviations from the plan as written are noted in the relevant sections below.

---

## Part A — split `Models.fs`

### What is there now

`src/FsLiveDocs.Core/Models.fs` is 391 lines holding five unrelated concerns:

| Concern | Types |
|---|---|
| Release artifacts | `SourceLink`, `ExampleModel`, `ParameterModel`, `MemberModel`, `EntityKind`, `EntityModel`, `ScenarioModel`, `PackageInfo`, `PackageModel`, `ApiModelArtifact` |
| Run diagnostics | `ApiDiagnostic` |
| Semantic data | `SemanticTokenKind`, `SemanticToken`, `SemanticLine`, `SemanticTooltip*`, `SemanticDiagnostic*`, `SemanticCodeBlock`, `SemanticPage`, `SemanticDocumentationArtifact` |
| History | `HistoryEntry`, `HistoryManifest` |
| Runner results | `ExampleStatus`, `ExampleSnapshotModel`, `ProjectSnapshotModel` |
| Site and content | `NavigationItem`, `SiteConfig`, `ContentMetadata`, `ContentPage`, `ResolvedProject` |
| Serialization | `FSharpListConverter`, `FSharpOptionConverter`, `FSharpUnionConverter`, `module Serialization` |

Serialization is the odd tenant: converters are behaviour, not model, and they depend on
nothing else in the file.

### Why this is low risk

Every type stays in `namespace FsLiveDocs.Core`. Splitting one file into several within the
same namespace changes no call site anywhere — only `FsLiveDocs.Core.fsproj` compile order.
No `open` needs touching in Runner, Renderer, Cli or the tests.

### Target files, in compile order

1. `ApiModel.fs` — release artifacts, plus the `ExampleModel` augmentation. Carries the
   `ProjectStructure` and `ExampleModel` snippet markers, which `docs/index.md` transcludes;
   snippets are located by scanning sources for the marker, so they may move file but must
   stay wrapped around the same types.
2. `ApiDiagnostic.fs` — run diagnostics. Deliberately separate from `ApiModel.fs`: a
   diagnostic describes one extraction run, an artifact describes the documented snapshot.
   Keeping them apart is what stops diagnostics drifting back into `PackageModel` and forcing
   an `ApiModelSchemaVersion` bump.
3. `SemanticModel.fs` — semantic tokens through `SemanticDocumentationArtifact`.
4. `HistoryModel.fs` — `HistoryEntry`, `HistoryManifest`.
5. `RunnerModel.fs` — `ExampleStatus`, `ExampleSnapshotModel`, `ProjectSnapshotModel`.
   `ExampleStatus` currently sits among the API types but is only used by the runner.
6. `SiteModel.fs` — `NavigationItem`, `SiteConfig`, `ContentMetadata`, `ContentPage`,
   `ResolvedProject`.
7. `Serialization.fs` — the three converters and `module Serialization`. **Last, not first.**
   `FSharpUnionConverter` falls back to `SemanticTokenKind.PlainText` for an unknown union
   case, so serialization depends on the semantic model rather than being free of models.

Ordering constraints inside the models: `SiteConfig` references `NavigationItem`, so those
stay together, and serialization must follow `SemanticModel.fs`. Nothing else cross-references.

### Sequence

Planned as one commit per file. **Done as a single commit instead**: the split is atomic — the
files only make sense once `Models.fs` is gone — and the verification below is stronger than
per-commit builds, comparing the exact multiset of lines before and after.

### Verification

- `dotnet build FsLiveDocs.sln` and the full suite after each commit.
- Each commit's diff should be pure movement. Confirm with
  `git show --stat` (line counts should net to roughly zero) and by diffing the sorted type
  list before and after.
- No `.fs` file outside `FsLiveDocs.Core` should appear in any of these commits. If one does,
  a type was in the wrong file — that is the signal to stop and re-partition.

### Note

`ExampleSnapshotModel` and `ProjectSnapshotModel` never appear by name outside `Models.fs`.
They are **not** dead: `DocTestRunner` builds them with record syntax, so the type name is
inferred. Do not delete them on a name search alone.

---

## Part B — retire the legacy verification path

### What is actually legacy

The review said "overlapping legacy and newer execution paths". Precisely:

- `DocTestRunner.verifyExamples` — **legacy**. One caller: the `Test` command
  (`Program.fs:853`), whose own help text reads *"Run the legacy direct docstring verifier"*.
- `DocTestRunner.collectSnapshotByName` and `snapshotExampleNames` — **current**. Called by
  the generated snapshot test project.
- `DocTestRunner.collectSnapshots` — **current**, but only exercised by a unit test.

So `DocTestRunner` is half-migrated. It cannot simply be deleted, and the file name is
misleading about which half is which.

### The decision that comes first

`Test` is not pure duplication. It verifies examples **without generating a test project** —
one command, no generated sources to commit, no second build. The current path
(`generate-tests` → build → `dotnet test`) cannot do that.

So the question is not "delete or keep" but:

- **(a) Delete `Test`.** Smallest code, loses the no-generated-project workflow. Only correct
  if nobody depends on that workflow.
- **(b) Keep the command, re-base it on the current pipeline.** Recommended.
- **(c) Keep as is, drop the word "legacy".** Cheapest, leaves two execution paths forever.

**Recommendation: (b).** It removes the second execution path while keeping the capability
that justifies the command's existence.

### Steps for (b)

1. **Characterize first.** Capture `livedocs test` output for this repo and for Axial, and
   commit those as golden files. Everything after this is judged against them — without this
   step there is no way to tell a fix from a regression.
2. **Add an in-process runner** in `FsLiveDocs.Runner` that drives the same units the
   generated cases drive — `GeneratedVerification.verifyCompilationUnit` and `executeBlock` —
   over the pages from `documentationPages`, returning results rather than raising.
3. **Route XML examples through the existing snapshot path**, comparing against
   `ExpectedOutput` in memory instead of Verify files. `statusOf` already computes exactly
   this comparison; reuse it rather than writing a second one.
4. **Point `Test` at the new runner.** Keep the output shape and exit codes from step 1.
5. **Delete `verifyExamples`** and anything left unreferenced with it.
6. **Update the help text** — drop "legacy", state what the command does that
   `generate-tests` does not.

### Risks

- The two paths may genuinely disagree today. **They did, worse than expected**: `test` exited
  1 on this repository because `verifyExamples` was called with no references, so any example
  touching another project failed for want of a reference. The generated case for the same
  example passed. Retiring the path removed a false failure, not just a duplicate.
- `Test` gained execution of markdown `run`/`transcript` blocks, which only the generated cases
  ran before. That is a deliberate widening: without it the command is a subset of
  `generate-tests` rather than an alternative to it.
- `Test` currently calls `auditAction` first and folds the result into its exit code. Preserve
  that, or the change silently narrows what a green `test` run means.

### Sequencing against Part A

Independent. Part A touches only `FsLiveDocs.Core` file layout; Part B touches
`FsLiveDocs.Runner` and the CLI. Do Part A first anyway — it is mechanical, and a smaller
`Models.fs` makes the Part B diffs easier to read.

---

## Part C — verify XML examples by default

### The inconsistency

The same content — F# shown to a reader — has opposite defaults depending on where it lives.

| | Default | Escape hatch |
|---|---|---|
| Markdown fence | compiled (`Page`) | `no-check` **with a required reason** |
| XML `<example>` | nothing | n/a — verification is opt-in |

A markdown block is compiled unless excluded in writing (`DocumentationDiscovery.fs:113`, and
the reason is enforced twice, at `:121` and `:160`). An XML example is verified only if it
carries an FSI transcript or `data-livedocs="snapshot"`.

Demonstrated: an example whose body is `let broken : int = "this does not compile"`, neither
marked nor transcluded, passes `livedocs audit` clean and produces no generated case. It is
rendered to readers unverified, and nothing reports it.

### The rule to adopt

The markdown model, applied to XML examples:

- **compile every example by default** — the claim "documented code compiles" must not
  silently exclude whatever was not annotated;
- **execute only on request** — execution has side effects and needs an expected output to
  compare against, which is why markdown already requires explicit `run`/`transcript`;
- **exclude only with a written reason**, enforced like `no-check`.

### Steps

1. **Report before enforcing.** Emit an `ApiDiagnostic` (`unverified-example`) for every XML
   example that is neither compiled nor executed. Warning by default, so no existing project
   breaks; `--warn-as-error` lets a project opt in immediately. This reuses the channel and
   display built for `unnamed-parameter`.
2. **Give examples a compilation context.** A page block gets page scope plus the configured
   prelude; an XML example has only its declaring project. Compile each example as an isolated
   unit against that project, through the existing `DocumentationCompiler` path.
3. **Add the escape hatch**: `data-livedocs="no-check" reason="…"` on an example, with the
   same non-empty-reason enforcement markdown already applies. Without this, step 4 has no
   legitimate way to describe a fragment that cannot compile standalone.
4. ~~**Flip the default** so a compile failure is an error.~~ **Declined.** Implemented and
   reverted after seeing it run.

   Every finding stays a warning; failing the build is opt-in through `--warn-as-error`. The
   argument for flipping was that nothing had been released, so there was no migration to
   stage. What that argument missed is what the strict default does on contact with a real
   codebase: Axial went from a building site with 19 warnings to no site at all, because 13 of
   its examples are one-line illustrations that hit F# value restriction. Refusing to render
   any documentation over that is a worse failure than rendering it with warnings attached.

   The tool cannot tell an incomplete example from a deliberately partial one, so the
   repository owner decides. `--warn-as-error` is there for those who want the strict rule, and
   it costs them one flag.

### Risk

Step 4 was breaking by construction, which is why it was planned last and ultimately declined:
a default that stops a project publishing documentation is not a safe first experience, however
correct the underlying finding. Steps 1-3 are what make the strict rule *available*; choosing it
is the repository's call.

### Sequencing

After Part A (mechanical, makes diffs readable). Independent of Part B, but note that Part C
changes what verification covers, so if Part B's golden files are captured first they will
need retaking.
