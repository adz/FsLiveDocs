# CustomerContext

`CustomerContext` demonstrates deterministic setup for an XML documentation example. It keeps a small amount of sample state so the effect of scenario preparation is visible.

`loadPreferredCustomer` carries `DocScenarioAttribute` and sets the discount fixture. The XML example on `price` names that scenario, so FsLiveDocs runs setup before evaluating the transcript.

Production scenarios should follow the same shape but keep setup fast, deterministic, and local.
