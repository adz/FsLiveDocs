# FsLiveDocs release process

This document covers publishing FsLiveDocs itself. For publishing a library's documentation with FsLiveDocs, see [`docs/guides/releases.md`](../docs/guides/releases.md).

## Publish FsLiveDocs packages

FsLiveDocs uses `.github/workflows/release.yml`. A tag named `v<semver>` is the only release trigger. The workflow removes the leading `v`, passes that version to every build and pack, verifies the package set, publishes it to NuGet.org, captures the matching documentation capsule, and creates the GitHub release.

The published package set is:

| Package | Purpose |
| --- | --- |
| `FsLiveDocs` | The `livedocs` .NET tool. |
| `FsLiveDocs.Annotations` | The lightweight, `netstandard2.0` compile-time contract for attributes such as `DocScenario`. |

`FsLiveDocs.Annotations` supplies declarative metadata attached to consumer code. Core, Runner, and Renderer remain internal project boundaries and are bundled into the tool package; they are not separate public NuGet packages.

Before the first tag, configure NuGet.org Trusted Publishing:

1. Sign in to NuGet.org and open **Account settings → Trusted publishing**.
2. Add a GitHub Actions policy owned by the NuGet account that will own the packages.
3. Set the GitHub owner to `adz`, repository to `FsLiveDocs`, and workflow path to `.github/workflows/release.yml`.
4. Leave the environment empty because the workflow does not use a GitHub environment.
5. In the GitHub repository, create an Actions variable named `NUGET_USER` containing the NuGet.org username—not an email address.

The workflow requests GitHub's OIDC token and exchanges it through `NuGet/login@v1` for a short-lived publishing credential. No persistent NuGet API key is stored in GitHub. The workflow does not use `--skip-duplicate`: package versions and release tags are immutable, so reusing a published version fails.

Create and push a release tag only from the commit to release:

```bash
git tag -a v0.1.0 -m "FsLiveDocs 0.1.0"
git push origin v0.1.0
```

The version in `Directory.Build.props` is the local-development default. Tagged CI overrides it from the tag and rejects tags that are not stable semantic versions.

## Plan storage capacity

Axial provides a representative large project. On 2026-08-14, its inputs contained 82 Markdown files (317,151 bytes), 8 assets (1,490,449 bytes), 942,360 bytes of API JSON, and 1,302,483 bytes of semantic JSON.

The structured inputs compressed to about 264 KB. Assets therefore dominate an estimated 1.75 MB capsule. Axial's release capture was not published because its dirty source and existing build outputs left 9 of 313 blocks uncompilable.

Treat that refusal as the release gate working correctly. Build a clean release commit before using its final report for retention planning.
