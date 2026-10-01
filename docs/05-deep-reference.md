---
title: Reference
---

# Reference

The guides follow common workflows. These pages pin down the details behind those workflows: discovery rules, verification boundaries, persisted artifacts, history rendering, and failure policy.

Each reference opens with enough context to explain the boundary it describes. The rest is deliberately compact and exact.

## Repository and discovery

[Repository and discovery](05-reference/00-repository-and-discovery.md) covers repository layout, project selection, page discovery, front matter, transclusion, and stable block identity.

## Verification

[Verification](05-reference/01-verification.md) explains what each command checks, when code runs, how fence modes compose, and how generated tests map back to documentation.

## API and semantic extraction

[API and semantic extraction](05-reference/02-extraction.md) describes the generated symbol graph, compiler evaluation, diagnostics, semantic tokens, and context hashes.

## Release capsules

[Release capsules](05-reference/03-release-capsules.md) defines capture behavior, archive layout, manifests, schemas, reports, assets, and inspection.

## Release history

[Release history](05-reference/04-release-history.md) covers the history index, remote acquisition, cache behavior, historical rendering, output verification, and legacy manifests.

## Security and failures

[Security and failures](05-reference/05-security-and-failures.md) collects archive limits, trust boundaries, integrity checks, and conditions that stop a build or capture.

For command syntax and options, see the [command reference](04-cheat-sheet.md). For release-artifact design rules, see the repository's developer documentation.
