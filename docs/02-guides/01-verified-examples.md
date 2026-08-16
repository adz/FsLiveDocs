---
title: Verify F# examples
---

# Verify F# examples

Choose the least powerful mode that proves your documentation claim. Ordinary examples compile but do not execute.

## Compile a page

Use ordinary `fsharp` fences for progressive examples:

````markdown
```fsharp
type Order = { Total: decimal }
```

```fsharp
let order = { Total = 120M }
```
````

FsLiveDocs compiles these blocks in one page unit. It never executes them.

## Compile an independent example

Use `isolated` when the example must stand alone:

````markdown
```fsharp isolated
let normalize (value: string) = value.Trim()
```
````

## Run an example

Use `run` only when runtime behavior is part of the contract:

````markdown
```fsharp run
printfn "order-total=120.0"
```
````

Operational code can access files, processes, networks, clocks, or hosts. Do not mark ordinary examples `run` for stronger-looking verification.

## Verify an FSI transcript

Use `transcript` for input and expected output:

````markdown
```fsharp transcript
> 20 + 22;;
val it: int = 42
```
````

## Add page setup

Use `prepare` for declarations shared by later blocks:

````markdown
```fsharp prepare
type Customer = { Name: string }
```
````

## Mark deliberate pseudocode

Use `no-check` only when the fragment cannot be made complete:

````markdown
```fsharp no-check reason="The remaining cases are application-specific"
match result with
| Ok value -> publish value
| Error _ -> ...
```
````

FsLiveDocs requires a nonempty reason. Audit output reports the exclusion.

## Verify XML examples

Add an example to XML documentation:

```fsharp
/// <summary>Adds two values.</summary>
/// <example name="add-values" data-livedocs="snapshot">
/// > add 20 22;;
/// val it: int = 42
/// </example>
let add left right = left + right
```

Use `data-livedocs="snapshot"` when output is part of the contract. Use `data-livedocs="no-check" reason="..."` for a deliberate exclusion.

## Prepare XML examples with scenarios

Some XML examples need deterministic state before their transcript runs. Install the small annotations package in the library that owns the example:

```bash
dotnet add package FsLiveDocs.Annotations
```

`FsLiveDocs.Annotations` contains metadata consumed by FsLiveDocs; it does not bring the CLI, compiler service, or renderer into your library. Mark a public, parameterless function with a unique scenario name:

```fsharp
open FsLiveDocs

module CustomerExamples =
    let mutable private currentCustomer = "anonymous"

    [<DocScenario("preferred-customer")>]
    let preparePreferredCustomer () =
        currentCustomer <- "Ada"

    /// <summary>Greets the current customer.</summary>
    /// <example name="preferred-customer-greeting"
    ///          scenario="preferred-customer"
    ///          data-livedocs="snapshot">
    /// > CustomerExamples.greet();;
    /// val it: string = "Hello Ada"
    /// </example>
    let greet () = $"Hello {currentCustomer}"
```

For each example that names the scenario, FsLiveDocs starts the example session, loads the documented project, calls `preparePreferredCustomer()`, and then evaluates the example. Setup output is not part of the expected transcript.

Use scenarios for focused deterministic setup such as fixture data, dependency-injection state, or an in-memory test double. Keep setup fast and local: executable documentation has the same file, process, network, clock, and environment access as the user running FsLiveDocs.

Scenario rules:

- the `scenario` value must exactly match the `DocScenario` name;
- scenario names must be unique across the projects in one documentation build;
- the annotated F# function must compile to a callable static, parameterless method; a public function in an F# module is the usual form;
- the example fails when its named scenario cannot be found;
- each example gets a fresh FSI session, so one example must not depend on another example having run first.

Do not add `FsLiveDocs` itself as a library dependency. It is a .NET tool package. `FsLiveDocs.Annotations` is the compile-time contract for attributes used by documented projects.

## Generate stable tests

```bash
dotnet livedocs generate-tests
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
```

The command produces stable xUnit cases from the same documentation discovery result used by audit, build, and capture.

Run the generated test project in CI. FsLiveDocs handles coverage validation, compile-before-execute ordering, transcript behavior, and stale-case detection.

## Audit without generated tests

```bash
dotnet livedocs audit
```

Audit classifies every block as passed, excluded, or failed. A successful release capture requires complete coverage.
