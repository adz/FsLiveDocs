# Acme.Docs sample

`Acme.Docs` is a tiny library used to show what FsLiveDocs can do. It is part of this repository and is not a package you should install.

The `Order` record and module show a normal generated API with checked XML examples. `CustomerContext` shows scenario setup through `DocScenarioAttribute`.

The sample deliberately includes:

- a record and same-named module;
- concise XML member documentation;
- checked FSI transcripts;
- a source snippet reused by a guide;
- deterministic setup for a stateful example.

Its authored pages live in `docs/api/`, just like the annotations package pages. That makes this API a working example of the authoring approach described in [Write API and guide pages](../guides/api-pages.md).
