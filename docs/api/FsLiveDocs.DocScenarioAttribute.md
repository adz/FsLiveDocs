# DocScenarioAttribute

`DocScenarioAttribute` marks deterministic setup code for a named XML documentation example. It is the public API of the lightweight `FsLiveDocs.Annotations` library package.

Apply it to a public, parameterless F# function that compiles to a static method. Pass the same unique name to the attribute and to the XML example's `scenario` attribute.

```fsharp
open FsLiveDocs

module CustomerExamples =
    let mutable private currentCustomer = "anonymous"

    [<DocScenario("preferred-customer")>]
    let preparePreferredCustomer () =
        currentCustomer <- "Ada"
```

The constructor accepts the scenario name. The read-only `Name` property exposes that value to FsLiveDocs during documentation discovery.

[Prepare an XML example](../guides/verified-examples.md#prepare-an-xml-example) has a complete example and the execution rules.
