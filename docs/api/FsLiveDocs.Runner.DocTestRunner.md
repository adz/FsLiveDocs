# DocTestRunner

`DocTestRunner` supports named XML documentation snapshots.

Use `snapshotExampleNames` to enumerate explicitly selected examples. Use `collectSnapshotByName` to execute one example and return its owned snapshot result.

Generated Markdown verification does not call `DocTestRunner` directly. It uses stable `GeneratedVerificationCase` values and `GeneratedVerification.runCase`.
