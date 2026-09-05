# Dogfood the public annotations API

## Problem

FsLiveDocs discovers only `.fsproj` files for API extraction. Its only supported library package, `FsLiveDocs.Annotations`, is a C# project and is therefore absent from the generated API reference.

The current authored API landing page instead presents `Core`, `Runner`, `Renderer`, and `Cli` as integration targets. These assemblies are implementation details bundled into the `FsLiveDocs` .NET tool.

This leaves the public API undocumented and makes the project's generated reference misleading. An authored-only annotations page would improve discovery but would not exercise FsLiveDocs' API extraction pipeline.

## Decision

Convert `FsLiveDocs.Annotations` from C# to F# while preserving its public CLR and NuGet contract. Use that project as the product's generated public API surface.

Keep the product boundary explicit: `FsLiveDocs` is a .NET tool, while `FsLiveDocs.Annotations` is the supported library package. Core, Runner, Renderer, and CLI remain internal implementation assemblies.

## Documentation behavior

The API landing page must direct readers to the tool's guides and command reference, and to the generated annotations API. It must not recommend internal assemblies as integration targets.

Add long-form API content for `FsLiveDocs.DocScenarioAttribute`. Its generated page must explain the constructor, `Name`, valid usage, and relationship to XML example scenarios.

The generated site configuration must select `FsLiveDocs.Annotations` and the `Acme.Docs` example for API extraction. The top navigation must include an external link to the FsLiveDocs GitHub repository.

`Acme.Docs` must be clearly identified as a sample API used to demonstrate FsLiveDocs. Authored API pages must explain its namespace, order record, order module, and scenario-based customer context.

Generated package landing pages must reuse the authored introduction of the matching package namespace. If package and namespace names differ, use the first documented namespace owned by that package.

## Compatibility requirements

The migration must preserve:

- package ID `FsLiveDocs.Annotations`;
- namespace `FsLiveDocs`;
- CLR type name `DocScenarioAttribute`;
- sealed inheritance from `System.Attribute`;
- constructor signature `DocScenarioAttribute(string name)`;
- read-only `string Name` property;
- method-only usage with `AllowMultiple = false`;
- `netstandard2.0` target framework.

Existing C# and F# consumers must not require source changes.

## Implementation requirements

Replace the C# project and source with an F# project and source file. Update every solution and project reference to the new `.fsproj` path.

Use XML documentation on the F# type, constructor, and property as the concise generated reference. Keep longer usage guidance in `docs/api/FsLiveDocs.DocScenarioAttribute.md`.

Configure `.livedocs/config.json` with the annotations and sample projects, plus explicit Home, API, and GitHub navigation entries.

## Acceptance criteria

- The solution builds and all tests pass.
- `FsLiveDocs.Annotations` remains packable for `netstandard2.0`.
- Reflection confirms the compatible public CLR shape and attribute usage.
- FsLiveDocs project discovery includes the annotations F# project.
- The configured generated API contains the annotation contract and documented `Acme.Docs` sample, not internal tool assemblies.
- Package landing pages orient readers with renderer-neutral authored namespace content.
- The API landing page describes the public package boundary and sample API accurately.
- The generated top navigation links to the GitHub repository.

## Out of scope

No in-process SDK is introduced. A future `FsLiveDocs.Api` or `FsLiveDocs.SDK` package requires intentional request and result models, cancellation, progress, error handling, and a separate compatibility promise.
