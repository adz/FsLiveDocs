# 🧪 FsLiveDocs.Runner.DocTestRunner

Ensures your documentation never lies. The `DocTestRunner` extracts code examples from your XML docstrings and executes them as part of your CI pipeline.

## How it Works

1.  **Extraction**: It finds all `<example>` tags in your source code.
2.  **Generation**: It generates a temporary F# project containing these examples.
3.  **Execution**: It runs the generated code and compares the output with the `// EXPECTED:` comments.

## Examples

If you have a function like this:

```fsharp
/// <example>
/// add 1 1
/// // EXPECTED: 2
/// </example>
let add x y = x + y
```

The `DocTestRunner` will verify that `add 1 1` indeed returns `2`.

## Scenarios

For more complex examples that require setup (like a database connection), you can use `[<DocScenario>]`.

{{< example id="DocScenarioUsage" >}}

## Key Functions

- `verifyExamples`: The main entry point for running doc-tests.
- `generateTestProject`: (Internal) Scaffolds the ephemeral test project.

{{< example id="VerifyExamplesExample" >}}
