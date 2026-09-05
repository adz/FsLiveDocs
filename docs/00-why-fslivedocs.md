---
title: Why FsLiveDocs
weight: 1
---

# Why FsLiveDocs

Documentation examples are easy to trust and easy to forget. A library changes, an old snippet still looks convincing, and the reader finds the breakage first.

FsLiveDocs treats examples as part of the codebase. It compiles them with the real project and can run the few examples whose output matters.

That turns a docs site into a friendly extra test suite. API changes fail near the explanation they broke, while readers still get normal guides and generated reference pages.

## One tool instead of a stack

FsLiveDocs started with a familiar mix: FSharp.Formatting for API reference, another static-site tool for guides, and scripts to connect releases and versions.

It worked, but every extra tool created another configuration, build step, and place for links or versions to drift.

FsLiveDocs keeps that workflow in one .NET tool:

- Markdown guides and enriched API pages;
- compile-checked, runnable, and transcript examples;
- source transclusion and API-aware links;
- one sidebar, search index, theme, and preview server;
- immutable documentation captured with each release.

It still uses [FSharp.Formatting](https://fsprojects.github.io/FSharp.Formatting/) for F# API extraction. If you only need a current API reference, fsdocs may already be the smaller answer.

## History without rebuilding old code

A release capsule stores API meaning, checked documentation, semantic code data, and assets. It does not freeze generated HTML.

A newer FsLiveDocs can render that capsule later without restoring the old SDK or compiling the old source. Styling can improve while the released meaning stays fixed.

That is the larger idea: docs should stay honest while a project changes, and old docs should remain buildable after its toolchain moves on.

Ready to try it? [Set up your repository](introduction.md).
