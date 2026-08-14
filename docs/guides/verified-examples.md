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

## Generate stable tests

```bash
dotnet livedocs generate-tests src/YourLibrary/YourLibrary.fsproj
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj
```

Discovery produces stable case values. Generated xUnit facts pass each value to one runner interface.

The runner owns coverage validation, compile-before-execute ordering, transcript behavior, and stale-case detection.

## Audit without generated tests

```bash
dotnet livedocs audit src/YourLibrary/YourLibrary.fsproj
```

Audit classifies every block as passed, excluded, or failed. A successful release capture requires complete coverage.
