# 🧬 FsLiveDocs API Reference

Welcome to the FsLiveDocs API reference. The project is organized into several key areas, each responsible for a different part of the documentation lifecycle.

## Solution Map

- [FsLiveDocs.Core](FsLiveDocs.Core.html): data models, markdown resolution, and symbol extraction.
- [FsLiveDocs.Runner](FsLiveDocs.Runner.html): compiled doc-test execution.
- [FsLiveDocs.Renderer](FsLiveDocs.Renderer.html): HTML output and navigation.
- [FsLiveDocs.Cli](FsLiveDocs.Cli.html): command-line entry point.

## Core Components

- **FsLiveDocs.Core**: The heart of the engine. Contains the data models, Markdown processing logic, and symbol extraction bridges.
- **FsLiveDocs.Cli**: The user-facing command-line tool. Handles scaffolding, building, and watching projects.
- **FsLiveDocs.Renderer**: The visual layer. Transforms the internal models into a modern, static website using Giraffe.ViewEngine.
- **FsLiveDocs.Runner**: The verification engine. Executes code examples from your docstrings to ensure they remain correct.

## Worked Examples

These examples show the real code paths that make the solution useful:

- The snippet resolver turns markdown plus example metadata into rendered content: {{< example id="ResolveSnippetExample" >}}
- The core data model example shows how a verified example is represented as an FSI session: {{< example id="CreateExample" >}}
- The runner uses the same transcript format when it verifies a docstring example under a scenario: {{< example id="UserGreeting" >}}
- The renderer assembles the LLM summary page from the documented package model: {{< example id="GenerateLlmsTxtExample" >}}

## Getting Started

If you are new to FsLiveDocs, we recommend starting with the [Introduction](../introduction.html) guide. For a quick reference of all features, see the [Cheat Sheet](../cheat-sheet.html).
