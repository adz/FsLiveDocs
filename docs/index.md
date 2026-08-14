---
title: FsLiveDocs
weight: 1
---

# Build documentation that stays true

FsLiveDocs generates API references, verifies F# examples, and preserves release documentation as renderer-neutral artifacts.

## Verify authored code

Use ordinary `fsharp` fences for compile verification. Mark a block `run` or `transcript` only when execution is part of the example contract.

FsLiveDocs uses the documented project's MSBuild and compiler settings. Diagnostics point to the authored page and block.

## Add semantic code tooltips

Release capture stores FsLiveDocs-owned tokens, inferred signatures, tooltip documentation, and source hashes.

The browser receives rendered code and accessible tooltips. It does not run the F# compiler.

## Preserve releases without preserving HTML

A release capsule contains:

- a structured public API model;
- canonical Markdown and documentation assets;
- compiler-derived semantic data;
- checksums and source provenance.

A current FsLiveDocs renderer can rebuild any supported capsule. Historical builds do not restore or compile old projects.

## Start here

1. [Install and run FsLiveDocs](introduction.md).
2. [Author verified examples](guides/verified-examples.md).
3. [Capture a release](guides/releases.md).
4. [Use the complete reference](deep-reference.md).
