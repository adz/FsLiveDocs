---
title: Release history
---

# Release history

Release history is an ordered index of immutable capsules. FsLiveDocs renders every indexed version with the current renderer while preserving the API, content, and compiler-derived meaning captured at release time.

## History index

A history index has this shape:

```json
{
  "SchemaVersion": 1,
  "CurrentVersion": "1.4.0",
  "Entries": [
    {
      "Version": "1.4.0",
      "CapsulePath": null,
      "CapsuleUrl": "https://example.com/releases/1.4.0/docs.zip",
      "CapsuleSha256": "<64 lowercase hexadecimal characters>"
    }
  ]
}
```

Each entry declares exactly one local path or HTTPS URL. Versions are unique semantic versions and appear newest first. `CurrentVersion` must equal the newest entry.

`history-add` rejects an existing version. Published history entries are immutable.

## Hosted entries

The normal publication flow uploads a capsule, then records its durable URL and checksum:

```bash
livedocs history-add --version 1.4.0 \
  --url https://example.com/releases/1.4.0/docs.zip \
  --sha256-file artifacts/Example-1.4.0-livedocs.zip.sha256
```

A `history.urlPattern` may provide the URL. Supported placeholders are `{version}`, `{name}`, and `{tag}`, where the tag is `v{version}`.

Local entries use `--capsule <path>` and are intended for offline history builds.

## Synchronization

`history-sync` discovers published capsules and merges missing entries into an index. It never edits remote releases or replaces existing metadata.

For GitHub, the source is an `owner/repo` and release assets must follow the expected naming convention with a published SHA-256 digest.

Other hosts may provide a command that prints `version url sha256` lines through `--from` or `history.discover`.

The oldest existing index entry is the compatibility floor. Synchronization does not import older capsule formats beneath that floor.

## Remote acquisition

Remote capsules require HTTPS and an expected SHA-256.

Downloads go to a temporary file. FsLiveDocs verifies the checksum before moving the file to:

```text
.livedocs/releases/<sha256>.livedocs.zip
```

A cached file is verified before reuse. `build-history --retry <count>` retries transient HTTP and filesystem failures with bounded backoff.

Checksum mismatches are deterministic failures. They are not retried and never enter the cache.

## Historical rendering

```bash
livedocs build-history .livedocs/history.json --retry 3
```

Each capsule is verified and materialized in a temporary directory. The current renderer builds the site from stored API, semantic, content, and asset data.

Historical rendering never restores, loads, or compiles the old project. Temporary content is removed after the build.

The current version appears at the site root. Older versions appear under `history/<version>/`.

A version switch keeps the current page and documentation set when that identity exists in the target release. It otherwise falls back to that set's API or root, then to the site root.

Historical releases use the documentation sets captured in their own capsule, not the current repository configuration.

## Candidate checks

```bash
livedocs history-check \
  --capsule artifacts/Example-1.4.0-livedocs.zip \
  --version 1.4.0
```

This temporarily adds the local candidate to the committed index, renders every version, and runs output checks. It does not change the index or upload anything.

With no candidate arguments, `history-check` validates the committed history as it stands.

## Output verification

```bash
livedocs verify-output .livedocs/history.json --output output
```

Verification requires every version entry point, checks generated local `href` and `src` targets, and confirms that the root version switcher lists releases newest first.

The generated search directory is excluded from local-link checks.

## Legacy manifests

`build-history` accepts the earlier local manifest format with separate API, semantic, checksum, and documentation-tree paths.

That path exists for migration. New releases use complete capsules.
