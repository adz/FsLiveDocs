---
title: Reference
---

# Reference

The guides follow common workflows. These pages pin down the details behind those workflows: discovery rules, verification boundaries, persisted artifacts, history rendering, and failure policy.

Each reference opens with enough context to explain the boundary it describes. The rest is deliberately compact and exact.

## Repository and discovery

[Repository and discovery](reference/repository-and-discovery.md) covers repository layout, project selection, page discovery, front matter, transclusion, and stable block identity.

## Verification

[Verification](reference/verification.md) explains what each command checks, when code runs, how fence modes compose, and how generated tests map back to documentation.

## API and semantic extraction

[API and semantic extraction](reference/extraction.md) describes the generated symbol graph, compiler evaluation, diagnostics, semantic tokens, and context hashes.

## Release capsules

[Release capsules](reference/release-capsules.md) defines capture behavior, archive layout, manifests, schemas, reports, assets, and inspection.

## Release history

[Release history](reference/release-history.md) covers the history index, remote acquisition, cache behavior, historical rendering, output verification, and legacy manifests.

## Security and failures

[Security and failures](reference/security-and-failures.md) collects archive limits, trust boundaries, integrity checks, and conditions that stop a build or capture.

For command syntax and options, see the [command reference](cheat-sheet.md). For release-artifact design rules, see the repository's developer documentation.
