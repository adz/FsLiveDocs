---
title: Verified Examples
weight: 1
type: how-to
---

# Verified Examples for Real Projects

This guide shows how to move beyond toy snippets and use FsLiveDocs for code that needs setup, dependencies, and controlled test fixtures.

## The problem

Many docs examples work only when the process is already in the right state. That usually means:

1. a database connection exists,
2. a fake clock is configured,
3. a service dependency is already wired,
4. a temporary folder is available.

FsLiveDocs supports that shape by letting you pair a snippet with a named setup scenario.

## Basic example

```fsharp
/// <example name="HelloExample">
/// Say.hello "F#"
/// // EXPECTED: Hello F#
/// </example>
```

That is the simplest case: no setup, no DI, no extra state.

## Adding a setup scenario

Mark a function with `[<DocScenario>]` and give the example a matching `scenario` name. The shared name is the join key: the generator scans all examples, finds the matching scenario, and runs that setup function before the example body.

```fsharp
open FsLiveDocs.Core

[<DocScenario("with-db")>]
let configureDatabase () =
    // This setup runs before the example below because both use "with-db".
    let connectionString = "Host=localhost;Database=docs"
    printfn "Configured %s" connectionString
```

Then reference it from the example:

```fsharp
/// <example name="RepositoryLookup" scenario="with-db">
/// let repo = Repository.create()
/// let user = repo.findUser 42
/// printfn "%s" user.Name
/// // EXPECTED: Ada
/// </example>
```

When FsLiveDocs generates the runner, it looks up `ScenarioModel.Name = "with-db"` and calls the corresponding `MethodId` before it executes the example body. That is what connects the setup function to the `scenario` attribute.

## What actually runs

The example body is extracted from the XML doc comment and copied into a generated test project. It is run in the context of that generated project, not pasted into this guide page.

The scenario function is different. FsLiveDocs discovers it from the compiled project assembly by looking for `[<DocScenario>]`, then calls the compiled method before the example body runs.

The real code paths are here:

{{< snippet id="DocScenarioAttributeUsage" >}}

{{< snippet id="DocScenarioPattern" >}}

{{< snippet id="ScenarioBinding" >}}

This means:

1. the scenario does not have to be written inline next to the guide text,
2. the example is still tested verbatim, with its original lines preserved,
3. the scenario can live in the same project or any other project you pass to the build,
4. the runner uses the scenario name to connect the example to the compiled setup function.

## Using dependency injection

For service-heavy code, keep the setup function responsible for construction and keep the doc snippet focused on behavior.

```fsharp
let mutable service = Unchecked.defaultof<UserService>

[<DocScenario("with-services")>]
let buildServices () =
    let clock = FakeClock(System.DateTime(2024, 1, 1))
    let store = InMemoryStore()
    service <- UserService(store, clock)
```

```fsharp
/// <example name="ServiceBehavior" scenario="with-services">
/// let result = service.CreateUser "Jane"
/// printfn "%s" result.Status
/// // EXPECTED: Created
/// </example>
```

The scenario runs first and assigns the shared `service` binding. The example then reads like a caller, not like a bootstrap script. The important part is that the scenario name binds the setup function to the example, not that the variable name happens to be `service`.

## Recommended patterns

1. Use one setup scenario per external concern.
2. Keep each snippet short enough to understand in one screen.
3. Prefer pure assertions over logs or incidental output.
4. Keep fixtures deterministic so the doc-test output does not drift.

## What the generated runner does

The generated `Program.fs` collects examples, looks up scenario functions, runs the setup, and then executes the snippet in that context.

That means the following are true:

1. the snippet is compiled,
2. the setup function is compiled,
3. the combined execution is verified,
4. the result is reflected back into the build.

If you want the full design trade-offs around when not to use doc-tests, read the new [DocTest Design guide](doctest-design.html).
