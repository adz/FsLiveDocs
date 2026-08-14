# Repository guidance

Before changing release extraction, persisted models, history builds, documentation discovery,
generated verification, or rendering, read and follow
[`dev-docs/RELEASE_ARTIFACT_RULES.md`](dev-docs/RELEASE_ARTIFACT_RULES.md).

Those rules define FsLiveDocs' 1.0 compatibility boundary. In particular, persisted release
artifacts contain renderer-neutral meaning rather than generated HTML or formatter-owned types.

Treat changes to those models as compatibility work. Update schemas, fixtures, migrations, release guidance, and capsule-only tests together.
