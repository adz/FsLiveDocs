---
title: Transclude source and examples
---

# Transclude source and examples

Transclude compiled source or verified XML examples instead of copying code into a guide.

## Mark a source snippet

Add markers around the source:

```fsharp isolated
// <snippet:ProjectStructure>
type SourceLink = {
    File: string
    Line: int
}
// </snippet:ProjectStructure>
```

Reference the snippet from Markdown:

```text
{{< snippet id="ProjectStructure" mode="isolated" >}}
```

Supported modes are `prepare`, `isolated`, `run`, and `no-check`. A `no-check` snippet also requires `reason="..."`.

## Transclude an XML example

Reference a named XML example:

```text
{{< example id="CreateExample" >}}
```

FsLiveDocs preserves the example's execution or exclusion contract when it creates the canonical fenced block.

## Understand release capture

Capture expands transclusions before it stores canonical Markdown. A later history render does not need the original source file or shortcode implementation.

Cross-references remain semantic until render time so the current renderer can create current page URLs.
