---
title: Snippet Transclusion
weight: 2
---
# Snippet Transclusion

You can pull live code directly from your source files into your documentation. This ensures that when your implementation changes, your docs update automatically.

## How it works

Mark your code with snippet tags:

```fsharp
// <snippet:MyFunction>
let myFunc () = 42
// </snippet:MyFunction>
```

Then reference it in Markdown:

{{< snippet id="MyFunction" >}}
