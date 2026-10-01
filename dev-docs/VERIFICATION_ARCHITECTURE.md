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
- One worker request carries a session policy. Snapshot examples that shadow each other use `SharedSession`, one FSI
  session for the whole request. Independent Markdown `run` and `transcript` blocks use `FreshSessionPerExample`:
  one worker process evaluates several blocks, each in its own FSI session, so definitions cannot leak while the
  compiler and its JIT are loaded once. A request never mixes documented projects; `FsiTranscriptRunner.runIndependent`
  groups by project graph, chunks each group, and `FSLIVEDOCS_TRANSCRIPT_BATCH` (default 16) bounds a batch.
  Evaluation results are reassembled in case order, so concurrency does not reorder output. Batch size also bounds a
  worker timeout's blast radius, because `Process.timeout` kills the whole process.
- Batches run with at most `FSLIVEDOCS_TRANSCRIPT_WORKERS` concurrent worker processes (default
  `min(ProcessorCount, 4)`). Each worker loads its own FSharp.Compiler.Service and its own FSI sessions, so raise the
  limit only against observed memory. Examples that share a process-wide external resource — a fixed port, a
  machine-global lock, one database — must be written to tolerate concurrent runs, or set the limit to 1. FsLiveDocs
  does not detect such sharing.
- Each worker runs under `Process.timeout` (10 minutes, or `FSLIVEDOCS_TRANSCRIPT_TIMEOUT_SECONDS`), which terminates the
  process tree. Isolation is for dependency identity and cleanup; it is not a security sandbox, and examples remain
  trusted code.

## Reuse only declared-deterministic execution

A `run` or `transcript` block may declare `deterministic`: the author's assertion that the example reads
no clock, network, filesystem, environment, or process. A passing result for such a block is stored in
`.livedocs/cache/execution/`, keyed by tool, compiler, project inputs, resolved assembly, references,
prelude, block id, source hash, executed content, and expected output. A later invocation with the same
key skips execution and reports the case as cached. A changed input cannot reuse an entry, and failures
are never cached, so a fixed error re-runs and shows the current message.

FsLiveDocs does not verify the assertion. A wrong declaration can reuse a stale pass, which would let a
release publish without running the example. The declaration is the correctness boundary; when in
doubt, leave it off and let the example run on every pass.
