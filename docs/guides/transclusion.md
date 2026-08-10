---
title: Snippet Transclusion
weight: 2
type: how-to
---
# Snippet Transclusion

You can pull live code directly from your source files into your documentation. This ensures that when your implementation changes, your docs update automatically.

## How it works

Mark your code with snippet tags:

```fsharp isolated
// <snippet:ProjectStructure>
type SourceLink = {
    File: string
    Line: int
}
// </snippet:ProjectStructure>
```

Then reference it in your markdown docs:

```
\{{snippet id="ProjectStructure" }}
```

Here it's actually pulling from the snippet: xref:T:FsLiveDocs.Core.SourceLink

{{< snippet id="ProjectStructure" mode="isolated" >}}
