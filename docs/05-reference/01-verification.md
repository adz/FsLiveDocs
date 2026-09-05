---
title: Verification
---

# Verification

Verification has two boundaries: compilation proves that documentation still matches the library, while explicit execution proves runtime output or behavior. Commands choose how far across those boundaries to go.

## What each command verifies

| Command | Discovery and coverage | Compilation | Explicit execution | Rendering |
| --- | --- | --- | --- | --- |
| `audit` | Yes | Yes | No | No |
| `test` | Yes | Yes | Yes | No |
| `generate-tests` | Yes | Generated cases compile or run under `dotnet test` | Through generated cases | No |
| `build` | Yes | Yes | Yes | Yes |
| `watch` | Yes | Yes | Yes | Yes, after each rebuild |
| `capture` | Yes | Yes | Yes | No; stores renderer-neutral artifacts |

Discovery checks that every F# block has a valid mode. Compilation checks ordinary page units, preparation blocks, isolated blocks, run blocks, and transcripts before anything executes.

Only `run`, `transcript`, and snapshot XML examples execute. A `no-check` block is an explicit exclusion and must explain why.

## Page mode

An ordinary `fsharp` fence joins the page compilation unit. It can use declarations from earlier ordinary and `prepare` blocks on that page.

It compiles during every verification command and never executes by itself.

## Prepare mode

`fsharp prepare` adds declarations to the page context. Later page blocks can use them.

Preparation is compiled, included in the page-context hash, and shown as shared setup. It does not execute as a separate example.

## Isolated mode

`fsharp isolated` creates its own compilation unit with the selected project's references and repository prelude.

It proves that a snippet stands alone. It cannot use declarations from other blocks on the page.

## Run mode

`fsharp run` first compiles in the page context, then executes. Compilation still happens when a generated execution test is selected without its companion compile test.

Run blocks have normal process permissions. They can reach files, networks, clocks, environment variables, and child processes.

## Transcript mode

`fsharp transcript` separates FSI input from expected output, compiles the input, executes it through the transcript runner, and compares the result.

Whitespace and FSI formatting are part of the expected transcript contract.

## No-check mode

`fsharp no-check reason="..."` is rendered with syntax highlighting but receives no compiler verification.

The reason must be nonempty. Audit and capture report the block as an intentional exclusion rather than a verified example.

## XML examples

Public API XML documentation may contain named `<example>` elements. Discovery turns each example into a verification case.

Snapshot examples execute and compare output. Compile-only examples are checked without execution. Excluded examples require a reason.

A named scenario runs in a fresh FSI session before its example. The matching `DocScenarioAttribute` method must be public, static, parameterless, and unique across the selected projects.

## Generated tests

`livedocs generate-tests` writes stable xUnit facts from the same discovery result used by the other commands.

The generated project contains:

- one compile fact per page unit and isolated block;
- one execution fact per run block and transcript;
- one fact per XML example;
- Verify snapshots for examples whose output is a contract.

Each fact rediscovers its named case and fails if that case disappeared. New cases appear only after regeneration.

FsLiveDocs still owns action ordering, coverage checks, transcript behavior, and diagnostic mapping. The xUnit layer exposes those operations to normal test tooling.

## Failure mapping

Compiler diagnostics map back to the authored Markdown or XML example line rather than the generated compilation unit.

A verification run fails on invalid modes, uncovered blocks, unexpected compiler errors, failed execution, transcript differences, missing scenarios, duplicate scenario names, or stale generated cases.
