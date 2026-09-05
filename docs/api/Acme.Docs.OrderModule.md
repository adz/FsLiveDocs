# Order functions

The `Order` module contains the operations for the sample `Order` record. Keeping behavior beside the type demonstrates how FsLiveDocs distinguishes same-named F# types and modules while presenting both in one API reference.

`create` rejects negative subtotals and returns a validated order. `total` applies a fractional tax rate, and its XML documentation includes a checked transcript.

The `create` source is tagged for transclusion so the deep-reference guide can display the maintained implementation rather than a copied snippet.
