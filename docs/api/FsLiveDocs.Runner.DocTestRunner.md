# 🧪 FsLiveDocs.Runner.DocTestRunner

Ensures your documentation never lies. The `DocTestRunner` extracts code examples from your XML docstrings and evaluates them so a generated Verify project can snapshot the result.

## How it Works

1.  **Extraction**: It finds transcript-style `<example>` tags in your source code, plus explicit snapshot examples marked with `data-livedocs="snapshot"`.
2.  **Preparation**: It loads the built project assembly and any setup scenarios.
3.  **Execution**: It runs each selected example as a transcript-style FSI session and records the result for snapshot verification.

## Examples

If you have a function like this:

```fsharp
/// <example>
/// > let add x y = x + y;;
/// > add 1 1;;
/// val it: int = 2
/// </example>
let add x y = x + y
```

The `DocTestRunner` will verify that the example still produces `val it: int = 2`.

## Scenarios

For more complex examples that require setup (like a database connection), you can use `[<DocScenario>]`.

{{< example id="DocScenarioUsage" >}}

## Key Functions

- `collectSnapshots`: Returns the snapshot payload used by the generated Verify project.
- `verifyExamples`: The legacy direct verifier for transcript-style examples.
- `resolveAssemblyPath`: Locates the built assembly used by the transcript runner.
