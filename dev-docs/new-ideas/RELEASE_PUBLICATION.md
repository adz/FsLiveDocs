# Host-agnostic release publication

## Status

Accepted plan, in progress. Records the design; operational guidance moves to
`docs/guides/releases.md` and `docs/guides/continuous-integration.md` as it lands.

## Problem

Two parts of the release workflow are shaped around GitHub:

- **discovery** — `history-sync <owner/repo>` calls `api.github.com` and expects
  assets named `<repo>-<version>-livedocs.zip` with GitHub's `digest` field;
- **publication** — no CLI command; every workflow hand-rolls `gh release create`
  and never commits `.livedocs/history.json` back, so `history-sync` rediscovers
  the whole history on each build.

A GitLab user, a self-hoster, or anyone who does not trust the tool to run
provider commands has no clean path.

## Principle

The CLI owns the **capsule**, the **history index**, and **verification**. It never
uploads an asset, calls a provider API for writes, or moves a Git ref. Those are
explicit, user-authored CI steps.

`history.json` stays `{ Version, CapsulePath | CapsuleUrl, CapsuleSha256 }` — no
provider fields, ever (`RELEASE_ARTIFACT_RULES.md`). Provider configuration lives
in `.livedocs/config.json`.

## Model

`.livedocs/history.json` is a normal committed file that gains one entry per
release. It is written only by `history add` (directly, or as the last step of a
future flow) and committed back by a CI step the user controls.

### Release flow (GitHub shown; the two provider lines are user-authored)

```
livedocs capture --version $V --output artifacts/$NAME-$V-livedocs.zip
livedocs history check --capsule artifacts/$NAME-$V-livedocs.zip --version $V
# provider: gh release create v$V artifacts/$NAME-$V-livedocs.zip{,.report.json} --verify-tag ...
livedocs history add --version $V --sha256-file artifacts/$NAME-$V-livedocs.zip.sha256
livedocs history check
# provider: git commit .livedocs/history.json and push to the default branch (or open a PR)
```

The Pages job then builds from the committed index with `build-history` +
`verify-output`. Swap the two provider lines for `glab`, `aws s3 cp`, `scp`, etc.

## CLI surface

| Command | Change |
| --- | --- |
| `capture` | also writes `<capsule>.sha256` (bare lowercase hex + newline) beside the zip and `.report.json`. |
| `history check` | **new.** Renders and verifies the committed index — optionally with a local `--capsule`/`--version` candidate spliced in — into a temp directory. Read-only; never writes the index. |
| `history add` | gains `--sha256-file <path>`. When `--url` is omitted and `history.urlPattern` is configured, the URL is derived from the pattern. |
| `history sync` | discovery becomes pluggable: built-in `github` mode (positional `owner/repo`) or `--from command:"<lister>"` / `history.discover`, emitting `version url sha256` lines. Optional now — backfill and migration only. |

### `.livedocs/config.json`

```json
{
  "history": {
    "urlPattern": "https://github.com/adz/Reified/releases/download/v{version}/{name}-{version}-livedocs.zip",
    "discover": "gh release list ..."
  }
}
```

`urlPattern` placeholders: `{version}`, `{name}` (capsule stem's package part),
`{tag}` (`v{version}`). It is a plain format string the user owns — the CLI has no
GitHub knowledge.

## generate-ci

`generate-ci --provider github` (only value for now) emits a workflow whose
provider steps (`gh release create`, `git commit`/`push`) are written out
explicitly and interleaved with the `livedocs` calls, so the workflow is readable
and every provider action is visible.

## Non-goals

- Built-in GitLab / S3 / Azure adapters. The publisher contract plus
  `--to command:` / `--from command:` cover them.
- A `history publish` command that shells to `gh`.
- Any provider field in the persisted history index.
