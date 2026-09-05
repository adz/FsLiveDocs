---
title: API and semantic extraction
---

# API and semantic extraction

Extraction records enough compiler-derived meaning to render current and historical documentation. The stored models belong to FsLiveDocs; compiler and formatter objects never cross the persistence boundary.

## API extraction

The API component contains:

- package and assembly provenance;
- entity and member IDs;
- entity hierarchy and kinds;
- plain signatures, parameter types, and return types;
- structured summaries, remarks, and parameter documentation;
- examples and source locations.

FsLiveDocs uses FSharp.Formatting to read built F# projects, then maps the result into its own API model.

Documentation nodes represent text, paragraphs, code, lists, symbol references, external links, line breaks, and canonical Markdown.

The API artifact contains no generated HTML, formatter objects, CSS classes, template markup, or page-layout URLs.

## Package provenance

Each extracted project contributes a package name and a set of entity IDs. Merging preserves that ownership while rebuilding one shared entity hierarchy.

Package ownership drives API grouping, package landing pages, badges, and documentation-set filtering.

Authored `docs/api/<entity-id>.md` content replaces the generated introduction for that entity. A package landing page uses its matching documented namespace introduction.

## Compiler context

FsLiveDocs evaluates real MSBuild properties, target frameworks, compiler options, and reference paths for each page-selected project.

A repository prelude and earlier page preparation contribute to the effective context. That context is part of semantic identity, not just a compiler convenience.

## Semantic extraction

Checked page and isolated units produce:

- original token text;
- FsLiveDocs-owned token classifications;
- block-local tooltip references;
- inferred signatures and documentation;
- mapped diagnostics;
- stable block IDs;
- source and context hashes.

The semantic artifact does not contain HTML, CSS classes, tooltip DOM IDs, or FSharp.Compiler.Service values.

## Source and context hashes

The source hash covers normalized displayed source and semantic mode.

The context hash covers preparation, repository prelude, project selection, and other inputs that can change the block's meaning without changing its displayed text.

A release that declares semantic data must match both hashes. Missing or mismatched blocks fail instead of silently falling back to syntax-only rendering.

Older releases created before semantic artifacts existed may use the documented syntax-only fallback.

## Diagnostics

Compiler diagnostics are stored in FsLiveDocs-owned records and mapped to documentation locations.

Unexpected compiler errors fail verification and capture. Warnings remain available for rendering and reporting; command policy may promote API-quality warnings with `--warn-as-error`.

## Cross-references

Canonical Markdown retains semantic `xref` targets. Structured API documentation retains symbol references through owned symbol IDs.

The renderer resolves those references against the stored API graph and constructs current URLs. Persisted artifacts do not depend on one site's route layout.
