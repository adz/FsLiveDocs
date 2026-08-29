---
title: Command reference
---

# Command reference

Run commands from the repository root. Project arguments are optional: FsLiveDocs uses explicit arguments first, the `projects` list in `.livedocs/config.json` second, and automatic discovery last.

Every command assumes it runs at the repository root with `.livedocs/` present
(`init` creates it). Every command that reads the API — `audit`, `test`,
`generate-tests`, `build`, `watch`, `capture`, `extract` — additionally assumes the
documented projects **already compiled** in this SDK; run `dotnet build` first. The
per-command **Assumes** column lists what each one needs beyond that.

## Repository setup

| Command | Result | Assumes |
| --- | --- | --- |
| `livedocs init` | Create starter configuration, history, docs, and ignore entries. | A writable working directory. Never overwrites existing files. |
| `livedocs init --discover-projects` | Discover `.fsproj` files and record them in configuration. | `.fsproj` files exist below the root; benchmarks, probes, and apps may need removing from the list afterward. |
| `livedocs generate-ci [--provider github]` | Generate a GitHub Actions workflow that verifies docs and publishes releases (provider steps spelled out). | GitHub repository, Pages set to "GitHub Actions", default branch `main`, release tags `v<semver>`. Won't overwrite an existing `livedocs.yml`. Other hosts: follow the recipe in [Verify documentation in CI](guides/continuous-integration.md). |

## Authoring and verification

| Command | Result | Assumes |
| --- | --- | --- |
| `livedocs audit [projects...]` | Check modes, coverage, and compilation for every F# block. | Projects built. Does not execute examples. |
| `livedocs test [projects...]` | Audit, then compile every unit and run each `run` block and `transcript`. | Projects built. Executable examples have the same file, network, process, and clock access as the shell. |
| `livedocs generate-tests [projects...]` | Write `tests/FsLiveDocs.SnapshotTests/` with one xUnit case per discovered example. | Projects built. Regenerate after adding, removing, or renaming an example or fence. |
| `livedocs build [projects...]` | Verify and render the current site to `output/`. | Projects built; Node.js on `PATH` (`npx pagefind` builds the search index). |
| `livedocs watch [projects...]` | Verify, rebuild, and serve the site after changes. | Projects built; Node.js on `PATH`; a free TCP port (default `0.0.0.0:5000`); on Linux, enough inotify watches. |

## Releases and history

| Command | Result | Assumes |
| --- | --- | --- |
| `livedocs capture [projects...] --version <v> --output <zip>` | Verify and create an immutable capsule. | Projects built; complete example coverage (no uncovered blocks); `--version` given. |
| `livedocs capture ... --dry-run` | Validate capture and report expected sizes. | As `capture`; writes nothing. |
| `livedocs inspect <zip>` | Verify and describe a capsule. | The zip is a capsule produced by `capture`. |
| `livedocs history-check [--capsule <zip> --version <v>]` | Render + verify the committed history, optionally splicing a local candidate in. | Every capsule the index references is reachable. Writes nothing; no host access. `--capsule` and `--version` come together. |
| `livedocs history-add --version <v> --url <https> --sha256-file <f>` | Record a hosted capsule in the index. | HTTPS URL; the checksum (`--sha256`/`--sha256-file`, or `history.urlPattern` supplies `--url`); `<v>` at or above the compatibility floor and not already present. |
| `livedocs history-add --version <v> --capsule <zip>` | Record a local capsule (offline history builds). | The capsule file exists; checksum computed if not given. |
| `livedocs history-sync <owner/repo> --output <index>` | One-way: discover published capsules and merge them into the index. | GitHub: non-draft Releases with assets `<repo>-<version>-livedocs.zip` and a `sha256:` digest, `api.github.com` reachable, `GH_TOKEN` for private/rate-limited repos. Or `--from "<command>"` / `history.discover` printing `version url sha256` lines. Only adds versions at or above the compatibility floor; never modifies releases. |
| `livedocs build-history <index> --retry 3` | Render every indexed release from its capsule alone. | Every capsule URL in the index is reachable; `--retry` bounds transient download failures. Never compiles the historical source. |
| `livedocs verify-output <index> --output output` | Verify entry points, switcher order, and generated local links. | `build-history` (or `build`) already wrote `output/` for the same index. |
| `livedocs extract [projects...]` | Write legacy loose API and semantic artifacts. | Projects built. Superseded by capsules; use only for external tooling that reads the loose files. |

## Common options

| Option | Use |
| --- | --- |
| `--version <v>` | Set the captured or rendered product version; the release version for `history-add`/`history-check`. |
| `--output <path>` | Set an artifact or index output path. |
| `--url <https>` / `--sha256 <hex>` / `--sha256-file <path>` | Identify a hosted capsule for `history-add`. |
| `--from "<command>"` | `history-sync` discovery command for non-GitHub hosts. |
| `--provider <name>` | `generate-ci` target host (default `github`). |
| `--theme <name>` | Select the initial site theme. |
| `--warn-as-error` | Fail on API documentation quality warnings. |
| `--verbosity <level>` | Set output detail: `warnings` (default), `info`, or `debug`. |
| `--interactive <bool>` | Enable or disable animated, stage-aware progress (default: `true`). |
| `--banner <bool>` | Show or hide the LiveDocs banner (default: `true`). |
| `--host <address>` | Set the preview bind address. |
| `--port <number>` | Set the preview port. |
| `--ignore <names>` | Add watcher directory names to ignore. |

At the default `warnings` level, LiveDocs groups API issues by source file and issue kind, links to configured GitHub source, and prints a concise summary. `info` adds normal progress messages. `debug` expands every issue with its compiler message and remedy, and also lists every audited block and watcher directory. Use `--interactive false` for stable line-oriented logs. Verbosity, interactivity, and the banner are independent. In CI, pass `--interactive false --banner false` to every invocation — see [Verify documentation in CI](guides/continuous-integration.md).

## Fence modes

| Mode | Compilation | Execution | Display |
| --- | --- | --- | --- |
| `fsharp` | Page unit | No | Yes |
| `fsharp prepare` | Page unit | No | Shared setup |
| `fsharp isolated` | Separate unit | No | Yes |
| `fsharp run` | Page unit | Yes | Yes |
| `fsharp transcript` | Transcript runner | Yes | Yes |
| `fsharp no-check reason="..."` | No | No | Syntax only |

## Shortcodes and references

| Syntax | Result |
| --- | --- |
| `{{< snippet id="Name" >}}` | Transclude a marked F# source region. |
| `{{< example id="Name" >}}` | Transclude an XML documentation example. |
| `xref:T:Namespace.Type` | Link to a documented entity. |
| `xref:M:Namespace.Module.member` | Link to a documented member. |
