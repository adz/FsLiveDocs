# Refactor plan: model split and legacy verification retirement

Two pieces of structural work, independent of each other. The first is mechanical and safe;
the second needs a behavioural decision before any code moves.

A third item from the same review — audit, build and generated tests each reconstructing
documentation projects — is already done (`65fc2d3`). All three now share
`Program.documentationPages`.

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

1. `Serialization.fs` — the three converters and `module Serialization`. First, because it
   depends on no model type. Consumers: `Program.fs` (11 uses), `Tests.fs` (6),
   `SiteBuilder.fs`, `History.fs`, `ContentProvider.fs`.
2. `ApiModel.fs` — release artifacts, plus the `ExampleModel` augmentation.
3. `ApiDiagnostic.fs` — run diagnostics. Deliberately separate from `ApiModel.fs`: a
   diagnostic describes one extraction run, an artifact describes the documented snapshot.
   Keeping them apart is what stops diagnostics drifting back into `PackageModel` and forcing
   an `ApiModelSchemaVersion` bump.
4. `SemanticModel.fs` — semantic tokens through `SemanticDocumentationArtifact`.
5. `HistoryModel.fs` — `HistoryEntry`, `HistoryManifest`. Consider folding these into the
   existing `History.fs`, which is their only consumer; keep separate only if that file's
   validation logic would obscure them.
6. `RunnerModel.fs` — `ExampleStatus`, `ExampleSnapshotModel`, `ProjectSnapshotModel`.
7. `SiteModel.fs` — `NavigationItem`, `SiteConfig`, `ContentMetadata`, `ContentPage`,
   `ResolvedProject`.

Only ordering constraint inside the models: `SiteConfig` references `NavigationItem`, so they
stay in one file. Nothing else cross-references.

### Sequence

One commit per file, each building green, in the order above. Moving `Serialization.fs` first
proves the approach on the piece with the most consumers and the fewest dependencies.

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

- The two paths may genuinely disagree today: `verifyExamples` runs only
  `IsSnapshotTest` examples, while the generated cases also cover `coverage` and `compile`
  units. Step 1 will expose that. Decide deliberately whether `Test` gains that coverage —
  it probably should, but it is a behaviour change and belongs in its own commit.
- `Test` currently calls `auditAction` first and folds the result into its exit code. Preserve
  that, or the change silently narrows what a green `test` run means.

### Sequencing against Part A

Independent. Part A touches only `FsLiveDocs.Core` file layout; Part B touches
`FsLiveDocs.Runner` and the CLI. Do Part A first anyway — it is mechanical, and a smaller
`Models.fs` makes the Part B diffs easier to read.
