---
title: Introduction
weight: 0
---

# Build Your First Verified Docs Site

This tutorial walks through a minimal FsLiveDocs workflow from a blank class library to a verified documentation site.

The goal is simple:

1. write code and document it in the same file,
2. add an executable example,
3. build the site,
4. verify the example fails if the code changes.

## 1. Create a project

Start with a normal class library:

```bash
dotnet new classlib -lang F# -n MyLibrary
cd MyLibrary
```

Add FsLiveDocs to the solution or install the local tool you use for the repo.

```bash
dotnet tool restore
```

## 2. Write documented code

Document the API directly in your source file:

```fsharp
namespace MyLibrary

module Math =
    /// <summary>Adds two numbers and returns the result.</summary>
    /// <example name="BasicAdd">
    /// let value = Math.add 10 20
    /// printfn "%d" value
    /// // EXPECTED: 30
    /// </example>
    let add x y = x + y
```

The `summary` becomes the reference text on the API page. The `example` becomes a real test.

## 3. Scaffold the docs site

Use the CLI to create the default docs layout:

```bash
livedocs init
```

This creates the `.livedocs/` history folder and a starter `docs/index.md` file.

## 4. Build and preview

Build the site from your project:

```bash
livedocs build src/MyLibrary/MyLibrary.fsproj
```

If you want the full local workflow, run the preview script used by this repo:

```bash
./scripts/preview.sh
```

That pipeline compiles your code, extracts symbols, renders the static site, and runs the example verification pass.

## 5. Check the failure mode

Change the implementation so the example is wrong:

```fsharp
let add x y = x * y
```

Build again. The verification step now fails because the documented output no longer matches the executable example.

That is the core FsLiveDocs loop: docs are only published if they still run.

## 6. What to add next

Once the tutorial works, expand the site with:

1. API reference pages for each module and type.
2. Examples that cover edge cases and error paths.
3. A guide for when to use doc-tests and when to use ordinary tests.

Next, read the [Verified Examples guide](verified-examples.html) for a task-oriented walkthrough of scenarios and setup functions.
