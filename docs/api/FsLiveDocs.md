# FsLiveDocs annotations

`FsLiveDocs.Annotations` is the small library package that connects documented code to FsLiveDocs. Most projects only need it when an XML example requires scenario setup.

Install it in the library that owns the example:

```bash
dotnet add package FsLiveDocs.Annotations
```

The package contains `DocScenarioAttribute`. It does not bring the CLI, compiler service, or renderer into your library.

The similarly named `FsLiveDocs` package is a .NET tool. Install that through a tool manifest and run it as `dotnet livedocs`; do not add it as a library reference.

[Author and test examples](../guides/verified-examples.md#prepare-an-xml-example) covers the complete workflow. The `DocScenarioAttribute` page contains its generated reference.

The `Acme.Docs` package shown beside this one is a teaching sample from this repository. It is not published for application use.
