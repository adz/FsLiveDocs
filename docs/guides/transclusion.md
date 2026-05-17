---
title: Snippet Transclusion
weight: 2
---
# Snippet Transclusion

You can pull live code directly from your source files into your documentation. This ensures that when your implementation changes, your docs update automatically.

## How it works

Mark your code with snippet tags:

```fsharp
// <snippet:ProjectStructure>
type SourceLink = {
    File: string
    Line: int
}
// </snippet:ProjectStructure>
```

Then reference it in Markdown:

{{< snippet id="ProjectStructure" >}}
