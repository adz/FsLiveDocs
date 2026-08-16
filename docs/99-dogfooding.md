---
title: Dogfood FsLiveDocs
---

# Dogfood FsLiveDocs

FsLiveDocs documents and releases itself with its own tool, using the same `init`, `audit`, `capture`, and `build-history` workflow described elsewhere in these docs. Nothing about the release process is special-cased for this repository.

Each release goes through the full pipeline before publishing: every documentation example across the repository's own projects is checked, a release capsule is captured and inspected, and the historical site is rebuilt from that capsule alone, with no compilation of the tagged source. That rebuild is what confirms a release capsule is genuinely self-contained: if it required recompiling the original project, `build-history` would fail.

This is the same guarantee described in [Capture and publish releases](guides/releases.md) — dogfooding is simply how that guarantee gets exercised before every FsLiveDocs release ships.
