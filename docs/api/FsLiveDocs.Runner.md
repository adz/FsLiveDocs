# 🏃 FsLiveDocs.Runner

The verification engine. This namespace is responsible for turning your documentation into a living test suite.

## Key Modules

- **DocTestRunner**: Extracts, compiles, and executes examples from XML docstrings.

## Transcript Style

The runner is designed around FSI transcripts, so the examples in this project are written the way you would type them into F# Interactive and read them back:

> `let x = 1;;`
> `x;;`
> `val x: int = 1`

That same shape is used in the source docs for a scenario-backed example:

{{< example id="UserGreeting" >}}

The runner reads the transcript, loads the scenario, and verifies that the returned value matches the exact FSI output.

## Why Verification Matters?

Documentation that is out of date is worse than no documentation at all. By treating every code example as a test case, `FsLiveDocs.Runner` guarantees that your users always see code that actually works with the current version of your library.
