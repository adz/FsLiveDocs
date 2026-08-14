# Release assistance implementation backlog

## Status

Accepted work required to satisfy the 1.0 rules in
[`RELEASE_ARTIFACT_RULES.md`](RELEASE_ARTIFACT_RULES.md). This document lists missing behavior; it
is not evidence that the current persisted interface is compatible.

## 1. Replace HTML in the API artifact

### Problem

`ApiModel.fs` persists `SummaryHtml`, `RemarksHtml`, and `DescriptionHtml`. Signatures, parameter
types, and return types also originate in FSharp.Formatting HTML. Historical releases therefore
freeze formatter markup and link shapes even though semantic code already follows a renderer-neutral
model.

### Required work

- Define an FsLiveDocs-owned documentation tree supporting at least text, paragraphs, inline code,
  code blocks, lists, symbol references, external links, and line breaks.
- Decide how summary, remarks, parameters, returns, exceptions, examples, and other XML-document
  sections are represented without duplicating their content.
- Convert XML documentation into that tree at the FSharp.Formatting boundary.
- Emit signatures and all type displays as plain semantic text.
- Preserve compiler symbol IDs for references instead of formatter-generated `/reference/*.html`
  links.
- Move symbol-reference URL creation and unresolved-reference presentation into the renderer.
- Render the documentation tree safely in API pages, cards, search synopses, and tooltip content.
- Remove HTML stripping and link-rewriting workarounds that become unnecessary.
- Bump the API artifact schema and implement only the pre-1.0 migration policy we deliberately
  choose; do not accidentally promise the current HTML schema as the stable 1.0 interface.
- Add serialization fixtures proving that persisted API JSON contains no HTML or formatter-owned
  data.

## 2. Freeze component schemas explicitly

- Give API, semantic, content, capsule-manifest, and history-index models independent schema
  versions.
- Replace permissive "older additive schema" loading with an explicit supported-version table.
- Add deterministic migrations from every retained version into current in-memory models.
- Reject zero, negative, unknown future, and unlisted historical versions with component-specific
  errors.
- Add canonical JSON fixtures and compatibility tests for each supported version.
- Document which changes require a schema bump and how long old readers/writers are supported.
- Validate required fields, stable IDs, duplicate symbols, tooltip indexes, hashes, and product
  version agreement after deserialization.

## 3. Add a canonical content artifact

### Problem

History currently combines API/semantic JSON with a separately materialized tagged `docs/` tree.
Snippet expansion can depend on source outside that tree and on future expansion behavior, so those
inputs are not yet a self-contained reproducible release.

### Required work

- Define a renderer-neutral content schema for canonical expanded Markdown pages, metadata,
  navigation inputs, assets, and authored-source provenance.
- Capture expansion after shortcode, snippet, example, and API-enrichment resolution through the
  same canonical discovery path used by verification.
- Preserve original block source and provenance where diagnostics and editing links need it.
- Decide whether API-enrichment pages are stored as canonical Markdown or structured content; do
  not persist generated HTML.
- Store asset bytes and media types with normalized safe paths and deterministic ordering.
- Hash the content as a component and validate semantic source/context hashes against it.
- Make history rendering consume the content artifact without checking out a historical repository.
- Retain an explicit compatibility path for old manifest entries that still provide `DocsPath`.

## 4. Package a complete release capsule

- Define a capsule manifest containing product version, source revision, capture-tool version,
  component paths, component schema versions, uncompressed sizes, and SHA-256 checksums.
- Select a deterministic archive format and compression supported on all target platforms.
- Make archive construction reproducible: stable paths, ordering, timestamps, and permissions.
- Verify every component before packaging and verify the archive after writing it.
- Refuse to overwrite an existing published product version unless an explicitly named local-only
  development option is used.
- Harden extraction against absolute paths, `..`, symlinks, duplicates, and decompression bombs.
- Print a useful size breakdown for API, semantic data, Markdown, and binary assets.
- Add an end-to-end fixture that captures a release, removes its project/build outputs, and renders
  the capsule with no compiler or restore step.

## 5. Add end-user capture assistance

- Introduce `livedocs capture <projects...> --version <version> --output <archive>` as the primary
  release command.
- Make it run canonical discovery, coverage audit, compilation verification, explicit executions,
  API extraction, semantic extraction, content capture, validation, packaging, and checksum output
  in that order.
- Keep uploads and remote release mutation out of `capture`; provide an explicit publishing command
  or generated CI workflow for those actions.
- Report failures using authored page/block IDs and provide a remediation, without leaving a capsule
  that looks publishable.
- Add `--dry-run`/inspection support that reports planned inputs and estimated size without writing a
  release.
- Add `livedocs inspect <capsule>` to display provenance, schemas, checksums, counts, and sizes.
- Update `livedocs init` to create the configuration, ignored cache/download directories, history
  index, and CI templates required by this workflow.

## 6. Add release storage and history assistance

- Recommend immutable GitHub Release assets as the default remote store while keeping storage
  providers replaceable.
- Generate a release workflow that captures once, publishes the capsule plus checksum, and refuses
  to publish after failed verification.
- Define a concise history index referencing capsule URL/provider identity and expected checksum,
  instead of requiring users to materialize every component path manually.
- Add `livedocs history add <version> ...` to discover a release asset, record its checksum, and keep
  entries deterministically ordered.
- Teach `build-history` to download, cache, checksum, unpack, migrate, and render capsules.
- Support offline builds from `.livedocs/releases/` and make that directory ignored by default.
- Avoid re-downloading verified capsules and provide actionable errors for missing, mutable, or
  mismatched assets.
- Preserve the current local-manifest workflow as an explicitly supported migration path until the
  capsule index replaces it.
- Document retention, signing/provenance options, and disaster recovery without making a particular
  hosting service part of the artifact schema.

## 7. Collapse generated verification entry points

### Scope

- `src/FsLiveDocs.Runner/GeneratedVerification.fs`
- `src/FsLiveDocs.Core/DocumentationDiscovery.fs`
- `src/FsLiveDocs.Cli/Program.fs`

### Problem

Generated tests currently know three actions and their ordering/composition:
`validateCoverage`, `verifyCompilationUnit`, and `executeBlock`. `Program.fs` reconstructs coverage,
compile, and execution cases itself, then emits a different call for each action. This leaks discovery
policy into generated source and allows callers to omit or reorder required work.

### Solution

Generate stable case values and run each through one interface.

### Required work

- Define a stable generated-case value carrying the case ID, selected project, source identity,
  canonical expanded content or content reference, and one owned action discriminator.
- Make `DocumentationDiscovery.verificationCases` the only place that composes coverage, compilation,
  and execution policy.
- Represent coverage as either construction-time validation or an explicit case produced by that
  function; generated callers must not add it independently.
- Preserve the rule that `Run` compiles in its page context before execution and that `Transcript`
  has its distinct expected-output semantics.
- Keep XML examples covered exactly once when an existing named Verify snapshot owns execution.
- Expose one Runner entry point, for example
  `GeneratedVerification.runCase : GeneratedVerificationCase -> Async<GeneratedVerificationResult>`.
- Return an owned result/failure model so generated xUnit code only supplies framework adaptation;
  do not make it understand compiler versus execution diagnostics.
- Change `Program.fs` to serialize/embed stable case values and emit one uniform test body per case.
- Remove `validateCoverage`, `verifyCompilationUnit`, and `executeBlock` after all callers migrate.
- Add tests proving deterministic case IDs/order, exact coverage, one execution per executable block,
  useful stale-generated-test errors, and identical behavior between audit, build, capture, and
  generated tests.

## 8. Make release creation observable and supportable

- Emit machine-readable capture reports alongside human terminal output.
- Include counts for symbols, documentation nodes, pages, blocks, tooltips, diagnostics, examples,
  and assets.
- Report compressed and uncompressed sizes so large embedded assets are obvious.
- Ensure logs never dump source, secrets, evaluated environment variables, or private package
  credentials unintentionally.
- Give every integrity, schema, and migration failure a component/version/path context.
- Add troubleshooting documentation for stale content, hash mismatches, missing release assets,
  unsupported schemas, and failed migrations.

## 9. Documentation and rollout

- Update the README, deep reference, semantic-code guide, command reference, and generated starter
  files to describe capsules rather than loose HTML-bearing API artifacts.
- Add a migration guide for repositories already using `extract` and local history manifests.
- Dogfood capture and capsule-only history rendering in FsLiveDocs itself.
- Capture a representative Axial release and record actual component and archive sizes. Current
  planning measurements are 82 Markdown files, about 317 KB of Markdown, 312 F# fences, about 2 MB
  for the docs tree, and a 1.30 MB semantic JSON cache that compresses to about 72 KB.
- Do not declare the 1.0 persisted interface frozen until the HTML-free API fixture, capsule-only
  end-to-end test, supported-version tests, and generated-verification single-interface tests pass.

## Suggested implementation slices

Keep each slice independently testable:

1. Documentation AST plus XML conversion and safe HTML rendering.
2. Renderer-neutral API model and schema fixtures.
3. Unified generated-verification case and runner interface.
4. Canonical content model and capture from the shared discovery result.
5. Capsule manifest, deterministic packaging, validation, and inspection.
6. Capsule-only history rendering with local files.
7. Download cache and history index.
8. Generated CI/release assistance and user migration documentation.

Do not combine the documentation AST, content capture, archive format, remote publishing, and history
download behavior into one schema or one implementation change.
