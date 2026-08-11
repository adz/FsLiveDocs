# C# in the API Reference

## Status

Investigation, not an accepted plan. Findings below come from running the extractor against a purpose-built
C# probe project and an F# probe project covering every type kind; each claim is marked as measured or inferred.

## Summary

Extraction already works. FsLiveDocs reads a compiled assembly plus its XML documentation file
(`SymbolLister.extractFromProject`), and that path never asks what language produced them. Pointing it at a
`.csproj` with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` produced a populated
`PackageModel` on the first attempt, with correct member names, parameter names, parameter types, resolved
type links, and extracted `<example>` content.

What does not work is everything downstream of extraction: one hard failure that stops a build, a type
taxonomy that flattens most of C#, compiler-generated members that would be published as public API, and a
verification story that does not transfer.

## Measured findings

### 1. `<see cref="..."/>` to a member is a hard build failure

This is the blocker. A C# doc comment containing `<see cref="Add"/>` — where `Add` is a method — makes
FSharp.Formatting emit a member-anchored link, and `Presentation.resolveApiReferenceLinks` cannot normalize
it:

```text
LINK NORMALIZATION FAILED: FSharp.Formatting emitted an API reference link that FsLiveDocs could not
normalize: Gets the running total. See <a href="/reference/csprobe-calculator.html#Add">Calculator.Add</a>.
```

The normalizer matches `/reference/{entity}.html` and rewrites it by looking the label up among known
entities. The `#Add` fragment is not part of that shape, so the link survives the rewrite, and the guard in
`Presentation.fs` raises `invalidOp` by design.

Cross-referencing a member is entirely ordinary in C# XML docs, so in practice the first realistic C#
project fails the build. Note this is not strictly C#-specific — any member-anchored reference would hit the
same path — but C# reaches it far sooner because `cref` to a method is idiomatic there.

Fixing it means teaching the normalizer about member anchors: resolve the entity part, then preserve or
remap the fragment. Contained, but it must be done before any C# support is usable.

### 2. Every C# type kind collapses to `Type` — and so do most F# ones

`EntityKind` is `Namespace | Module | Record | Union | Type`, assigned in `mapEntity` from `IsFSharpModule`,
`IsNamespace`, `IsFSharpRecord`, `IsFSharpUnion`, else `Type`. Measured against the C# probe:

| Declaration | Kind assigned |
| --- | --- |
| `class Calculator` | `Type` |
| `interface IShape` | `Type` |
| `enum Colour` | `Type` |
| `struct Point` | `Type` |
| `record Person` | `Type` |
| `static class Helpers` | `Type` |

The important part is that **this is not a C# limitation**. Measured against an F# probe declaring one of
each:

| F# declaration | Kind assigned |
| --- | --- |
| `type IThing = abstract Do: int -> int` | `Type` |
| `type Colour = Red = 0 \| Green = 1` | `Type` |
| `type Handler = delegate of int -> unit` | `Type` |
| `type Count = int` (abbreviation) | `Type` |
| `type Widget(name) = ...` (class) | `Type` |
| `[<Struct>] type Point = { X: int }` | `Record` |
| `type Person = { Name: string }` | `Record` |
| `type Shape = Circle \| Square` | `Union` |

F# interfaces, enums, delegates, abbreviations and classes are already indistinguishable in the model today,
and a struct record is reported as a plain record with its struct-ness dropped. So distinguishing these is an
existing F# gap that C# support would merely make impossible to ignore.

They are fully representable. `FSharpEntity` exposes `IsInterface`, `IsEnum`, `IsDelegate`,
`IsFSharpAbbreviation`, `IsValueType` and `IsClass` alongside the four properties already consulted. A
kind function using all of them compiles against the pinned FCS (43.12.201) — verified, not assumed:

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

Order matters and carries real decisions: a struct record satisfies both `IsFSharpRecord` and `IsValueType`,
an enum is also a value type, and a union is a class at the CLR level. The listing above keeps the F#
semantic kind ahead of the CLR kind, which preserves today's behaviour for records and unions rather than
silently reclassifying them.

Cost is a widened `EntityKind`, its `ToString()`, and the renderer's kind-dependent display. Every `match`
over `EntityKind` becomes incomplete, so the compiler will enumerate the call sites.

### 3. Compiler-generated members are published as public API

The C# `record Person(string Name)` produced these members, all indistinguishable from authored API:

```text
.ctor(Name)          .ctor(original)      ToString             GetHashCode
Equals(obj)          Equals(other)        <Clone>$             Deconstruct(Name)
EqualityContract     Name                 (=)                  (<>)
```

`<Clone>$` and `EqualityContract` are pure compiler artifacts. The enum surfaced its `value__` backing
field. Rendering these would bury the one member the author actually wrote.

F# records generate comparable machinery, so this too is partly a pre-existing issue, but C# records are
common enough to make it acute. Filtering needs a rule — likely `IsCompilerGenerated` plus a name-shape
check for `<...>$` and `value__` — and the rule must not discard authored members that legitimately
override `ToString` or `Equals`.

### 4. Public fields disappear

`struct Point { public int X; }` extracted with **no members at all**. Fields are not surfaced. Idiomatic F#
rarely exposes public fields, so this has gone unnoticed; C# structs and interop types expose them routinely.
Unverified whether the loss is in FSharp.Formatting's model or in `mapEntity`'s use of `AllMembers` — worth
establishing before estimating.

### 5. Examples extract but cannot be verified

The C# `<example><code>` block extracted cleanly, with `IsSnapshotTest = false`:

```text
EXAMPLE Example snapshot=false content=var c = new Calculator();\nConsole.WriteLine(c.Add(1, 2));
```

That content is C#. The verification pipeline — `ExampleTranscript.parse`, `FsiTranscriptRunner`,
`DocumentationCompiler` — is FSI and F#-compiler shaped throughout. C# examples cannot run through it, and
the renderer would label the block `fsharp` and syntax-highlight it as such.

This is the one that is not a bug to fix but a product decision. Either:

- **C# is documented but unverified.** Cheap, but it quietly erodes the guarantee the tool exists to make.
  If taken, the UI must say so per-block; an unverified example that looks identical to a verified one is
  worse than no badge at all.
- **C# examples are verified via Roslyn scripting.** Preserves the guarantee, and is a substantial
  subsystem: a second execution engine, a second transcript format, and a second semantic extractor for
  hovers.

## What the parameter work does and does not cover

`SourceParameters` parses the **F# untyped AST** and cannot read `.cs` files. This turns out not to matter:
C# has no destructuring in parameter position, so every C# parameter carries a real name, and the C# probe
produced correct parameter names (`left`, `right`, `value`, `items`) with **zero diagnostics**. The
fallback chain in `displayParameterNames` degrades correctly for a language it knows nothing about.

## Rough shape of the work

| Scope | Effort | Blocking? |
| --- | --- | --- |
| Member-anchored `cref` normalization | Small | Yes — build fails without it |
| Widen `EntityKind` (helps F# too) | Small–medium | No, but output is poor without it |
| Filter compiler-generated members | Medium | No, but output is poor without it |
| Surface public fields | Unknown — needs investigation | No |
| C# example verification (Roslyn) | Large | Only if the guarantee must extend to C# |

The first row is a prerequisite. Rows two and three are worth doing on their own merits for F# and would
land independently of any C# decision. The last row is where the real question lives, and it is a question
about what "verified documentation" is promised to mean, not an engineering estimate.

## Reproducing

Both probes are throwaway projects: a `.csproj` with one file covering class, interface, enum, struct,
record, static class and extension method; and a `.fsproj` covering each F# type kind. Drive them with
`SymbolLister.extractFromProjectWithDiagnostics` directly rather than through the CLI — `livedocs extract`
also resolves this repository's own Markdown content and fails on unrelated missing examples before
reaching the probe.
