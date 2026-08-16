---
title: Command reference
---

# Command reference

Run commands from the repository root. Build documented projects before commands that extract or verify APIs. Project arguments are optional. FsLiveDocs uses explicit arguments first, the `projects` list in `.livedocs/config.json` second, and automatic discovery last.

## Repository setup

| Command | Result |
| --- | --- |
| `livedocs init` | Create starter configuration, history, docs, and ignore entries. |
| `livedocs init --discover-projects` | Discover `.fsproj` files and record them in configuration. |
| `livedocs generate-ci` | Generate a GitHub Actions workflow that verifies documentation and publishes release capsules. |

## Authoring and verification

| Command | Result |
| --- | --- |
| `livedocs audit [projects...]` | Validate modes, coverage, and compilation. |
| `livedocs test [projects...]` | Audit and run explicitly executable examples. |
| `livedocs generate-tests [projects...]` | Generate stable xUnit and Verify cases. |
| `livedocs build [projects...]` | Verify and render the current site to `output/`. |
| `livedocs watch [projects...]` | Verify, rebuild, and serve the site after changes. |

## Releases and history

| Command | Result |
| --- | --- |
| `livedocs capture [projects...] --version <v> --output <zip>` | Verify and create an immutable capsule. |
| `livedocs capture ... --dry-run` | Validate capture and report expected sizes. |
| `livedocs inspect <zip>` | Verify and describe a capsule. |
| `livedocs history-add <v> --capsule <zip>` | Add a local capsule and calculated checksum. |
| `livedocs history-add <v> --url <https-url> --sha256 <hash>` | Add a remote immutable capsule. |
| `livedocs build-history <index>` | Verify and render every indexed release. |
| `livedocs extract [projects...]` | Write legacy loose API and semantic artifacts. |

## Common options

| Option | Use |
| --- | --- |
| `--version <v>` | Set the captured or rendered product version. |
| `--output <path>` | Set an artifact or index output path. |
| `--theme <name>` | Select the initial site theme. |
| `--warn-as-error` | Fail on API documentation quality warnings. |
| `--verbosity <level>` | Set output detail: `warnings` (default), `info`, or `debug`. |
| `--interactive <bool>` | Enable or disable animated, stage-aware progress (default: `true`). |
| `--banner <bool>` | Show or hide the LiveDocs banner (default: `true`). |
| `--host <address>` | Set the preview bind address. |
| `--port <number>` | Set the preview port. |
| `--ignore <names>` | Add watcher directory names to ignore. |

At the default `warnings` level, LiveDocs groups API issues by source file and issue kind, links to configured GitHub source, and prints a concise summary. `info` adds normal progress messages. `debug` expands every issue with its compiler message and remedy, and also lists every audited block and watcher directory. Use `--interactive false` for stable line-oriented logs. Verbosity, interactivity, and the banner are independent.

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
