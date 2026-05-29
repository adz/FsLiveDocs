---
title: Cross References
weight: 3
type: how-to
---

# Cross References

FsLiveDocs supports semantic cross-references with the `xref:` syntax.
Use it when you want Markdown to link to a real API symbol instead of a hand-written URL.

## What `xref:` does

`xref:` is a shorthand that FsLiveDocs resolves during Markdown processing.
It finds the matching API member or API entity in the extracted symbol model, then rewrites the text into a link to the generated API reference page.

That means:

1. you write a compact symbol reference in Markdown,
2. FsLiveDocs finds the matching member or type in the compiled project output,
3. the final site contains a normal clickable link.

`xref:` is not a source-code link. It points to the generated API reference page.
On those API pages, the member header also includes a separate source icon that jumps to the line in the repository.

## Basic syntax

```md
xref:M:FsLiveDocs.Core.ContentProvider.resolveSnippets
xref:T:FsLiveDocs.Core.ExampleModel
```

The general shape is:

```md
xref:<kind>:<fully-qualified-id>
```

Where:

1. `<kind>` is the symbol kind prefix, such as `M` for member or `T` for type.
2. `<fully-qualified-id>` is the identifier FsLiveDocs knows about from the extracted API model.

## What you can reference

FsLiveDocs resolves `xref:` against the API model produced from compiled F# code.
In practice that covers:

1. modules,
2. namespaces,
3. types,
4. records,
5. unions,
6. classes and interfaces,
7. functions, values, methods, and other members.

Projects themselves are not xref targets. A project contributes compiled symbols to the model; you link to the namespaces, modules, types, and members that project exposes.

## How symbol kinds map

Use these as the default mental model:

| Symbol kind | What to link to | Example |
| :--- | :--- | :--- |
| Module | The module page or a member inside the module | `xref:T:FsLiveDocs.Core.ContentProvider` |
| Namespace | The namespace page | `xref:T:FsLiveDocs.Core` |
| Type | The type page | `xref:T:FsLiveDocs.Core.ExampleModel` |
| Record | The record type page | `xref:T:FsLiveDocs.Core.ContentMetadata` |
| Union | The union type page | `xref:T:FsLiveDocs.Core.PackageModel` |
| Function or member | The member anchor on its parent page | `xref:M:FsLiveDocs.Core.ContentProvider.resolveSnippets` |

The exact identifier must match the symbol ID FsLiveDocs extracted from the compiled assembly.

## Real resolver code

The implementation lives in the core Markdown resolver:

{{< snippet id="XrefResolution" >}}

This is the important part of the behavior:

1. the resolver checks members first,
2. then it checks entities,
3. member links point to the containing API page with an anchor,
4. entity links point to the entity page itself.

## Writing good xrefs

1. Prefer xrefs over raw Markdown links when you are pointing at an API symbol.
2. Use the fully qualified symbol ID so the link stays stable.
3. Link to the type, module, or namespace page when you are explaining the concept.
4. Link to the member anchor when you are discussing a specific function or method.

## Markdown examples

### Type link

```md
See `xref:T:FsLiveDocs.Core.ExampleModel` for the example data structure.
```

### Function link

```md
The runner uses `xref:M:FsLiveDocs.Runner.DocTestRunner.collectSnapshots` to prepare snapshot payloads for the generated Verify project produced by `livedocs generate-tests`, and `xref:M:FsLiveDocs.Runner.DocTestRunner.verifyExamples` remains as the legacy direct checker.
```

### Namespace link

```md
The `xref:T:FsLiveDocs.Core` namespace collects the core documentation model.
```

## Common mistakes

1. Do not use xref for normal prose links.
2. Do not point xrefs at source files. Use the API page source icon when you want to jump to code.
3. Do not assume the display text and the symbol ID are the same thing.
4. Do not expect a project name to resolve directly; link to the symbols inside the project.

## Source links vs xrefs

These are different tools:

1. `xref:` gives you a link to the generated API documentation.
2. The source icon on API pages gives you a link to the actual source line in the repository.

Use both, depending on what the reader needs.

## When to use xrefs

Use xrefs whenever the reader should move from a narrative guide into a specific API symbol.
They are especially useful in guides that explain:

1. how modules are structured,
2. how records and unions are shaped,
3. which function or method implements a behavior,
4. how one namespace relates to another.

If you need to link to a source file, use the source icon on the API page instead of `xref:`.
