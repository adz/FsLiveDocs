# 💻 FsLiveDocs.Cli

The command-line interface for FsLiveDocs. This project provides the `livedocs` tool used to scaffold, build, and test your documentation.

## Commands

- `init`: Scaffolds a new project.
- `build`: Generates the static documentation site.
- `generate-tests`: Generates a Verify-based snapshot test project for selected examples.
- `test`: Runs the legacy direct docstring verifier.
- `watch`: Starts a dev server with hot-reloading.
- `extract`: Dumps the `PackageModel` to JSON.

## Usage

```bash
livedocs build MyProject.fsproj --theme emerald
```
