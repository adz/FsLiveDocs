---
title: Link to API symbols
---

# Link to API symbols

Use `xref` identifiers to link guide text to the generated API reference.

## Link to an entity

```text
xref:T:Example.Orders.Order
```

## Link to a member

```text
xref:M:Example.Orders.create
```

Use the exact symbol ID reported by the extracted API model.

## Resolve links during rendering

Canonical release content preserves `xref` syntax. The renderer resolves it against the API graph stored in the same capsule.

This design keeps current URL layout out of immutable release artifacts.

## Fix an unresolved reference

Check these conditions:

1. The symbol is public and included in the captured API graph.
2. The identifier includes the correct `T:` or `M:` prefix.
3. The fully qualified ID matches the extracted symbol ID.
4. You passed the project that owns the symbol to FsLiveDocs.

FsLiveDocs fails the build when an authored `xref` does not resolve.
