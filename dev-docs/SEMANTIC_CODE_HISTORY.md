# Persisted Semantic Code for Historical Documentation

## Status

Proposed implementation plan.

This document describes how FsLiveDocs can provide FsDocs-style type and documentation hovers without recompiling every historical release during each site build.

## Goal

Type-check F# documentation once during release extraction, persist renderer-neutral semantic data, and render that data with the latest FsLiveDocs UI whenever the current or historical documentation site is rebuilt.

The browser must receive precomputed semantic tokens and tooltip content. It must not run the F# compiler. Historical site builds must not rebuild historical projects.

## Rejected approaches

### Recompile every historical version

Rejected because build time grows with the number of retained versions. It also requires historical SDKs, dependencies, and build environments to remain reproducible indefinitely.

### Persist FSharp.Formatting HTML

Potentially viable as an intermediate implementation, but not the target design. Stored HTML would couple immutable release artifacts to FSharp.Formatting's markup and prevent the latest FsLiveDocs renderer from restyling or restructuring old code blocks cleanly.

### Resolve hovers from API JSON alone

Rejected because the API model cannot reproduce local inference, generic specialization, overload resolution, local declarations, expression types, or the semantic meaning of partially qualified identifiers.

## Architecture

The release pipeline performs semantic analysis:

```text
Documentation Markdown
  + expanded snippets and examples
  + project compiler options and references
                |
                v
  FSharp.Compiler.Service / FSharp.Formatting
                |
                v
  FsLiveDocs-owned semantic tokens and tooltips
                |
                v
  versioned semantic JSON artifact
```

Later site builds only render stored data:

```text
Tagged documentation source
  + API model artifact
  + semantic documentation artifact
                |
                v
       latest FsLiveDocs renderer
                |
                v
       static HTML, CSS, and JavaScript
```

The semantic artifact must contain no generated HTML and no FSharp.Formatting implementation types.

## Artifact model

Keep semantic documentation separate from `PackageModel`. API consumers should not need to load code-block data, and each schema should be independently versioned.

An initial domain model could be:

```fsharp
type SemanticTokenKind =
    | PlainText
    | Keyword
    | Identifier
    | TypeName
    | Function
    | Property
    | UnionCase
    | ActivePatternCase
    | Module
    | Namespace
    | Operator
    | Number
    | String
    | Comment
    | Punctuation
    | Preprocessor

type SemanticToken = {
    Text: string
    Kind: SemanticTokenKind
    Tooltip: int option
}

type SemanticLine = {
    Tokens: SemanticToken list
}

type SemanticTooltipSection = {
    Heading: string option
    Content: string
}

type SemanticTooltip = {
    Signature: string option
    Documentation: string option
    Sections: SemanticTooltipSection list
    Footer: string option
}

type SemanticDiagnosticSeverity =
    | Warning
    | Error

type SemanticDiagnostic = {
    Severity: SemanticDiagnosticSeverity
    Message: string
    StartLine: int
    StartColumn: int
    EndLine: int
    EndColumn: int
}

type SemanticCodeBlock = {
    Id: string
    SourceHash: string
    Lines: SemanticLine list
    Tooltips: SemanticTooltip list
    Diagnostics: SemanticDiagnostic list
}

type SemanticPage = {
    SourcePath: string
    Blocks: SemanticCodeBlock list
}

type SemanticDocumentationArtifact = {
    SchemaVersion: int
    Pages: SemanticPage list
}
```

The exact token-kind set should be derived from the information actually returned by FSharp.Formatting. Unknown future classifications should map to `PlainText` rather than breaking deserialization.

Tooltip references are block-local integer indexes. This keeps JSON compact and avoids persisting renderer-specific DOM identifiers.

## History manifest

Extend each history entry with an optional semantic artifact and checksum:

```fsharp
type HistoryEntry = {
    Version: string
    ModelPath: string
    ModelSha256: string
    SemanticPath: string option
    SemanticSha256: string option
    DocsPath: string
}
```

Expected release files:

```text
.livedocs/history/0.8.0.api.json
.livedocs/history/0.8.0.semantic.json
```

Both path and checksum must either be present together or absent together. Loading must validate the semantic schema version and checksum before rendering any page.

The semantic fields are optional so artifacts created before this feature remain readable.

## Code-block identity

Semantic data must be matched to the same expanded source used during extraction. Use three values:

1. Normalized documentation path relative to the documentation root.
2. Semantic block ordinal within that page.
3. SHA-256 hash of the normalized, expanded block source and its semantic mode.

A block ID can be derived from the path and ordinal:

```text
guides/getting-started.md#fsharp-2
```

The source hash prevents a modified block from silently receiving stale tokens or tooltips.

Normalize line endings to `\n` before hashing. Do not trim meaningful indentation. Include the mode and any hidden preparation source affecting the checking context in the page-level context hash. A stronger model may store both a block source hash and a page checking-context hash.

## Shortcode expansion

Semantic extraction must run after resolving:

- `{{< snippet ... >}}`;
- `{{< example ... >}}`;
- any future shortcode that emits F# source.

The renderer must perform the same deterministic expansion before looking up block identities and validating hashes.

Shortcode resolution must therefore be shared between release extraction and site rendering. Do not create separate implementations that can drift.

## Page-level checking

By default, semantic F# blocks on a page form one synthetic F# script. This allows an earlier `open`, type, function, or value declaration to affect later examples.

Construct the script with explicit source boundaries:

```fsharp
// generated page prelude

// <livedocs-block id="guides/start.md#fsharp-0">
open Example
// </livedocs-block>

// <livedocs-block id="guides/start.md#fsharp-1">
let result = calculate 42
// </livedocs-block>
```

Record the synthetic start line of every block before calling the compiler. Convert returned ranges and diagnostics back into block-relative positions afterward.

Source mapping is the highest-risk implementation area. Tests must cover blank lines, multiline strings, comments, Unicode identifiers, Windows line endings, and hidden preparation blocks.

## Authoring modes

Support these fenced-code modes:

### Default

```text
```fsharp
let value = 42
```
```

The block participates in the shared page checking context and is rendered with semantic tokens.

### Preparation

```text
```fsharp prepare
open Example
let hiddenDependency = createDependency ()
```
```

The source participates in checking but is not rendered. Preparation source affects the page checking-context hash.

### Isolated

```text
```fsharp isolated
let value = 42
```
```

The block is checked in a separate synthetic script with the normal project references and standard page prelude. Use this when examples redeclare common names or should stand alone.

### No-check

```text
```fsharp no-check
let intentionallyIncomplete =
```
```

The block does not enter semantic extraction and uses the normal syntax highlighter.

### Transcript

```text
```fsharp transcript
> let value = 42;;
val value: int = 42
```
```

The block is rendered as an FSI transcript. Prompt handling and transcript verification remain separate from semantic source formatting.

Fence-option parsing should tolerate either whitespace-separated options or one documented canonical syntax. Reject contradictory modes such as `prepare isolated`.

## Compiler inputs

Extraction must use the documented project's real compiler configuration, including:

- target framework;
- referenced assemblies;
- project references;
- package references;
- conditional compilation symbols;
- language version;
- warning configuration where relevant;
- source namespace and implicit SDK references.

Prefer obtaining options through MSBuild/project evaluation rather than constructing only `-r` flags from discovered DLLs. Simple assembly reference injection will miss conditional compilation and other project semantics.

For multiple documented projects, establish which project context checks each page. Possible policies are:

- an explicit page frontmatter project;
- a configured default documentation project;
- a generated aggregate script context containing all documented assembly references.

Start with the aggregate reference context if it is unambiguous. Add explicit page selection before supporting projects with conflicting dependency versions.

## FSharp.Formatting boundary

Use FSharp.Formatting to obtain annotated snippets and formatted tooltip spans where practical. Convert those values immediately into FsLiveDocs-owned records.

Do not serialize:

- `Snippet`;
- `TokenSpan`;
- `ToolTipSpans`;
- FCS compiler objects;
- FSharp.Formatting-generated HTML;
- CSS class names or tooltip DOM IDs.

This boundary permits replacing FSharp.Formatting or changing renderer markup without invalidating historical artifacts.

If FSharp.Formatting loses source ranges required for reliable block mapping, use FCS semantic classifications and tooltip queries directly while retaining the same persisted model.

## Diagnostics and failure policy

Release extraction must fail when a semantic block contains an unexpected compiler error. A release must not publish an artifact that appears semantic but lacks trustworthy information.

Warnings should be recorded in the artifact and reported by the CLI. Whether warnings fail extraction can remain configurable.

Rules:

- `no-check` and `transcript` blocks are intentionally excluded.
- `prepare`, default, and `isolated` blocks fail on compiler errors.
- every persisted diagnostic uses documentation-relative block coordinates;
- diagnostics identify the Markdown path and block ID;
- duplicate block IDs fail extraction;
- an unresolved documented assembly or missing compiler configuration fails extraction.

## Rendering and lookup policy

During rendering:

1. Expand shortcodes deterministically.
2. Identify semantic blocks and calculate their hashes.
3. Locate the page and block in the semantic artifact.
4. Verify both source and checking-context hashes.
5. Render tokens and tooltip content with the current renderer.

Behavior by artifact state:

- Old history entry with no semantic artifact: render F# with Prism or the existing syntax-only formatter.
- Semantic artifact present and matching: render semantic tokens and tooltips.
- Semantic artifact present but page, block, or hash missing: fail the build.
- `no-check` or transcript block: use its designated non-semantic renderer.

Do not silently fall back after a mismatch in a release that claims to contain semantic data. A mismatch indicates inconsistent tagged documentation, expansion behavior, or artifact contents.

## Browser behavior

Semantic rendering should emit token elements with a tooltip reference. Tooltip payloads should appear once per block or page.

The browser interaction must support:

- pointer hover;
- keyboard focus;
- `aria-describedby` relationships;
- viewport-aware placement;
- light and dark themes;
- long signatures and documentation with bounded scrolling;
- touch interaction where practical;
- no JavaScript requirement for reading the code itself.

Use the HTML popover API where supported, with a small fallback if the supported browser policy requires it. The persisted artifact must remain independent of this choice.

## Schema evolution

Give the semantic artifact its own schema version. Increment it for incompatible representation changes.

The loader should support explicitly known versions and reject unknown future versions with a clear message. Avoid reflection-based generic migrations; write small deterministic migrations when required.

Old history entries without semantic artifacts remain supported through syntax-only rendering. Once a semantic artifact exists for a release, it is immutable and checksum-verified.

## Tests

### Model and serialization

- semantic artifacts round-trip through JSON;
- tooltip indexes remain valid;
- checksums are verified;
- unknown schema versions are rejected;
- manifest path/checksum option pairs are validated;
- old manifests without semantic fields remain readable.

### Extraction

- local inferred values receive signatures;
- library identifiers receive signatures and XML documentation;
- generic functions show specialized or compiler-reported information correctly;
- overloads resolve to the selected member;
- declarations from earlier blocks are visible later;
- preparation blocks affect checking but are hidden;
- isolated blocks do not conflict with other declarations;
- no-check and transcript blocks are excluded;
- transcluded snippets and examples are analyzed after expansion;
- project references and package references resolve;
- compiler diagnostics map back to Markdown coordinates;
- Unicode and multiline source mappings remain correct.

### Artifact matching

- matching source hashes render semantic code;
- modified source fails when a semantic artifact exists;
- changed preparation context invalidates dependent blocks;
- missing blocks fail;
- old artifacts use the syntax-only fallback.

### Rendering

- all token text is HTML-encoded;
- tooltip documentation is sanitized or constructed without raw unsafe HTML;
- tooltip IDs are unique within a page;
- pointer and focus triggers target the correct tooltip;
- large tooltips remain within the viewport;
- copy operations return the original source rather than rendered token text artifacts.

### End-to-end history

Create a small fixture release containing semantic documentation, store its API and semantic artifacts, and build it through the history pipeline without compiling the fixture project. Assert that the resulting historical page contains semantic tokens and documentation tooltips.

## Implementation sequence

Keep commits independently testable where possible.

1. Add semantic artifact records, JSON serialization, and schema validation.
2. Extend `HistoryEntry` with optional semantic path and checksum fields.
3. Add stable expanded-block discovery and hashing.
4. Add authoring-mode parsing and validation.
5. Build page-level synthetic scripts and source maps.
6. Convert FSharp.Formatting/FCS results into FsLiveDocs semantic records.
7. Add release extraction output and checksum generation.
8. Add artifact loading and block/hash validation to history builds.
9. Render semantic tokens and structured tooltips without stored HTML.
10. Add hover, focus, placement, copy, and accessibility behavior.
11. Add migration, extraction, rendering, and end-to-end history tests.
12. Document the author-facing fence modes after their behavior is stable.

## Autonomous implementation estimate

For GPT-5.6-sol working without human interaction in the current repository:

- functional end-to-end implementation: 2–4 hours;
- production-quality implementation with migration and broad tests: 4–7 hours;
- safe ceiling if direct FCS range mapping is required: 8–10 hours.

Allocate six uninterrupted hours as the expected autonomous execution budget and ten hours as the safe ceiling.

The primary uncertainty is whether FSharp.Formatting exposes reliable snippet boundaries and structured tooltip data without losing the source ranges needed for documentation-block mapping. If it does, the work should remain near the expected budget. If not, direct FCS classification and tooltip queries will be required.

## Autonomous decision defaults

If implemented without human interaction, use these defaults:

- store semantic data in a separate versioned JSON artifact;
- persist only FsLiveDocs-owned renderer-neutral models;
- check page blocks together by default;
- support `prepare`, `isolated`, `no-check`, and `transcript` modes;
- fail release extraction on unexpected semantic errors;
- allow syntax-only fallback only when an older manifest has no semantic artifact;
- fail on missing blocks or hash mismatches when a semantic artifact is declared;
- preserve original source text for copying;
- make browser tooltips accessible by hover and keyboard focus;
- do not rebuild historical projects during site rendering.

## Completion criteria

The feature is complete when:

- a release extraction produces a checksum-protected semantic artifact;
- the artifact contains inferred local types and library XML documentation;
- a history site can render those hovers without loading or compiling the historical project;
- current renderer changes apply equally to current and historical semantic code;
- stale semantic data cannot be attached silently to changed source;
- old history artifacts continue to render with syntax highlighting;
- the full solution build and test suite pass.
