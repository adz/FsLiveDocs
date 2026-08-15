---
title: Link to API symbols
---

# Link to API symbols

FsLiveDocs links an inline code span when its text identifies exactly one entity or member in the generated API reference:

```markdown
Create an `Order` with `Orders.create`.
```

Entity display names, entity IDs, qualified member names, and member IDs are eligible. Unmatched or ambiguous spans remain inline code. In particular, an unqualified member such as `create` is not linked because another documented entity may expose a member with the same name.

## Disambiguate a reference

Use a Markdown link with an `xref` destination when a short name is ambiguous or the link needs custom text:

```markdown
Use [`Order`](xref:T:Example.Orders.Order).
Call [`the order factory`](xref:M:Example.Orders.create).
```

Use the exact symbol ID reported by the extracted API model. An explicit reference that does not resolve fails the build.

The bare forms `xref:T:Example.Orders.Order` and `xref:M:Example.Orders.create` are also supported. FsLiveDocs supplies their link text from the API model.

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
