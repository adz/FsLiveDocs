# C# in the API reference

## Status

This document records an investigation, not an accepted plan.

Findings come from purpose-built C# and F# probe projects that cover each type kind. Each finding is measured unless marked as inferred.

## Executive summary

API extraction already supports C#. `SymbolLister.extractFromProject` reads a compiled assembly and its XML documentation without depending on the source language.

A `.csproj` with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` produced a populated `PackageModel` on the first attempt.

The model contained correct member names, parameter names and types, resolved type links, and `<example>` content.

Downstream processing is not ready for C#. Current limitations include:

- member `cref` links that stop the build;
- a type taxonomy that flattens most C# and several F# types;
- compiler-generated members exposed as authored API;
- missing public fields;
- an F#-specific example verification pipeline.

## Blocking issue: member `cref` links

**Measured:** A C# `<see cref="Add"/>` reference to a method causes a hard build failure.

FSharp.Formatting emits a member-anchored URL:

```text
LINK NORMALIZATION FAILED: FSharp.Formatting emitted an API reference link that FsLiveDocs could not
normalize: Gets the running total. See <a href="/reference/csprobe-calculator.html#Add">Calculator.Add</a>.
```

`Presentation.resolveApiReferenceLinks` recognizes `/reference/{entity}.html`. It does not recognize the `#Add` fragment.

The URL therefore survives normalization, and the guard in `Presentation.fs` raises `invalidOp` as designed.

Member references are common in C# XML documentation. A realistic C# project is likely to encounter this failure quickly.

The defect is not specific to C#. Any member-anchored reference can trigger it, but idiomatic C# uses these references more often.

### Required fix

Resolve the entity portion of a member URL, then preserve or remap its fragment. Complete this work before claiming usable C# support.

## Model limitations

### Type classification

**Measured:** Every tested C# type maps to `EntityKind.Type`.

| C# declaration | Current kind |
| --- | --- |
| `class Calculator` | `Type` |
| `interface IShape` | `Type` |
| `enum Colour` | `Type` |
| `struct Point` | `Type` |
| `record Person` | `Type` |
| `static class Helpers` | `Type` |

This limitation also affects F#.

| F# declaration | Current kind |
| --- | --- |
| Interface | `Type` |
| Enum | `Type` |
| Delegate | `Type` |
| Type abbreviation | `Type` |
| Class | `Type` |
| Struct record | `Record` |
| Record | `Record` |
| Union | `Union` |

The model loses the struct property of a struct record. It cannot distinguish F# interfaces, enums, delegates, abbreviations, or classes.

`FSharpEntity` exposes the required properties. The following ordering compiles against pinned FCS 43.12.201:

```fsharp
if e.IsNamespace then Namespace
elif e.IsFSharpModule then Module
elif e.IsFSharpRecord then Record
elif e.IsFSharpUnion then Union
elif e.IsInterface then Interface
elif e.IsEnum then Enum
elif e.IsDelegate then Delegate
elif e.IsFSharpAbbreviation then Abbreviation
elif e.IsValueType then Struct
elif e.IsClass then Class
else Type
```

Ordering is significant:

- a struct record is both an F# record and a value type;
- an enum is also a value type;
- a union is a CLR class.

This ordering prioritizes F# semantic kinds over CLR kinds. It preserves current record and union classification.

The change requires widening `EntityKind`, updating its string representation and renderer display, and handling every exhaustive match. The F# compiler identifies affected matches.

### Compiler-generated members

**Measured:** A C# `record Person(string Name)` exposes generated implementation details as public API:

```text
.ctor(Name)          .ctor(original)      ToString             GetHashCode
Equals(obj)          Equals(other)        <Clone>$             Deconstruct(Name)
EqualityContract     Name                 (=)                  (<>)
```

`<Clone>$` and `EqualityContract` are compiler artifacts. Enums also expose the `value__` backing field.

F# records generate similar machinery, so this is partly an existing F# problem.

Filtering should combine `IsCompilerGenerated` with name checks for `<...>$` and `value__`.

Do not remove authored overrides of `ToString`, `Equals`, or similar members.

### Public fields

**Measured:** `struct Point { public int X; }` produces no members. Public fields are missing from extraction.

Public fields are uncommon in idiomatic F# but common in C# structs and interop APIs.

The source of the loss is unknown. Determine whether FSharp.Formatting omits fields or `mapEntity` loses them through its use of `AllMembers` before estimating the fix.

## Example verification limitation

**Measured:** C# `<example><code>` content extracts successfully:

```text
EXAMPLE Example snapshot=false content=var c = new Calculator();\nConsole.WriteLine(c.Add(1, 2));
```

The verification pipeline is F#-specific:

- `ExampleTranscript.parse` expects F# transcript conventions;
- `FsiTranscriptRunner` executes F# Interactive;
- `DocumentationCompiler` uses the F# compiler;
- the renderer labels extracted code as `fsharp`.

Choose one product policy before supporting C# examples.

### Option 1: Document but do not verify C# examples

This option is inexpensive but weakens the product guarantee.

The UI must identify each unverified block. An unverified example must not look equivalent to a verified example.

### Option 2: Verify C# examples with Roslyn

This option preserves the verification guarantee but adds a major subsystem:

- a second execution engine;
- a second transcript format;
- C# semantic extraction for tooltips.

## Parameter extraction

`SourceParameters` parses the F# untyped AST and cannot read `.cs` files. This does not currently block C# extraction.

C# does not support destructuring in parameter position. Each parameter has a source name.

The C# probe produced `left`, `right`, `value`, and `items` correctly, with zero diagnostics. The `displayParameterNames` fallback works for an unknown source language.

## Recommended work

| Work | Estimate | Blocking C# support? |
| --- | --- | --- |
| Normalize member-anchored `cref` links | Small | Yes |
| Widen `EntityKind` | Small to medium | No, but current output is poor |
| Filter compiler-generated members | Medium | No, but current output is poor |
| Surface public fields | Unknown; investigate first | No |
| Verify C# examples with Roslyn | Large | Only if verification must include C# |

Implement member-link normalization first.

Type classification and generated-member filtering improve F# output and can land independently of C# support.

The main product decision is whether “verified documentation” includes C# examples. Resolve that policy before estimating a complete C# implementation.

## Reproduce the findings

Use two throwaway probe projects:

- a C# project containing a class, interface, enum, struct, record, static class, and extension method;
- an F# project containing each F# type kind.

Call `SymbolLister.extractFromProjectWithDiagnostics` directly.

Do not use `livedocs extract` for the probes. That command also resolves this repository’s Markdown and can fail on unrelated missing examples before reaching the probe.
