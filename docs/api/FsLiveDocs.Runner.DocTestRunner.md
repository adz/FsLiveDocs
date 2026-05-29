# 🧪 FsLiveDocs.Runner.DocTestRunner

Ensures your documentation never lies. The `DocTestRunner` extracts code examples from your XML docstrings and executes them as part of your CI pipeline.

## How it Works

1.  **Extraction**: It finds all `<example>` tags in your source code.
2.  **Preparation**: It loads the built project assembly and any setup scenarios.
3.  **Execution**: It runs each example as a transcript-style FSI session and compares the printed output.

## Examples

If you have a function like this:

```fsharp
/// <example>
/// > let add x y = x + y;;
/// > add 1 1;;
/// 2
/// </example>
let add x y = x + y
```

The `DocTestRunner` will verify that the example still produces `2`.

## Scenarios

For more complex examples that require setup (like a database connection), you can use `[<DocScenario>]`.

{{< example id="DocScenarioUsage" >}}

## Key Functions

- `verifyExamples`: The main entry point for running doc-tests.
- `resolveAssemblyPath`: Locates the built assembly used by the transcript runner.
