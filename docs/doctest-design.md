---
title: Verification architecture
---

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
