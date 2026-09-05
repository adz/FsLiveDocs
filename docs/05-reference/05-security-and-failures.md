---
title: Security and failures
---

# Security and failures

Capsules and authored documentation cross trust boundaries. FsLiveDocs validates structure and integrity before rendering, then treats stored text and links as untrusted input.

The strict failure policy protects historical meaning. A build stops rather than publishing incomplete semantics, unsafe paths, or a release assembled from mismatched parts.

## Archive boundaries

Capture and loading reject:

- duplicate entries;
- absolute paths;
- empty or current-directory paths;
- parent-directory traversal;
- directories and symbolic links;
- more than 10,000 entries;
- an entry larger than 64 MiB;
- an archive expanding beyond 256 MiB.

Asset materialization validates every destination before writing and verifies the declared size and checksum.

## Integrity checks

A capsule has an outer SHA-256 recorded by the history index. Its manifest records independent sizes and SHA-256 checksums for API, semantic, and content components.

Loading verifies the outer archive before caching it, then verifies every component and asset before rendering.

A checksum proves that bytes match the expected bytes. It does not identify who created them.

## Rendering boundaries

Renderers:

- encode plain documentation text;
- construct markup instead of trusting stored HTML;
- disable raw HTML in canonical Markdown nodes;
- allow only HTTP, HTTPS, and mail links in structured API documentation;
- resolve internal references through stored symbol IDs;
- create current URLs rather than loading persisted layout URLs.

Generated HTML, CSS classes, template fragments, DOM identifiers, and formatter objects are not persisted in release artifacts.

## Verification failures

Documentation verification stops on:

- an incomplete or missing project list;
- an invalid fence mode;
- an F# block without a verification mode;
- a `no-check` block without a reason;
- compiler errors;
- failed run blocks or transcripts;
- missing or duplicate scenarios;
- stale generated verification cases.

Diagnostics point back to authored Markdown or XML documentation where possible.

## Capture failures

Capture also stops on:

- missing Git provenance;
- incomplete semantic results;
- source or context hash mismatches;
- unresolved authored links;
- duplicate or unsafe archive paths;
- unsupported component schemas;
- mismatched product versions;
- checksum or size mismatches;
- an existing output path.

A failed capture publishes nothing.

## History failures

History commands stop on:

- malformed or unsupported indexes;
- duplicate semantic versions;
- a current version that is not the newest entry;
- entries with both or neither local and remote locations;
- non-HTTPS remote URLs;
- missing capsules;
- unsupported capsule schemas;
- checksum mismatches;
- generated local links that do not resolve.

Transient acquisition failures may be retried. Integrity, schema, and validation failures are deterministic and are never retried.

## Recovery

Fix unreleased source, documentation, configuration, or CI inputs and create a fresh capsule.

A published capsule is immutable. Corrections ship under a new product version rather than replacing the old artifact.
