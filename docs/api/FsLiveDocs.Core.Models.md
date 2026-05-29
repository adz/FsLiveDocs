# 🏛️ FsLiveDocs.Core.Models

The blueprint of the documentation engine. This module defines the domain models used to represent symbols, members, examples, and the overall package structure.

## Key Types

- `PackageModel`: The root container for a documentation version.
- `EntityModel`: Represents a Module, Type, Union, or Record.
- `MemberModel`: Represents a function, method, or property.
- `ExampleModel`: Represents an executable code snippet.

## Serialization

The models are designed to be serialized to JSON, allowing them to be stored in the `.livedocs/history` directory and used for versioned documentation.

{{< example id="CreateExample" >}}
