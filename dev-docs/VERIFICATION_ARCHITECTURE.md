# Verification architecture

FsLiveDocs uses one deterministic discovery result for audit, generated tests, semantic extraction, capture, and rendering.

## Separate authored blocks from actions

A `DocumentationBlock` records stable identity, origin, source, mode, project, and source hash.

A `CompilationUnit` groups blocks checked in one project context.

A verification case represents one compile or explicit execution action.

## Generate stable cases

`DocumentationDiscovery.generatedCases` owns case composition and ordering.

Generated tests embed stable case values and call `GeneratedVerification.runCase`. They do not call separate coverage, compilation, or execution entry points.

## Preserve execution policy

Ordinary, `prepare`, and `isolated` blocks compile exactly once. `run` blocks compile in their owning context before execution.

Transcript blocks use transcript semantics. `no-check` blocks require a reason and produce no verification action.

XML snapshot examples remain owned by their named Verify cases and do not execute a second time through page generation.

## Detect stale generated tests

The runner reconstructs canonical cases before running an embedded case. If its ID or action no longer exists, the test tells you to regenerate the project.

## Execute examples in an isolated worker

Transcripts, `run` blocks, and snapshot examples execute in `FsLiveDocs.TranscriptHost`, a separate process, never in
`livedocs` or a generated test process. FsLiveDocs depends on libraries (Axial among them) that a documented project may
use at another version, and one process binds one copy of an assembly identity: the tool's. The worker loads only the
documented project's graph.

- The worker references only FSharp.Core and FSharp.Compiler.Service. Never add FsLiveDocs.Core, FsLiveDocs.Runner,
  Axial, or another shared library to it; a test asserts its `deps.json` stays free of them.
- `FsLiveDocs.Runner` references the worker for its protocol types, which also copies the worker, its `deps.json`, and
  its `runtimeconfig.json` beside the Runner in every consumer. Generated snapshot projects, which reference the Runner by
  `HintPath`, copy those three files explicitly. `TranscriptHostClient` resolves the worker beside the assembly that
  declares the protocol, or from `FSLIVEDOCS_TRANSCRIPT_HOST`.
- The worker never runs from where it ships. That folder also holds the tool's Axial, and the F# compiler resolves a
  referenced assembly's dependencies from the folder it was loaded from and from the working directory, so a documented
  `Axial.HttpClient.dll` referenced before its `Axial.dll` would type-check against the tool's copy. `TranscriptHostClient`
  copies the worker and the runtime and resource assets its `deps.json` lists into
  `<temp>/fslivedocs-transcript-host/<hash>/`, keyed by those files' paths, sizes, and timestamps, and runs that copy.
- Examples run against the build the audit compiles. `ProjectResolver.documentationBuildFor` selects it (the sole or
  first declared framework, the default configuration when built, otherwise Release) for both `DocumentationCompiler`
  and `ProjectResolver.resolveAssemblyPath`. The newest file under `bin` is only a fallback when MSBuild cannot
  evaluate the project.
- The request travels as JSON on the worker's stdin and the response on its stdout; both carry
  `Protocol.Version`, and a mismatch is rejected. Evaluated code's stdout is redirected to the worker's stderr so
  printing cannot corrupt the response. That output is not part of the compared transcript, as before the move.
- One request is one fresh FSI session. `runExamples` sends a project's snapshot examples in one request, preserving
  the shared-session shadowing they had in process.
- Each worker runs under `Process.timeout` (10 minutes, or `FSLIVEDOCS_TRANSCRIPT_TIMEOUT_SECONDS`), which terminates the
  process tree. Isolation is for dependency identity and cleanup; it is not a security sandbox, and examples remain
  trusted code.
