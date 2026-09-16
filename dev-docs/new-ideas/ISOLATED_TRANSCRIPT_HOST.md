# Isolate transcript execution in an Axial-free worker process

## Status

Proposal. This is not yet an implementation requirement.

## Problem

FsLiveDocs executes FSI transcripts in the `livedocs` process through `FsiEvaluationSession`. The tool itself depends on
Axial. When the documented project is Axial, or depends on a different Axial version, the process has already loaded the
tool's `Axial.dll` into the default `AssemblyLoadContext`.

Compilation can still use metadata from the documented project's assembly, but execution binds calls to the Axial
assembly already loaded by the tool. A transcript that uses an API absent from that version then fails at runtime even
though it compiled successfully. Observed failures include:

```text
MissingMethodException: Method not found: Axial.FlowStreamModule.chunkBySize(...)
TypeLoadException: Could not load type 'Axial.ParallelismModule' from assembly 'Axial, Version=0.9.1.0'
```

This is not specific to Axial. Any library used by FsLiveDocs can conflict with another version supplied by the
project whose documentation is being tested.

## Decision

Run transcript execution in a separate, Axial-free worker process.

The worker must not reference `FsLiveDocs.Core`, `FsLiveDocs.Runner`, Axial, or another library that can collide with a
documented project. It receives plain serialized input, creates the FSI session, loads only the requested project and
references, evaluates setup and transcript interactions, and returns plain serialized output.

Process isolation is the version boundary. The operating system process, rather than CLR accessibility or NuGet asset
metadata, guarantees that the documented project's dependency graph is not unified with the tool's graph.

## Why an internal package reference is insufficient

`PrivateAssets="all"` prevents a package from flowing transitively to consumers. Keeping Axial types out of public
signatures also makes Axial an implementation detail at the source API boundary. Neither measure stops `Axial.dll`
from loading into the `livedocs` process at runtime.

The default `AssemblyLoadContext` cannot provide two independently resolved assemblies with the same identity to the
same FSI execution environment. Assembly accessibility (`internal` versus `public`) does not affect this binding rule.

## Components

### `FsLiveDocs.TranscriptHost`

Add a small executable whose dependency set is limited to:

- the .NET runtime;
- FSharp.Core;
- FSharp.Compiler.Service;
- an Axial-free protocol assembly, if a separate assembly is useful.

The host must not depend on the rest of FsLiveDocs. In particular, sharing the current Core model would reintroduce
Axial transitively.

The host owns:

- creation and disposal of `FsiEvaluationSession`;
- project and reference loading;
- scenario setup invocation;
- interaction evaluation;
- stdout and stderr capture;
- stable formatting of bound values;
- timeout and cancellation enforcement;
- conversion of exceptions and diagnostics to protocol results.

### Parent runner

`FsLiveDocs.Runner` remains responsible for:

- documentation discovery;
- transcript parsing;
- selecting the documented project;
- constructing the worker request;
- starting and supervising the worker;
- comparing actual and expected output;
- mapping failures back to authored source locations;
- rendering the existing user-facing diagnostic.

The parent must treat abnormal worker termination as a transcript failure with the worker exit code and captured
stderr. It must not silently retry a transcript whose execution may have external effects.

## Protocol

Use newline-delimited JSON over standard input and standard output. Keep protocol messages separate from evaluated
program output so arbitrary transcript text cannot corrupt framing.

A request needs only renderer-neutral execution data:

```text
protocol version
project assembly path
reference assembly paths
extra namespace opens
optional scenario method identity
ordered FSI interactions
working directory
timeout
```

A response contains:

```text
protocol version
status
formatted output
compiler diagnostics
exception summary
worker stderr, when relevant
```

Paths are absolute before they cross the process boundary. The request must preserve interaction boundaries because a
transcript can contain several FSI submissions in one session.

Version the protocol from its first release. Reject an unsupported version explicitly rather than attempting a partial
interpretation.

## Execution lifecycle

For each transcript session:

1. The parent constructs one complete request.
2. The parent starts `FsLiveDocs.TranscriptHost` with redirected stdin, stdout, and stderr.
3. The worker reads and validates the request before loading the documented assembly.
4. The worker creates a fresh FSI session.
5. The worker loads the documented project's assembly and references.
6. If configured, the worker invokes scenario setup and excludes setup output from the expected transcript.
7. The worker evaluates interactions in order and formats bound values and captured output with existing transcript
   semantics.
8. The worker writes one response and exits.
9. The parent applies the existing expected-output comparison and source-mapped diagnostics.

A fresh process per transcript is the simplest correct baseline. If startup cost becomes material, add a bounded worker
pool only after measuring it. A pooled worker may execute several transcripts for the same exact dependency graph, but
it must discard the process before switching project identity or assembly paths. Correct dependency isolation takes
priority over startup throughput.

## Output compatibility

Moving execution must not silently rewrite transcript contracts. Extract the current value-name, type-name, decimal,
collection, stdout, and stderr formatting into code the host can use without depending on Axial.

Add characterization tests before changing the execution boundary for:

- unit, primitive, option, list, map, tuple, and generic values;
- named bindings and `it`;
- multiple interactions in one session;
- stdout and stderr ordering;
- compiler errors;
- runtime exceptions;
- scenario setup with suppressed setup output;
- whitespace normalization.

If process-level output capture changes ordering for work performed on background threads, define and test the intended
ordering rather than preserving an accidental in-process `StringWriter` limitation.

## Cancellation and cleanup

The parent owns the worker lifetime.

- On timeout or cancellation, terminate the complete worker process tree.
- Await process exit and drain redirected streams to avoid deadlocks.
- Delete request or script files in `finally` if the implementation uses temporary files.
- Do not leave an FSI process running after `livedocs test`, capture, or generated verification exits.
- Bound request size and captured output so a broken example cannot consume memory without limit.

The transcript still has the same filesystem, network, process, clock, and environment access as the user running
FsLiveDocs. Isolation solves dependency identity and process cleanup; it is not a security sandbox. User-facing
documentation must continue to state that executable examples are trusted code.

## Packaging

Bundle the worker beside the `livedocs` executable in the .NET tool package. Resolve it relative to the running tool,
not from `PATH`. Package verification must assert that the worker and its runtime files are present.

The worker must be runnable from:

- the installed global or local tool;
- `dotnet run --project src/FsLiveDocs.Cli` during development;
- generated verification tests;
- release capture and CI environments.

Do not copy a specific Axial assembly into the worker output.

## Validation

Add an integration fixture that reproduces the actual conflict:

1. The test host loads dependency version A through FsLiveDocs.
2. A documented fixture references an incompatible version B with a member absent from A.
3. A transcript calls the version-B-only member.
4. Audit compiles it and transcript execution succeeds against B.

Also verify:

- two documented projects can use incompatible versions in one `livedocs test` run;
- a missing project dependency produces a source-mapped transcript failure;
- timeout kills the worker and its children;
- malformed protocol input fails deterministically;
- existing transcript fixtures retain their output;
- package installation contains and launches the worker on supported platforms.

Before release, run FsLiveDocs against the current Axial repository and execute transcripts that use APIs newer than the
Axial version consumed internally by FsLiveDocs.

## Rejected alternatives

### Mark Axial package references private

Useful for package hygiene, but it does not change runtime assembly loading.

### Keep Axial out of public signatures

Desirable independently, but the implementation still loads Axial into the process.

### Rename or shade Axial

IL rewriting could give the tool's private copy another assembly identity. It would complicate packaging, debugging,
stack traces, trimming, and every Axial upgrade. It solves one known dependency rather than the general collision.

### Load only the documented assembly in a collectible `AssemblyLoadContext`

The FSI session and its dependency resolver would also need to live inside that context. Passing FCS or reflected
values across the boundary reintroduces type identity problems. A complete isolated FCS load can work, but it has more
resolver, native dependency, cache, and unload failure modes than a worker process.

### Reuse the in-process assembly when identities match

Assembly versions can remain stable across package releases, and matching identities do not prove API or file equality.
Path and content differences still matter. Silent reuse is exactly the behavior this design removes.

## Acceptance criteria

The work is complete when:

- transcript execution no longer occurs in the `livedocs` process;
- the worker has no Axial dependency, direct or transitive;
- a documented project can execute against an Axial version incompatible with FsLiveDocs' internal version;
- existing transcript formatting remains covered by characterization tests;
- cancellation terminates the worker process tree;
- installed-tool, generated-test, capture, and local-development paths all use the same isolated execution boundary;
- Axial's `FlowStream.chunkBySize` and `FlowStream.mapFlowPar` documentation transcripts pass while FsLiveDocs still
  consumes an older Axial package internally.
