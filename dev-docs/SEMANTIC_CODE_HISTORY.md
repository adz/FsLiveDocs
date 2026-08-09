# Persisted Semantic Code for Historical Documentation

## Status

Accepted implementation plan. The current per-fence semantic formatter is a prototype to replace through the phases
below; it is not the target extraction architecture.

This document describes how FsLiveDocs can provide FsDocs-style type and documentation hovers without recompiling every historical release during each site build.

## Goal

Type-check F# documentation once during release extraction, persist renderer-neutral semantic data, and render that data with the latest FsLiveDocs UI whenever the current or historical documentation site is rebuilt.

The browser must receive precomputed semantic tokens and tooltip content. It must not run the F# compiler. Historical site builds must not rebuild historical projects.

Every rendered F# example must also be accounted for by generated documentation tests. Semantic rendering is an output
of successful verification, not a separate best-effort pass over Markdown.

## One discovery and verification pipeline

FsLiveDocs currently has two disconnected paths:

- `SymbolLister` discovers selected XML docstring examples and `DocTestRunner` executes them;
- `ContentProvider` discovers Markdown fences for rendering, but generated tests never see them.

Replace that split with one canonical discovery result used by tests, semantic extraction, current rendering, and
historical artifacts:

```text
Markdown, snippets, API enrichment, and XML examples
                         |
                         v
             expand and discover blocks
                         |
                         v
              assign compilation units
                         |
                         v
        generated tests compile or execute units
                         |
                         v
       successful compiler results become semantic data
                         |
                         v
             current and historical rendering
```

The implementation must never parse or expand documentation independently in the test and rendering paths. A block's
expanded source, identity, mode, and selected project must be identical everywhere.

## Separate the four relevant units

Do not use “example” to mean all stages of the pipeline. Model these units explicitly:

| Unit | Responsibility |
| --- | --- |
| `DocumentationBlock` | One authored or transcluded F# block with stable identity and display source. |
| `CompilationUnit` | One synthetic script checked with one evaluated project configuration. |
| `VerificationCase` | One generated test action: compile a unit or execute an explicitly runnable block. |
| `SemanticCodeBlock` | Renderer-neutral tokens, tooltips, and diagnostics mapped back to one displayed block. |

A page-scoped `CompilationUnit` normally contains several displayed blocks. An isolated block has its own compilation
unit. Generated tests discover compilation units and executable blocks, while coverage validation confirms that every
discovered F# block belongs to a verification case or carries an explicit exclusion.

An initial discovery model should include:

```fsharp
type DocumentationBlockOrigin =
    | MarkdownFence
    | SourceSnippet
    | XmlExample
    | ApiEnrichment

type DocumentationBlockMode =
    | Page
    | Prepare
    | Isolated
    | Run
    | Transcript
    | NoCheck of reason: string

type DocumentationBlock = {
    Id: string
    Origin: DocumentationBlockOrigin
    SourcePath: string
    Ordinal: int
    ExpandedSource: string
    SourceHash: string
    Mode: DocumentationBlockMode
    Project: string option
}

type CompilationUnit = {
    Id: string
    ProjectPath: string
    Prelude: string
    Blocks: DocumentationBlock list
}

type VerificationCase =
    | Compile of CompilationUnit
    | Execute of DocumentationBlock
    | ExecuteTranscript of DocumentationBlock
```

Keep runtime results and semantic compiler output outside the discovery records. Discovery must remain deterministic
and cheap enough for generated test enumeration.

## Compilation scope and execution policy

Compilation scope and execution policy are independent decisions.

- The default `fsharp` block participates in its page compilation unit and is compile-verified.
- `prepare` participates in the page compilation unit but is not displayed.
- `isolated` creates a separate compilation unit and promises that the example stands alone.
- `run` participates in compilation and is then explicitly executed.
- `transcript` is parsed and executed through the transcript runner.
- `no-check` is not an example; it is intentionally displayed pseudocode and must provide a reason.

Do not execute ordinary Markdown blocks automatically. Documentation may demonstrate HTTP, filesystem, process,
hosting, timing, or concurrency operations. Compile verification is the generated test for an ordinary block. Only
`run`, `transcript`, and the existing explicitly selected XML examples execute.

Require a reason for exclusions so coverage reports can distinguish deliberate pseudocode from unfinished examples:

````markdown
```fsharp no-check reason="Abbreviated match branches"
match error with
| Failure _ -> ...
```
````

Reject contradictory modes such as `prepare isolated`, `run no-check`, or `transcript prepare`.

## Generated test contract

`livedocs generate-tests` must generate tests over the canonical discovery result, not one broad test per project that
only scans XML comments. The generated project should expose deterministic case names such as:

```text
docs/the-flow-type/task-async-interop.md#page
docs/the-flow-type/task-async-interop.md#fsharp-3
src/Axial/Flow.fs#example-FromOption
```

The generated tests may use xUnit theory data or generated facts, but test discovery must report the failing page or
block directly. A page compile case can verify several blocks in one compiler invocation. Diagnostics must still map
to the documentation-relative block ID and line/column.

Verification coverage is a separate required assertion:

- every default, preparation, isolated, and run block belongs to exactly one compilation unit;
- every run or transcript block belongs to exactly one execution case;
- every no-check block has a non-empty reason;
- every XML example selected for execution belongs to exactly one execution case;
- duplicate stable IDs fail discovery.

The current Verify snapshots may remain as the approval mechanism for execution output. Compilation-only cases need no
snapshot when they succeed; compiler diagnostics are their failure output.

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

````markdown
```fsharp
let value = 42
```
````

The block participates in the shared page checking context and is rendered with semantic tokens.

### Preparation

````markdown
```fsharp prepare
open Example
let hiddenDependency = createDependency ()
```
````

The source participates in checking but is not rendered. Preparation source affects the page checking-context hash.

### Isolated

````markdown
```fsharp isolated
let value = 42
```
````

The block is checked in a separate synthetic script with the normal project references and standard page prelude. Use this when examples redeclare common names or should stand alone.

### Run

````markdown
```fsharp run
let result = calculate 42
printfn "%d" result
```
````

The block participates in its normal page or isolated compilation unit and is executed after compilation succeeds.
Use `run` only when runtime behavior is part of the example's contract. Ordinary blocks are compile-verified without
being executed.

### No-check

````markdown
```fsharp no-check reason="Intentionally incomplete syntax"
let intentionallyIncomplete =
```
````

The block does not enter semantic extraction and uses the syntax-only renderer. The reason is required and appears in
audit output.

### Transcript

````markdown
```fsharp transcript
> let value = 42;;
val value: int = 42
```
````

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

Use one documented frontmatter field for explicit selection, expressed as a documentation-root-relative or
repository-root-relative project path:

```yaml
---
project: src/Axial.Http/Axial.Http.fsproj
---
```

Discovery must resolve and validate this path once, then carry the canonical project path into every compilation,
test-generation, and semantic-extraction stage.

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

Current documentation must not silently switch to a different visual renderer when compilation fails:

- `livedocs test` and production `livedocs build` fail;
- `livedocs watch` may retain the last successful site and report mapped diagnostics in the terminal;
- a future watch UI may render an inline diagnostic, but it must not publish recovery types as trustworthy hovers;
- syntax-only fallback is reserved for explicit no-check/transcript modes and old history entries without a semantic
  artifact.

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

### Discovery and generated verification

- Markdown fences, expanded snippets, API enrichment, and XML examples enter one discovery result;
- every discovered F# block is covered once or excluded with a reason;
- page blocks produce one compile case and isolated blocks produce separate cases;
- generated test names contain stable documentation-relative IDs;
- compile-only cases never execute operational code;
- run and transcript cases execute once and preserve existing expected-output behavior;
- diagnostics from a page compilation unit map to the owning displayed block;
- contradictory modes and duplicate IDs fail before compiler invocation.

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

1. Add canonical block discovery after shortcode expansion, with stable IDs and source hashes.
2. Add authoring-mode parsing, required no-check reasons, and coverage validation.
3. Evaluate real compiler options through MSBuild and add page project selection.
4. Build page and isolated compilation units with reliable block source maps.
5. Expose compilation units and executable blocks as deterministic generated xUnit cases.
6. Fold XML examples and the existing transcript/scenario runner into the same verification-case model.
7. Make current test/build fail on uncovered blocks or unexpected compiler errors; keep watch on the last successful site.
8. Add FsLiveDocs-owned semantic artifact records, JSON serialization, and schema validation.
9. Convert successful FSharp.Formatting/FCS results into semantic records instead of generated HTML.
10. Extend `HistoryEntry` with optional semantic path and checksum fields.
11. Add release extraction, artifact loading, source/context hash checks, and historical rendering.
12. Replace the per-fence semantic prototype with rendering from verified semantic records.
13. Migrate FsLiveDocs' own docs, then run an Axial audit and migrate its 231 F# fences.
14. Add end-to-end current, generated-test, watch, release, and history fixtures.
15. Document the stable author-facing modes and remove transitional APIs.

Do not combine discovery, MSBuild evaluation, source mapping, generated tests, artifact persistence, and migration into
one change.

## Axial migration plan

Add `livedocs audit` before turning verification failures into hard build failures. Its output must classify every block
as page, isolated, executable, transcript, excluded, or failing, for example:

```text
PASS     docs/the-flow-type/task-async-interop.md#page
PASS     docs/http/requests.md#fsharp-2 (isolated)
EXCLUDED docs/http/responses-and-errors.md#fsharp-3: Abbreviated match branches
FAIL     docs/dependencies/layers.md#fsharp-4: IOrders is not defined
```

Migrate Axial with these rules:

1. Keep progressive guide blocks page-scoped. This covers later blocks that intentionally use earlier declarations.
2. Mark genuinely standalone examples `isolated`, especially when pages redeclare common names.
3. Add hidden `prepare` blocks for concise domain fixtures that are part of the page's teaching context.
4. Prefer transcluding real compiled source over duplicating large setup blocks.
5. Replace placeholder implementations with small compiling fixtures when that improves the example.
6. Mark irreducible pseudocode no-check with a concrete reason. Axial currently has a small minority of obvious
   placeholder fences compared with its full guide corpus.
7. Select platform-specific documentation projects in frontmatter before verifying browser, Node, or hosting pages.
8. Enable hard verification only after the audit has no unexplained failures or uncovered blocks.

## Implementation defaults

If implemented without human interaction, use these defaults:

- store semantic data in a separate versioned JSON artifact;
- persist only FsLiveDocs-owned renderer-neutral models;
- check page blocks together by default;
- support `prepare`, `isolated`, `run`, `no-check reason="..."`, and `transcript` modes;
- fail release extraction on unexpected semantic errors;
- allow syntax-only fallback only when an older manifest has no semantic artifact;
- fail on missing blocks or hash mismatches when a semantic artifact is declared;
- preserve original source text for copying;
- make browser tooltips accessible by hover and keyboard focus;
- do not rebuild historical projects during site rendering.

## Completion criteria

The feature is complete when:

- generated tests discover every rendered F# block from Markdown, transclusion, API enrichment, and XML comments;
- every block is compiled in a page or isolated unit, executed explicitly, or excluded with a reason;
- ordinary compile verification never executes operational examples;
- compiler diagnostics identify the authored page and block rather than only a synthetic script;
- a release extraction produces a checksum-protected semantic artifact;
- the artifact contains inferred local types and library XML documentation;
- a history site can render those hovers without loading or compiling the historical project;
- current renderer changes apply equally to current and historical semantic code;
- stale semantic data cannot be attached silently to changed source;
- old history artifacts continue to render with syntax highlighting;
- the full solution build and test suite pass.
