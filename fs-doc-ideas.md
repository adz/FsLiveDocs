# LiveDocs: The "Verified Documentation" Engine for F#


  
**Official Name:** LiveDocs (FsLiveDocs)  
**Status:** Design Phase (v2 - High Detail)

---

## 1. Executive Summary
LiveDocs is a "best-in-class" documentation tool for F# that treats documentation as a **validated artifact of execution**. It replaces the fragmented Hugo/Bash/Script-based documentation pipeline (currently seen in FsFlow) with a unified, pure-F# orchestrator. 

It solves three primary problems:
1. **The "Rotting Examples" Problem:** Documentation examples often drift from the code. LiveDocs runs every snippet as a test.
2. **The "Broken Build" Problem:** Rebuilding 10+ versions of documentation in CI is slow and brittle. LiveDocs uses metadata snapshots.
3. **The "External Dependency" Problem:** Hugo, Go, and Node add friction. LiveDocs is a single `dotnet` tool with zero external requirements.

---

## 2. Core Workflows (The "Killer Features")

### A. Verified Docstrings (The DocTest Engine)
Every code block inside an F# docstring can be marked for verification.

**Logic:**
- **Extraction:** LiveDocs uses `FSharp.Compiler.Service` to parse `/// <example>` tags.
- **Attributes:** Supports metadata like `name` (for test reporting) and `scenario` (for setup).
- **Verification:**
  - Extracts snippet code.
  - Matches the `scenario` to a `[<DocScenario("X")>]` function in the project's test suite to provide Mocks/DI.
  - Generates a temporary `.fsproj` (LiveDocs.Generated.Tests).
  - Executes via `dotnet test` or a custom internal runner.
  - Captures `stdout` and compares it to a `// EXPECTED:` marker inside the docstring.
  - If they don't match, the build fails.

**Refinement from FsFlow:** 
Currently, FsFlow uses `scripts/generate-example-docs.sh` to run entire projects. LiveDocs moves this into the docstring itself, enabling granular verification of every single API example.

### B. The Metadata Snapshot Strategy (Fast Versioning)
Instead of checking out old Git tags and building code in CI, LiveDocs uses a "Build Once, Render Always" approach.

**The Workflow:**
1. **Extraction (at Release):** During the release of `v1.2.0`, CI runs `livedocs extract`.
2. **The Blob:** This produces `v1.2.0.livedocs.json`. This JSON contains:
   - Full API symbol tree (Lifts signatures, docstrings, and location).
   - Captured outputs of all Verified Doctests.
   - Normalized type names (e.g., stripping `FsFlow.`, `Module`, etc., using the logic from `scripts/docgen/Program.fs`).
3. **The History:** These blobs are saved in `.livedocs/history/` in the repo.
4. **The Render:** On every push to `main`, `livedocs build --all` is run. 
   - It builds the current code for the `next` version.
   - It reads the historical JSON blobs for all other versions.
   - It renders **all versions** into HTML using the **latest** UI theme.
   - *Result:* Old documentation versions look modern and have updated search/UI immediately.

### C. Example Transclusion (Snippets)
Reference compiled, checked code from your `examples/` directory directly in your guides.

**Source Code (`examples/Auth.fs`):**
```fsharp
// <snippet:UserAuth>
let auth = flow {
    let! user = User.read
    return user.IsAdmin
}
// </snippet:UserAuth>
```

**Markdown Guide (`docs/managing-dependencies/_index.md`):**
`{{< snippet id="UserAuth" showOutput="true" >}}`

**Motivation:**
Currently, FsFlow uses a manual bash script `scripts/generate-example-docs.sh` to `cat` files into a single `_index.md`. LiveDocs provides a first-class "Snippet Provider" that handles this across any number of files and projects.

---

## 3. Reference Implementation: Learning from FsFlow

LiveDocs will absorb and improve upon the following files in the FsFlow project:

1. **`scripts/docgen/Program.fs` (The Lifter):**
   - **Absorb:** The `normalize` and `cleanName` functions for human-readable signatures.
   - **Absorb:** The `ApiDocInput.FromFile` logic to load multiple DLLs.
   - **Refine:** Instead of rendering HTML/Markdown strings in the program, it will populate a `PackageModel` that is serialized to JSON.

2. **`scripts/populate-hugo-content.sh` (The Site Prep):**
   - **Replace:** The `upsert_frontmatter` bash function will be replaced by a pure F# YAML frontmatter parser.
   - **Replace:** The complex `find` and `cp` operations will be replaced by a `FileSystem` provider that builds a virtual `ContentTree`.

3. **`scripts/generate-example-docs.sh` (The Runner):**
   - **Replace:** The hardcoded `dotnet run` calls will be replaced by the "Verified Docs" runner, which handles project scaffolding and output redirection automatically.

4. **`site/hugo.toml` (The UI):**
   - **Replace:** The Docsy theme configuration will be replaced by the `LiveDocs.Theme.Default` package, implemented using `Giraffe.ViewEngine`.

---

## 4. Implementation Guide for AI Agent

To implement LiveDocs, follow these phases:

### Phase 1: The Content Model & Core
- Create `FsLiveDocs.Core`.
- Define the `PackageModel`:
  ```fsharp
  type MemberModel = { 
      Id: string; Name: string; Signature: string; 
      SummaryHtml: string; RemarksHtml: string; 
      Examples: ExampleModel list; Location: SourceLink 
  }
  type PackageModel = { Version: string; Entities: MemberModel list }
  ```
- Port the FsFlow symbol normalization logic (`scripts/docgen/Program.fs`) into a `SymbolLister` module.

### Phase 2: The DocTest Runner
- Create `FsLiveDocs.Runner`.
- Implement a regex-based extractor for `/// <example>` tags.
- Implement the "Temporary Project" logic:
  - Generate a `livedocs.tmp.fsproj`.
  - Add references to the target DLLs.
  - Create a `Program.fs` that sequences the **Scenario Setup** + **Snippet**.
- Implement `Capture.stdout`:
  ```fsharp
  use sw = new StringWriter()
  Console.SetOut(sw)
  // Run snippet
  let output = sw.ToString()
  ```

### Phase 3: The Virtual Content Tree
- Implement a `ContentProvider` that:
  - Scans `docs/**/*.md`.
  - Parses frontmatter for `title`, `weight`, `type`.
  - Resolves `{{< snippet >}}` and `{{< example >}}` tags.
  - Handles "X-Refs" (`xref:M:Namespace.Type.Method`) by looking up the `PackageModel`.

### Phase 4: The Renderer (The UI)
- Use `Giraffe.ViewEngine` to build a "Docsy-Lite" layout.
- **Critical Components:**
  - `Navbar`: Top navigation with version dropdown.
  - `Sidebar`: Nested accordion based on folder structure and weights.
  - `ApiCard`: A clean block for signatures, parameters, and returns.
  - `CodeBlock`: Includes syntax highlighting (via `Prism.js` or `Shiki`) and "Copy" button.

### Phase 5: The CLI
- Build using `Argu` or `Spectre.Console`.
- Commands:
  - `init`: Scaffolds `.livedocs/` and `livedocs.fsx`.
  - `extract`: Builds the JSON blob for the current version.
  - `test`: Runs all verified docstrings and snippets.
  - `build`: Renders the final static site to `output/`.
  - `watch`: Starts an ASP.NET Core dev server with file watching.

---

## 5. Technical Specifications

### Asset Pipeline
- **Images:** Automatic `WebP` conversion and lazy-load injection.
- **Icons:** Built-in `Lucide` icon provider.
- **Search:** Generates a `pagefind` index. Pagefind is a highly efficient static search tool that LiveDocs will bundle as a post-build step.

### CI/CD Integration
- **GitHub Actions:** Provides a standard template that:
  1. Checks out code.
  2. Runs `dotnet livedocs test`.
  3. Runs `dotnet livedocs build --all`.
  4. Deploys `output/` to GitHub Pages.
- **LLMS Integration:** Automatically generates an `llms.txt` at the root of the site for AI consumption.

---

## 6. Success Metrics
1. **Zero-Config Build:** A new project can run `livedocs init` and `livedocs build` and have a working site in < 60 seconds.
2. **Version Independence:** A change to the documentation theme updates all historical versions without recompiling old code.
3. **Execution Confidence:** Every example on the website is guaranteed to compile and produce the shown output.
