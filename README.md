# FsLiveDocs (Atlas)

FsLiveDocs is a verified documentation engine for F#. It ensures that your documentation remains in sync with your code by treating examples as executable tests.

## Features

- **Verified Docstrings:** Automatically extracts and runs code snippets from `/// <example>` tags.
- **Example Transclusion:** Reference code from your source files directly in your Markdown guides using `{{< snippet id="SnippetName" >}}`.
- **Fast Versioning:** Uses metadata snapshots for fast rendering of multiple documentation versions.
- **Modern UI:** Built-in responsive theme using Bootstrap and Prism.js for syntax highlighting.
- **Zero Configuration:** Simple CLI to get started and build your site.

## Getting Started

1.  **Initialize:**
    ```bash
    livedocs init
    ```
2.  **Add Documentation:**
    Create `.md` files in the `docs/` folder. Use frontmatter for metadata.
3.  **Build:**
    ```bash
    livedocs build path/to/your/project.fsproj
    ```

## Project Structure

- `FsLiveDocs.Core`: Content models, symbol normalization, and Markdown/YAML processing.
- `FsLiveDocs.Runner`: The DocTest engine that scaffolds and executes code snippets.
- `FsLiveDocs.Renderer`: Giraffe.ViewEngine-based HTML renderer and site builder.
- `FsLiveDocs.Cli`: Command-line interface.

## Verification

Every snippet in your documentation is a promise. FsLiveDocs guarantees that promise is kept.
```fsharp
/// <example name="AddTest">
/// let result = Math.add 1 2
/// printfn "%d" result
/// // EXPECTED: 3
/// </example>
```
Running `livedocs test` will verify that the output matches the `EXPECTED:` marker.
