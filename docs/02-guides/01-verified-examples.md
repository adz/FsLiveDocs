---
title: Author and test examples
---

# Author and test examples

Choose the least powerful mode that proves the point. Most examples only need to compile.

## Compile a page as one story

Ordinary `fsharp` blocks share a page compilation unit. Later blocks can use earlier declarations, but none of them run.

````markdown
```fsharp
let subtotal = 120M
```

```fsharp
let total = subtotal * 1.2M
```
````

Use `prepare` for setup that readers need less often:

````markdown
```fsharp prepare
open System
```
````

Use `isolated` when a snippet should stand on its own:

````markdown
```fsharp isolated
let normalize (value: string) = value.Trim()
```
````

## Run only what matters

Use `run` when runtime behavior is part of the claim:

````markdown
```fsharp run
printfn "order-total=120.0"
```
````

Use `transcript` when the exact FSI result matters:

````markdown
```fsharp transcript
> 20 + 22;;
val it: int = 42
```
````

Executable examples have the same file, process, network, clock, and environment access as the person running FsLiveDocs. Keep them quick, deterministic, and local.

## Mark honest pseudocode

Sometimes a fragment is useful even though it cannot compile alone. Say why:

````markdown
```fsharp no-check reason="The omitted cases are application-specific"
match result with
| Ok value -> publish value
| Error _ -> ...
```
````

A nonempty reason is required and appears in the audit result.

## Check XML examples

XML examples stay beside the API they explain:

```fsharp
/// <summary>Adds two values.</summary>
/// <example name="add-values" data-livedocs="snapshot">
/// > add 20 22;;
/// val it: int = 42
/// </example>
let add left right = left + right
```

Use `data-livedocs="snapshot"` when output is part of the contract. Use `data-livedocs="no-check" reason="..."` only for a deliberate exclusion.

## Prepare an XML example

Some examples need known state. Add the small annotations library to the documented project:

```bash
dotnet add package FsLiveDocs.Annotations
```

Do not reference the `FsLiveDocs` tool package from your library. `FsLiveDocs.Annotations` is the lightweight compile-time contract.

Mark a public, parameterless F# function with a unique scenario name:

```fsharp
open FsLiveDocs

module CustomerExamples =
    let mutable private discount = 0M

    [<DocScenario("preferred-customer")>]
    let loadPreferredCustomer () =
        discount <- 0.1M

    /// <example name="preferred-price"
    ///          scenario="preferred-customer"
    ///          data-livedocs="snapshot">
    /// > CustomerExamples.price 100M;;
    /// val it: decimal = 90.0M
    /// </example>
    let price subtotal = subtotal * (1M - discount)
```

FsLiveDocs starts a fresh FSI session, loads the project, runs scenario setup, and then evaluates the example. Setup output is not included in the expected transcript.

Scenario names must be unique across the build. The XML `scenario` value must match the `DocScenario` name exactly.

## Use the quick test loop

Run all checks without creating test source:

```bash
dotnet livedocs test
```

This audits every block, compiles page and isolated units, then runs explicit examples. It writes nothing and is enough for many repositories.

Use `audit` when you want compilation and coverage checks without execution:

```bash
dotnet livedocs audit
```

## Manage examples as normal tests

Generate an xUnit project when examples should appear in your IDE, normal test run, and test reports:

```bash
dotnet livedocs generate-tests
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
```

The generated project contains one fact per discovered case:

- one compile fact per page unit and isolated block;
- one execution fact per `run` block and transcript;
- one fact per XML example, using Verify for snapshots.

The facts rerun FsLiveDocs discovery and catch removed or renamed cases. They cannot discover a brand-new case until you regenerate the project.

Regenerate whenever a checked fence or XML example is added, removed, or renamed. Commit the generated diff.

A useful freshness check is:

```bash
dotnet livedocs generate-tests --interactive false --banner false
git diff --exit-code tests/FsLiveDocs.SnapshotTests
```

That command produces no diff when the project is current, so it fits a pre-commit hook or CI job.

## Read a failure

Failures name the authored page and stable case, such as `guides/orders.md#fsharp-2`. Compiler line numbers are mapped back to that block rather than the temporary F# file.

A compile failure usually means the example no longer matches the selected project or an earlier page block. A transcript failure shows expected and actual output. A stale-case failure means the generated test project needs regenerating.

Run the narrower command while editing:

```bash
dotnet livedocs audit --verbosity debug --interactive false
```

`debug` includes compiler messages and every discovered block. Once compilation passes, `test` gives the runtime or transcript result.

## Choose a practical policy

A simple default works well:

1. Compile ordinary examples.
2. Use isolated blocks for copy-and-paste snippets.
3. Run only examples that make a runtime claim.
4. Snapshot only output readers depend on.
5. Generate tests when your team benefits from normal test tooling.
6. Run freshness checks in CI if generated tests are committed.

See [Verify documentation in CI](continuous-integration.md) for a complete pipeline.
