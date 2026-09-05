# Repository guidance

Before changing release extraction, persisted models, history builds, documentation discovery,
generated verification, or rendering, read and follow
[`dev-docs/RELEASE_ARTIFACT_RULES.md`](dev-docs/RELEASE_ARTIFACT_RULES.md).

Those rules define FsLiveDocs' 1.0 compatibility boundary. In particular, persisted release
artifacts contain renderer-neutral meaning rather than generated HTML or formatter-owned types.

Treat changes to those models as compatibility work. Update schemas, fixtures, migrations, release guidance, and capsule-only tests together.

## Release notes

The version being prepared is declared in [`NEXT_VERSION`](NEXT_VERSION). Its release notes are
[`dev-docs/releases/<version>.md`](dev-docs/releases/); for example, when `NEXT_VERSION` contains
`0.6.0`, update `dev-docs/releases/0.6.0.md`. Verify the expected file with
`scripts/check-release-notes.sh`.
