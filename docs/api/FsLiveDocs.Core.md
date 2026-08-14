# FsLiveDocs.Core

`FsLiveDocs.Core` owns the stable domain and persisted artifact boundary.

Use it for:

- renderer-neutral API and documentation models;
- semantic artifact models;
- canonical content discovery and expansion;
- schema and checksum validation;
- deterministic release capsule creation and inspection;
- local and HTTPS history acquisition.

Do not add generated HTML, CSS classes, DOM IDs, or formatter-owned values to persisted Core models.
