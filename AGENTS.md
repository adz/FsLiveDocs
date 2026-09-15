# Agent notes

Read [`CLAUDE.md`](CLAUDE.md) and [`dev-docs/RELEASE_PROCESS.md`](dev-docs/RELEASE_PROCESS.md) first.
This file records what past releases taught us.

## Releasing FsLiveDocs

### The release workflow commits to `main`

Pushing a `v<semver>` tag runs `release.yml`. After the GitHub Release is created, the workflow
runs `history-sync` and pushes a `github-actions[bot]` commit, `Record <version> in the release
history`, which updates `.livedocs/history.json` on `main`.

- Every release therefore leaves local `main` one commit behind `origin/main`. Run
  `git fetch --tags && git pull --rebase` before starting release work. Otherwise the next push is
  rejected, or a local change to `.livedocs/history.json` conflicts with the bot's commit.
- These bot commits are expected. They are not repairs. Do not edit `.livedocs/history.json` by
  hand to fake them.

### Version and notes must change before the tag

- `NEXT_VERSION` and `dev-docs/releases/<version>.md` must be updated in a commit before tagging.
  Releases 0.7.1 and 0.6.1 bumped them in the same commit as the fix and tagged that commit.
- Never add notes to a version that is already tagged. Check with `git tag --list 'v*'`. If
  `NEXT_VERSION` names a tagged version, bump it first.
- Run `scripts/check-release-notes.sh` before committing.
- Push `main`, wait for CI and Pages on that commit to go green, then tag that exact commit:
  `git tag -a vX.Y.Z -m "FsLiveDocs X.Y.Z" && git push origin vX.Y.Z`. A tag must never move
  after NuGet accepts the version.

### Past failures and their fixes

- **0.5.0 (run 33952968766):** the tag build, GitHub Release and history commit all succeeded.
  The job then failed with `Pages run for <sha> was not observed.`, because a push made with
  `GITHUB_TOKEN` does not trigger `pages.yml`. The fix came in follow-up commits on `main`:
  - `95f8ca3` Recover 0.5.0 through trusted release workflow
  - `8fea0fa` Keep release recovery manually dispatched
  - `6e6f3e2` Document explicit Pages release dispatch

  `release.yml` now dispatches Pages explicitly. If publication fails after the GitHub Release
  exists, manually dispatch `release.yml` with the existing version. Do not delete the release or
  re-tag.
- **0.7.0:** capturing the documentation capsule for a large consumer (Reified) ran out of memory
  at 9.8 GB. Three commits on `main` reduced memory use before the tag was cut:
  - `7593b59` Bound release capture memory
  - `ff5a39a` Reduce cold capture compiler retention
  - `f586808` Stream semantic extraction by page batch

  Release capture is memory-sensitive. For changes to capture, analysis or snapshot execution,
  run a capture on a large consumer before tagging, not just the unit tests.

### Consumer repos can show stale diagnostics

Consumers such as Axial pin the tool in `.config/dotnet-tools.json`. Audit results depend on the
consumer's built assemblies. Stale builds under `artifacts/bin` can produce misleading results,
such as `unnamed-parameter` warnings at shifted lines, even after the source is fixed. Rebuild the
consumer before treating a diagnostic as an FsLiveDocs bug. To test an unreleased FsLiveDocs
against a consumer, use `scripts/release-local-tool.sh <consumer-repo>` or
`dotnet run --project src/FsLiveDocs.Cli -- <command>` from the consumer's directory.
