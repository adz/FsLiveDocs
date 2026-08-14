---
title: Add semantic code tooltips
---

# Add semantic code tooltips

FsLiveDocs compiles documentation once during capture and stores renderer-neutral semantic data for later rendering.

## How semantic code works

The pipeline uses one expanded documentation model:

```text
Markdown and transclusions
        ↓
stable documentation blocks
        ↓
project compiler evaluation
        ↓
verification cases and semantic results
        ↓
renderer-neutral release capsule
        ↓
current HTML renderer
```

The stored semantic component contains:

- token text and FsLiveDocs token classifications;
- block-local tooltip references;
- inferred signatures and documentation;
- mapped diagnostics;
- source and page-context hashes;
- the repository documentation prelude.

It does not contain HTML, formatter types, CSS classes, or tooltip DOM IDs.

## Use page-scoped blocks

Ordinary F# blocks share one page compilation context:

````markdown
```fsharp
let add left right = left + right
```

```fsharp
let total = add 20 22
```
````

The second block can use declarations from the first block.

## Add shared setup

Use `prepare` for setup that later blocks need:

````markdown
```fsharp prepare
open System
let now = DateTimeOffset.UnixEpoch
```
````

The setup participates in compilation and source hashing. The renderer displays it as shared setup.

## Check a block independently

Use `isolated` when a block must not share declarations with the page:

````markdown
```fsharp isolated
let value = 42
```
````

## Select a project

Set `project` in front matter when a page needs a nondefault project:

```yaml
---
title: HTTP client
project: src/Example.Http/Example.Http.fsproj
---
```

Pass every selected project to FsLiveDocs. The command fails and lists the supplied projects when one is missing.

## Configure repository setup

Set `fSharpPrelude` in `.livedocs/config.json`:

```json
{
  "fSharpPrelude": "open System\nopen Example"
}
```

FsLiveDocs compiles the prelude for checked pages and stores it in the semantic component.

## Understand hash failures

FsLiveDocs hashes each block's normalized source and semantic mode. It also hashes page preparation and other context that can change meaning.

A declared semantic component must match the captured content. Missing blocks or hash mismatches fail the build.

Do not weaken this check. Capture a new unreleased capsule or use the matching released capsule.

## Handle old releases

Legacy history entries without semantic data use syntax-only rendering.

Once a release declares semantic data, FsLiveDocs does not silently fall back to syntax-only output.
