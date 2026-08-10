---
title: Verified Examples
weight: 1
type: how-to
---

# Verified Examples for Real Projects

This guide shows how to move beyond toy snippets and use FsLiveDocs for code that needs setup, dependencies, and controlled test fixtures.

The examples here follow two selection rules:

1. transcript-style `<example>` blocks are picked up automatically when they already show FSI input/output,
2. any `<example>` or `<code language="fsharp">` block can opt in explicitly with `data-livedocs="snapshot"`.

That means the first cut is simple: write the example in source, run `livedocs generate-tests`, inspect the generated Verify snapshot, and accept it when the output is right.

## The problem

Many docs examples work only when the process is already in the right state. That usually means:

1. a database connection exists,
2. a fake clock is configured,
3. a service dependency is already wired,
4. a temporary folder is available.

FsLiveDocs supports that shape by letting you pair a transcript-style example with a named setup scenario, then generate a Verify-based test project that snapshots the evaluated result.

## Basic example

This is the simplest case: a plain example with no setup, no DI, and no extra state.

You can see the same pattern in the source example for `ContentProvider.resolveSnippets`:

{{< example id="ResolveSnippetExample" >}}

For a fuller FSI session, the core library also includes a multiline transcript example:

{{< example id="MapTranscript" >}}

## Adding a setup scenario

Mark a function with `[<DocScenario>]` and give the example a matching `scenario` name. The shared name is the join key: the generated test project scans the extracted examples, finds the matching scenario, and runs that setup function before the example body.

```fsharp
open FsLiveDocs.Core

[<DocScenario("with-db")>]
let configureDatabase () =
    // This setup runs before the example below because both use "with-db".
    let connectionString = "Host=localhost;Database=docs"
    connectionString
```

Then reference it from the example:

```fsharp
/// <example name="RepositoryLookup" scenario="with-db">
/// > let repo = Repository.create();;
/// > let user = repo.findUser 42;;
/// > user.Name;;
/// val it: string = "Ada"
/// </example>
```

When FsLiveDocs evaluates a snapshot example, it looks up `ScenarioModel.Name = "with-db"` and calls the corresponding `MethodId` before it executes the example body. That is what connects the setup function to the `scenario` attribute.

## What actually runs

The example body is extracted from the XML doc comment and copied into the snapshot runner. It is run in the context of the compiled project, not pasted into this guide page.

If the example has no expected output yet, FsLiveDocs treats it as a first cut and includes the evaluated result in the generated snapshot project so you can review and update the source intentionally.

The scenario function is different. FsLiveDocs discovers it from the compiled project assembly by looking for `[<DocScenario>]`, then calls the compiled method before the example body runs.

The real code paths are here:

```fsharp prepare
open System
```

{{< snippet id="DocScenarioAttributeUsage" >}}

{{< snippet id="DocScenarioPattern" >}}

This means:

1. the scenario does not have to be written inline next to the guide text,
2. the example is still tested verbatim, with its original lines preserved,
3. the scenario can live in the same project or any other project you pass to the build,
4. the runner uses the scenario name to connect the example to the compiled setup function,
5. the generated test project can snapshot the evaluated result with Verify so changes are reviewed explicitly.

## Using dependency injection

For service-heavy code, keep the setup function responsible for construction and keep the doc snippet focused on behavior.

```fsharp no-check reason="Application-specific service fixture types are abbreviated"
let mutable service = Unchecked.defaultof<UserService>

[<DocScenario("with-services")>]
let buildServices () =
    let clock = FakeClock(System.DateTime(2024, 1, 1))
    let store = InMemoryStore()
    service <- UserService(store, clock)
```

```fsharp
/// <example name="ServiceBehavior" scenario="with-services">
/// > let result = service.CreateUser "Jane";;
/// > result.Status;;
/// val it: string = "Created"
/// </example>
```

The scenario runs first and assigns the shared `service` binding. The example then reads like a caller, not like a bootstrap script. The important part is that the scenario name binds the setup function to the example, not that the variable name happens to be `service`.

## Recommended patterns

1. Use one setup scenario per external concern.
2. Keep each snippet short enough to understand in one screen.
3. Prefer pure assertions over logs or incidental output.
4. Keep fixtures deterministic so the doc-test output does not drift.

## What the runner does

The runner collects selected examples, looks up scenario functions, runs the setup, and then executes the transcript in that context.

That means the following are true:

1. the snippet is compiled,
2. the setup function is compiled,
3. the combined execution is verified,
4. the result is reflected back into the build,
5. if the example does not yet have expected output, it is treated as a first cut and shows up in the snapshot payload for later source update.

If you want to generate the snapshot test project, run:

```bash
livedocs generate-tests src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj
```

The generated project can then be tested directly with `dotnet test`, and any received snapshots can be accepted through the usual Verify workflow.

If you want the full design trade-offs around when not to use doc-tests, read the [DocTest Design guide](../doctest-design.html).
