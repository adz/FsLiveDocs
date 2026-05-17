---
title: Verified Examples
weight: 1
---
# Verified Examples

FsLiveDocs ensures that your examples are always correct. By using the `<example>` tag in your XML documentation, you can provide snippets that are automatically extracted and verified.

## Example Usage

```fsharp
/// <example name="HelloExample">
/// Say.hello "F#"
/// // EXPECTED: Hello F#
/// </example>
```

You can transclude these examples into your guides using the example shortcode:

{{< example id="HelloExample" >}}
