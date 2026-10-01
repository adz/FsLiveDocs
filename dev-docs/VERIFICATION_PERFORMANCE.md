# Verification performance plan

## Measured release path

Axial 0.10.0's [release workflow](https://github.com/adz/Axial/actions/runs/36861050068) ran for about 87 minutes on 1 October 2026. The relevant measured steps were:

| Step | Duration | Work |
| --- | ---: | --- |
| Build | 4m 38s | Compile Axial and its tests. |
| `livedocs test` | 24m 21s | Audit and execute documentation examples. |
| `livedocs capture` | 18m 51s | Audit and execute the same examples before writing the capsule. |
| Release history check | 1m 02s | Render and validate the candidate in history. |
| Pages build | 4m 53s | Rebuild Axial after the capsule was committed. |
| Pages `livedocs test` | 24m 14s | Audit and execute source examples again. |
| Pages history render | 1m 22s | Render and verify the committed capsules. |
| Pages deploy | 1m 55s | Publish the site. |

The Pages step timings come from [the dispatched LiveDocs workflow](https://github.com/adz/Axial/actions/runs/36867117988). FsLiveDocs's own 0.11.2 release took about 6½ minutes; Axial's larger documentation set exposed the scaling problem.

## Where time goes in the code

`Program.fs` implements `test` by auditing, then executing snapshot cases and generated Markdown execution cases. `ReleaseCapture.verifyExplicitCases` executes them again during `capture`. `GeneratedVerification.runCase` compiles the owning unit before an executable `run` block, even when the command's audit already compiled it. Each Markdown execution case calls `FsiTranscriptRunner.runExample`, which starts a fresh `FsLiveDocs.TranscriptHost` process and FSI session. The case loop in `test` and the page/case loop in `capture` are serial. `build` repeats the compiler audit.

This gives one release three complete execution passes and many process/session startups. Full verification has work proportional to the number of examples; the goal is to remove repeated work and reduce wall time substantially while preserving each execution boundary.

## Changes in order

1. **Measure each phase and case.** Emit a machine-readable timing report for extraction, compiler audit, worker startup, FSI session creation, reference loading, example execution, capsule writing, and history rendering. Include counts and the slowest cases. This establishes a baseline and catches regressions.
2. **Use capture as the release verification pass.** Axial's release workflow can retain its conventions check, run `capture`, then check the candidate history. Capture already audits and executes explicit cases. Remove its separate `livedocs test` pass after confirming the commands select the same cases. The Pages dispatch should render and verify the committed capsules; it has no reason to re-execute source examples. Keep source verification on pull requests and ordinary main pushes. A history-only Pages dispatch can also omit the Axial solution build.
3. **Share work inside one verification command.** Pass the audit result and resolved project graph to execution cases. Reuse a successful owning-unit compilation within that command while keeping the generated xUnit `runCase` contract, which must compile before a selected execution case when run alone. Give `test` and `capture` one common verification implementation so case selection and failure policy cannot drift.
4. **Batch isolated examples by project graph.** Extend the worker protocol to accept several independent cases per worker. Preserve a fresh FSI session for each Markdown case, and preserve the existing shared session for a project's snapshot examples. Never batch different dependency graphs in one process. Chunk batches to bound memory and preserve per-case timeout and diagnostics.
5. **Run bounded independent batches concurrently.** Start with a small worker limit based on observed CPU and memory, and preserve output order by case ID. Document which examples may require serial execution because they share an external resource. Benchmark before raising the limit.
6. **Cache only inputs with a complete identity.** Compiler results can use the source, referenced assembly, tool, compiler, framework, and prelude fingerprints. Reuse executable results across invocations only for examples explicitly declared deterministic and isolated; otherwise run them in each fresh release verification pass.

## Correctness conditions

- The release verifies the same snapshot and Markdown execution cases before its capsule is published.
- A changed source block, referenced assembly, compiler/tool version, or prelude cannot reuse an earlier verification result.
- Independent Markdown cases retain fresh FSI sessions; project dependency graphs remain isolated from the tool and from one another.
- Failures identify the same case and retain its stdout/stderr and timeout details.
- Pages builds from immutable, checksum-verified capsules and verifies the rendered output links.
- Timing reports show each pass and its case count, so another duplicate pass is visible.

The workflow changes remove about 48 minutes from the measured Axial release path before worker optimization. The worker and compiler changes need a timed benchmark to establish their actual gain.

## Measured result

Items 1-5 were implemented and measured against Axial's documentation set — 125 pages, 612 F# blocks, 192 executed cases — on 2026-10-01 with a warm `.livedocs/cache`:

| Command | Baseline | After items 1-3 | After items 1-5 |
| --- | ---: | ---: | ---: |
| `livedocs test` | 24m 21s | 13m 40s | **3m 28s** |
| `livedocs capture` | 18m 51s | 13m 15s | **3m 33s** |

Items 1-3 removed the duplicate pass and the repeated work inside one command. Items 4-5 replaced the one-process-per-example execution with batches grouped by project graph, each example in a fresh FSI session, run by at most four concurrent workers. Example execution on the release path fell from 744.8s to 157.4s.

The release path used to run `test` then `capture`. It now runs `capture` alone: capture audits and executes examples once and writes the capsule from that result, so the measured release verification is 43m 12s to 3m 33s. The dispatch that re-ran `livedocs test` for the Pages job removes another 24m 14s. The `capture` output was inspected as a valid capsule with the same inventory the release expects.

The worker limit defaults to `min(ProcessorCount, 4)`; `FSLIVEDOCS_TRANSCRIPT_WORKERS` overrides it and `FSLIVEDOCS_TRANSCRIPT_BATCH` overrides the batch size (default 16). `--timings` records the FSI session time per Markdown case, so raising the limit can be judged against memory rather than guessed. Item 6 (cross-invocation caching) remains.
