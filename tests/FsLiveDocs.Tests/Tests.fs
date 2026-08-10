namespace FsLiveDocs.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Runner
open FsLiveDocs.Renderer
open Newtonsoft.Json

module HistoryTests =

    [<Fact>]
    let ``semantic artifact round trips and validates tooltip indexes`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".semantic.json")
        let block = {
            Id = "guide.md#fsharp-0"; SourceHash = "source"; ContextHash = "context"
            Lines = [ { Tokens = [ { Text = "value"; Kind = SemanticTokenKind.Identifier; Tooltip = Some 0 } ] } ]
            Tooltips = [ { Signature = Some "value: int"; Documentation = Some "A value."; Sections = []; Footer = None } ]
            Diagnostics = []
        }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [ { SourcePath = "guide.md"; Blocks = [ block ] } ] }
        File.WriteAllText(path, JsonConvert.SerializeObject(artifact, Formatting.Indented, Serialization.jsonSettings))
        let loaded = History.loadSemanticArtifact (History.sha256 path) path
        Assert.Equal("value", loaded.Pages.Head.Blocks.Head.Lines.Head.Tokens.Head.Text)

        let invalid = { artifact with Pages = [ { SourcePath = "guide.md"; Blocks = [ { block with Lines = [ { Tokens = [ { Text = "bad"; Kind = Identifier; Tooltip = Some 2 } ] } ] } ] } ] }
        File.WriteAllText(path, JsonConvert.SerializeObject(invalid, Formatting.Indented, Serialization.jsonSettings))
        let error = Assert.Throws<InvalidOperationException>(fun () -> History.loadSemanticArtifact (History.sha256 path) path |> ignore)
        Assert.Contains("invalid tooltip index", error.Message)

    [<Fact>]
    let ``unknown future semantic classifications degrade to plain text`` () =
        let json = "{\"Text\":\"future\",\"Kind\":\"FutureClassification\",\"Tooltip\":null}"
        let token = JsonConvert.DeserializeObject<SemanticToken>(json, Serialization.jsonSettings)
        Assert.Equal(SemanticTokenKind.PlainText, token.Kind)

    [<Fact>]
    let ``loadArtifact verifies checksum schema and version`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        let package : PackageModel = { Version = "1.2.3"; Entities = []; Scenarios = []; Packages = [] }
        let artifact : ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
        File.WriteAllText(path, JsonConvert.SerializeObject(artifact, Serialization.jsonSettings))

        let loaded = History.loadArtifact "1.2.3" (History.sha256 path) path
        Assert.Equal("1.2.3", loaded.Version)

        let error = Assert.Throws<InvalidOperationException>(fun () -> History.loadArtifact "1.2.3" (String.replicate 64 "0") path |> ignore)
        Assert.Contains("checksum mismatch", error.Message)

    [<Fact>]
    let ``loadManifest requires an entry for the current version`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        let manifest : HistoryManifest = {
            SchemaVersion = History.ManifestSchemaVersion
            CurrentVersion = "2.0.0"
            Entries = [ { Version = "1.0.0"; ModelPath = "model.json"; ModelSha256 = "checksum"; SemanticPath = None; SemanticSha256 = None; DocsPath = "docs" } ]
        }
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Serialization.jsonSettings))

        let error = Assert.Throws<InvalidOperationException>(fun () -> History.loadManifest path |> ignore)
        Assert.Contains("has no manifest entry", error.Message)

    [<Fact>]
    let ``semantic manifest fields must be paired`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        let manifest : HistoryManifest = {
            SchemaVersion = History.ManifestSchemaVersion
            CurrentVersion = "1.0.0"
            Entries = [ { Version = "1.0.0"; ModelPath = "model.json"; ModelSha256 = "checksum"; SemanticPath = Some "semantic.json"; SemanticSha256 = None; DocsPath = "docs" } ]
        }
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Serialization.jsonSettings))
        let error = Assert.Throws<InvalidOperationException>(fun () -> History.loadManifest path |> ignore)
        Assert.Contains("semanticPath and semanticSha256 together", error.Message)

    [<Fact>]
    let ``legacy manifest without semantic fields remains valid`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        File.WriteAllText(path, """{"SchemaVersion":1,"CurrentVersion":"1.0.0","Entries":[{"Version":"1.0.0","ModelPath":"model.json","ModelSha256":"checksum","DocsPath":"docs"}]}""")
        let manifest, entries = History.loadManifest path
        Assert.Equal("1.0.0", manifest.CurrentVersion)
        let entry, _, _ = Assert.Single(entries)
        Assert.Equal(None, entry.SemanticPath)
        Assert.Equal(None, entry.SemanticSha256)

module SymbolListerTests =

    [<Fact>]
    let ``Placeholder for new SymbolLister tests`` () =
        // We will add new tests as we verify the FSharp.Formatting integration
        Assert.True(true)

    [<Fact>]
    let ``extractExamples marks transcript and explicit snapshot examples`` () =
        let xml =
            """
            <example name="Transcript">
            > let x = 1;;
            val x: int = 1
            </example>
            <code name="Explicit" language="fsharp" data-livedocs="snapshot">
            let value = 42
            </code>
            """

        let examples = SymbolLister.extractExamples xml
        Assert.Equal(2, examples.Length)
        Assert.True(List.item 0 examples |> fun ex -> ex.IsSnapshotTest)
        Assert.True(List.item 1 examples |> fun ex -> ex.IsSnapshotTest)

    [<Fact>]
    let ``merge removes empty synthetic Default namespace`` () =
        let child = { Id = "Default.Sample"; Name = "Sample"; Kind = EntityKind.Module; SummaryHtml = ""; Members = []; Examples = []; Entities = [] }
        let defaultNamespace = { Id = "Default"; Name = "Default"; Kind = EntityKind.Namespace; SummaryHtml = ""; Members = []; Examples = []; Entities = [ child ] }
        let package = SymbolLister.merge [ { Version = "1.0"; Entities = [ defaultNamespace; child ]; Scenarios = []; Packages = [ { Name = "Example.Package"; EntityIds = [ child.Id ] } ] } ]

        let onlyEntity = Assert.Single(package.Entities)
        Assert.Equal("Default.Sample", onlyEntity.Id)
        Assert.Equal("Sample", onlyEntity.Name)
        Assert.Equal("Example.Package", Assert.Single(package.Packages).Name)

    [<Fact>]
    let ``merge combines entities contributed to the same namespace by multiple packages`` () =
        let coreChild = { Id = "Example.CoreFlow"; Name = "CoreFlow"; Kind = EntityKind.Module; SummaryHtml = ""; Members = []; Examples = []; Entities = [] }
        let satelliteChild = { Id = "Example.Http"; Name = "Http"; Kind = EntityKind.Module; SummaryHtml = ""; Members = []; Examples = []; Entities = [] }
        let coreRoot = { Id = "Example"; Name = "Example"; Kind = EntityKind.Namespace; SummaryHtml = ""; Members = []; Examples = []; Entities = [ coreChild ] }
        let satelliteRoot = { Id = "Example"; Name = "Example"; Kind = EntityKind.Namespace; SummaryHtml = ""; Members = []; Examples = []; Entities = [ satelliteChild ] }
        let model name root child =
            { Version = "1.0"; Entities = [ root; child ]; Scenarios = []; Packages = [ { Name = name; EntityIds = [ root.Id; child.Id ] } ] }

        let merged = SymbolLister.merge [ model "Example.Core" coreRoot coreChild; model "Example.Http" satelliteRoot satelliteChild ]
        let root = Assert.Single(merged.Entities)

        Assert.Equal<string list>([ "Example.CoreFlow"; "Example.Http" ], root.Entities |> List.map _.Id |> List.sort)

module ContentProviderTests =

    let private emptyPackage : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }

    [<Fact>]
    let ``canonical discovery assigns stable ids modes and normalized hashes`` () =
        let markdown = "```fsharp prepare\r\nopen System\r\n```\r\n```fsharp isolated\r\nlet answer = 42\r\n```"
        let blocks = DocumentationDiscovery.discoverMarkdown "docs\\start.md" None markdown

        Assert.Equal<string list>([ "docs/start.md#fsharp-0"; "docs/start.md#fsharp-1" ], blocks |> List.map _.Id)
        Assert.Equal(Prepare, blocks.[0].Mode)
        Assert.Equal(Isolated, blocks.[1].Mode)
        Assert.DoesNotContain("\r", blocks.[0].ExpandedSource)

    [<Fact>]
    let ``no-check requires a reason and contradictory modes fail discovery`` () =
        let missing = Assert.Throws<InvalidOperationException>(fun () -> DocumentationDiscovery.discoverMarkdown "guide.md" None "```fsharp no-check\nx\n```" |> ignore)
        Assert.Contains("requires a non-empty reason", missing.Message)
        let contradictory = Assert.Throws<InvalidOperationException>(fun () -> DocumentationDiscovery.discoverMarkdown "guide.md" None "```fsharp run isolated\nx\n```" |> ignore)
        Assert.Contains("Contradictory", contradictory.Message)

    [<Fact>]
    let ``verification compiles page and isolated units but only executes explicit modes`` () =
        let markdown = "```fsharp\nlet a = 1\n```\n```fsharp run\nprintfn \"run\"\n```\n```fsharp isolated\nlet a = 2\n```\n```fsharp transcript\n> 1 + 1;;\nval it: int = 2\n```\n```fsharp no-check reason=\"pseudocode\"\n...\n```"
        let blocks = DocumentationDiscovery.discoverMarkdown "guide.md" None markdown
        let cases = DocumentationDiscovery.verificationCases "sample.fsproj" "" blocks

        Assert.Equal(4, cases.Length)
        Assert.Equal(2, cases |> List.choose (function Compile unit -> Some unit | _ -> None) |> List.length)
        Assert.Equal(1, cases |> List.choose (function Execute block -> Some block | _ -> None) |> List.length)
        Assert.Equal(1, cases |> List.choose (function ExecuteTranscript block -> Some block | _ -> None) |> List.length)

    [<Fact>]
    let ``snippet modes and example transcripts survive canonical expansion`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        File.WriteAllText(Path.Combine(root, "Sample.fs"), "// <snippet:Partial>\nmissing ...\n// </snippet:Partial>")
        let example = ExampleModel.Create("Session", "> 1 + 1;;\nval it: int = 2", Some "val it: int = 2", None)
        let memberModel = { Id = "M"; Name = "M"; Signature = ""; Parameters = []; ReturnType = ""; SummaryHtml = ""; RemarksHtml = ""; Examples = [ example ]; Location = { File = ""; Line = 1 } }
        let entity = { Id = "E"; Name = "E"; Kind = EntityKind.Module; SummaryHtml = ""; Members = [ memberModel ]; Examples = []; Entities = [] }
        let package = { emptyPackage with Entities = [ entity ] }
        let expanded =
            ContentProvider.resolveSnippets
                "{{< snippet id=\"Partial\" mode=\"no-check\" reason=\"Excerpt\" >}}\n{{< example id=\"Session\" >}}"
                root package ""
        let blocks = DocumentationDiscovery.discoverMarkdown "guide.md" None expanded
        Assert.Equal(NoCheck "Excerpt", blocks.[0].Mode)
        Assert.Equal(Transcript, blocks.[1].Mode)

    [<Fact>]
    let ``frontmatter selects a documentation project`` () =
        let parsed = ContentProvider.parseFrontMatter "---\ntitle: Browser\nproject: src/Browser/Browser.fsproj\ntargetFramework: net8.0\nplatform: dotnet\n---\nBody"
        Assert.Equal(Some "src/Browser/Browser.fsproj", parsed |> Option.bind (fun (metadata, _) -> metadata.Project))
        Assert.Equal(Some "net8.0", parsed |> Option.bind (fun (metadata, _) -> metadata.TargetFramework))
        Assert.Equal(Some "dotnet", parsed |> Option.bind (fun (metadata, _) -> metadata.Platform))

    [<Fact>]
    let ``persisted semantic records render accessible encoded tooltips and reject stale source`` () =
        let markdown = "```fsharp\nlet value = List.head [ \"<safe>\" ]\n```"
        let discovered = DocumentationDiscovery.discoverMarkdown "guide.md" None markdown |> List.head
        let tooltip = { Signature = Some "val value: string"; Documentation = Some "Returns <content>."; Sections = []; Footer = Some "Sample" }
        let semantic = {
            Id = discovered.Id
            SourceHash = discovered.SourceHash
            ContextHash = DocumentationDiscovery.contextHash "" [ discovered ]
            Lines = [ { Tokens = [ { Text = discovered.ExpandedSource; Kind = SemanticTokenKind.Identifier; Tooltip = Some 0 } ] } ]
            Tooltips = [ tooltip ]
            Diagnostics = []
        }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [ { SourcePath = "guide.md"; Blocks = [ semantic ] } ] }
        let html = SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact } "guide.md" markdown
        Assert.Contains("aria-describedby=", html)
        Assert.Contains("&lt;safe&gt;", html)
        Assert.Contains("Returns &lt;content&gt;.", html)
        Assert.DoesNotContain("Returns <content>.", html)
        let error = Assert.Throws<InvalidOperationException>(fun () -> SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact } "guide.md" (markdown.Replace("value", "changed")) |> ignore)
        Assert.Contains("source hash mismatch", error.Message)

    [<Fact>]
    let ``persisted semantic records reject changed preparation context and missing blocks`` () =
        let markdown = "```fsharp prepare\nopen System\n```\n\n```fsharp\nlet value = DateTime.UnixEpoch\n```"
        let blocks = DocumentationDiscovery.discoverMarkdown "guide.md" None markdown
        let displayed = blocks.[1]
        let semantic = {
            Id = displayed.Id; SourceHash = displayed.SourceHash
            ContextHash = DocumentationDiscovery.contextHash "" blocks
            Lines = [ { Tokens = [ { Text = displayed.ExpandedSource; Kind = Identifier; Tooltip = None } ] } ]
            Tooltips = []; Diagnostics = []
        }
        let preparation = {
            Id = blocks.[0].Id; SourceHash = blocks.[0].SourceHash
            ContextHash = DocumentationDiscovery.contextHash "" blocks
            Lines = [ { Tokens = [ { Text = blocks.[0].ExpandedSource; Kind = Identifier; Tooltip = None } ] } ]
            Tooltips = []; Diagnostics = []
        }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [ { SourcePath = "guide.md"; Blocks = [ preparation; semantic ] } ] }
        let changedPreparation = markdown.Replace("open System", "open System.IO")
        let contextError =
            Assert.Throws<InvalidOperationException>(fun () ->
                SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact } "guide.md" changedPreparation |> ignore)
        Assert.Contains("source hash mismatch", contextError.Message)

        let missingArtifact = { artifact with Pages = [ { SourcePath = "guide.md"; Blocks = [] } ] }
        let missingError =
            Assert.Throws<InvalidOperationException>(fun () ->
                SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some missingArtifact } "guide.md" markdown |> ignore)
        Assert.Contains("missing block", missingError.Message)

    [<Fact>]
    let ``prepare blocks render as inspectable shared setup`` () =
        let markdown = "```fsharp prepare\nopen System\n```\n\n```fsharp\nlet value = DateTime.UnixEpoch\n```"
        let blocks = DocumentationDiscovery.discoverMarkdown "guide.md" None markdown
        let semantic (block: DocumentationBlock) = {
            Id = block.Id; SourceHash = block.SourceHash
            ContextHash = DocumentationDiscovery.contextHash "" blocks
            Lines = [ { Tokens = [ { Text = block.ExpandedSource; Kind = Identifier; Tooltip = None } ] } ]
            Tooltips = []; Diagnostics = []
        }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [ { SourcePath = "guide.md"; Blocks = blocks |> List.map semantic } ] }

        let html = SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact } "guide.md" markdown

        Assert.Contains("<details class=\"livedocs-shared-setup", html)
        Assert.Contains("Shared setup", html)
        Assert.Contains("open System", html)

    [<Fact>]
    let ``configured repository prelude is rendered and participates in context validation`` () =
        let markdown = "```fsharp\nlet value = DateTime.UnixEpoch\n```"
        let block = DocumentationDiscovery.discoverMarkdown "guide.md" None markdown |> List.head
        let prelude = "open System"
        let semantic = {
            Id = block.Id; SourceHash = block.SourceHash; ContextHash = DocumentationDiscovery.contextHash prelude [ block ]
            Lines = [ { Tokens = [ { Text = block.ExpandedSource; Kind = Identifier; Tooltip = None } ] } ]; Tooltips = []; Diagnostics = [] }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = prelude; Pages = [ { SourcePath = "guide.md"; Blocks = [ semantic ] } ] }

        let html = SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact; Prelude = artifact.Prelude } "guide.md" markdown

        Assert.Contains("Repository F# setup", html)
        Assert.Contains("<span class=\"tok-keyword\">open</span>", html)
        Assert.Contains(">System</span>", html)

    [<Fact>]
    let ``no-check F sharp fences use the shared lexical renderer when an artifact is present`` () =
        let markdown = "```fsharp no-check reason=\"Illustrative\"\nlet incomplete =\n```"
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [] }
        let formatted = SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact } "guide.fsx" markdown

        Assert.Contains("livedocs-code livedocs-lexical-code", formatted)
        Assert.Contains("<span class=\"tok-keyword\">let</span>", formatted)
        Assert.DoesNotContain("```fsharp", formatted)

    [<Fact>]
    let ``semantic and lexical F sharp blocks share one code frame contract`` () =
        let markdown = "```fsharp\nlet checkedValue = 1\n```\n\n```fsharp no-check reason=\"Illustrative\"\nlet illustrativeValue = 2\n```"
        let blocks = DocumentationDiscovery.discoverMarkdown "guide.md" None markdown
        let checkedBlock = blocks.[0]
        let semantic = {
            Id = checkedBlock.Id
            SourceHash = checkedBlock.SourceHash
            ContextHash = DocumentationDiscovery.contextHash "" [ checkedBlock ]
            Lines = [ { Tokens = [ { Text = "let"; Kind = Keyword; Tooltip = None }; { Text = " checkedValue = 1"; Kind = PlainText; Tooltip = None } ] } ]
            Tooltips = []
            Diagnostics = []
        }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [ { SourcePath = "guide.md"; Blocks = [ semantic ] } ] }

        let html = SemanticCode.formatFences { SemanticCode.defaults with Artifact = Some artifact } "guide.md" markdown

        Assert.Equal(2, Regex.Matches(html, "<pre class=\"code-frame\"><code class=\"language-fsharp\">").Count)
        Assert.Equal(2, Regex.Matches(html, "<span class=\"tok-keyword\">let</span>").Count)
        Assert.Contains("livedocs-code livedocs-semantic-code", html)
        Assert.Contains("livedocs-code livedocs-lexical-code", html)

    [<Fact>]
    let ``semantic formatting without a release artifact is syntax-only`` () =
        let markdown = "```fsharp\nlet answer = 42\n```"
        Assert.Equal(markdown, SemanticCode.formatFences SemanticCode.defaults "guide.md" markdown)

    [<Fact>]
    let ``outputPathFor preserves folders and removes ordering prefixes`` () =
        let path = ContentProvider.outputPathFor "docs" "docs/03-the-flow-type/02-creating-flows.md"
        Assert.Equal("the-flow-type/creating-flows.html", path)

    [<Fact>]
    let ``scanDocs rejects output path collisions`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Path.Combine(docsDir, "01-guides")) |> ignore
        Directory.CreateDirectory(Path.Combine(docsDir, "guides")) |> ignore
        File.WriteAllText(Path.Combine(docsDir, "01-guides", "start.md"), "Start")
        File.WriteAllText(Path.Combine(docsDir, "guides", "start.md"), "Start again")

        let error = Assert.Throws<InvalidOperationException>(fun () -> ContentProvider.scanDocs docsDir docsDir emptyPackage "" |> ignore)
        Assert.Contains("Documentation output path collision", error.Message)

    [<Fact>]
    let ``scanDocs uses section index title and nested output path`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let sectionDir = Path.Combine(docsDir, "01-http")
        Directory.CreateDirectory(sectionDir) |> ignore
        File.WriteAllText(Path.Combine(sectionDir, "_index.md"), "---\ntitle: HTTP\nweight: 1\n---\nSection")
        File.WriteAllText(Path.Combine(sectionDir, "02-client.md"), "Client")

        let pages = ContentProvider.scanDocs docsDir docsDir emptyPackage ""
        Assert.Contains(pages, fun page -> page.OutputPath = "http/index.html" && page.Metadata.Title = "HTTP")
        Assert.Contains(pages, fun page -> page.OutputPath = "http/client.html" && page.Metadata.Title = "Client")
        Assert.All(pages, fun page -> Assert.Equal(1, page.SectionOrder))

    [<Fact>]
    let ``scanDocs rewrites trailing slash links to generated html pages`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let sectionDir = Path.Combine(docsDir, "01-guides", "01-nested")
        Directory.CreateDirectory(sectionDir) |> ignore
        File.WriteAllText(Path.Combine(sectionDir, "_index.md"), "[Details](details/)")
        File.WriteAllText(Path.Combine(sectionDir, "01-details.md"), "Details")

        let pages = ContentProvider.scanDocs docsDir docsDir emptyPackage ""
        let indexPage = pages |> List.find (fun page -> page.OutputPath = "guides/nested/index.html")

        Assert.Contains("href=\"details.html\"", indexPage.ContentHtml)

    [<Fact>]
    let ``scanDocs preserves indented code in syntax-only fallback`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(docsDir) |> ignore
        File.WriteAllText(
            Path.Combine(docsDir, "guide.md"),
            "```fsharp\nlet choose value =\n\n    match value with\n    | Some item -> item\n    | None -> 0\n```")

        let page = ContentProvider.scanDocs docsDir docsDir emptyPackage "" |> List.exactlyOne

        Assert.Contains("match value with", page.ContentHtml)
        Assert.Contains("<pre><code class=\"language-fsharp\">", page.ContentHtml)
        Assert.DoesNotContain("&lt;span", page.ContentHtml)
        Assert.DoesNotContain("data-fslivedocs-semantic-placeholder", page.ContentHtml)

    [<Fact>]
    let ``syntax-only fallback uses the same code frame for every F sharp fence`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(docsDir) |> ignore
        File.WriteAllText(
            Path.Combine(docsDir, "guide.md"),
            "```fsharp\nlet valid = 42\n```\n\n```fsharp\nlet invalid : MissingType = ...\n```")

        let page = ContentProvider.scanDocs docsDir docsDir emptyPackage "" |> List.exactlyOne

        Assert.Equal(2, Regex.Matches(page.ContentHtml, "<pre><code class=\"language-fsharp\">").Count)

    [<Fact>]
    let ``copyStaticFiles preserves paths and ignores Markdown`` () =
        let testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let docsDir = Path.Combine(testRoot, "docs")
        let outputDir = Path.Combine(testRoot, "output")
        Directory.CreateDirectory(Path.Combine(docsDir, "content", "img")) |> ignore
        File.WriteAllText(Path.Combine(docsDir, "content", "img", "logo.svg"), "<svg />")
        File.WriteAllText(Path.Combine(docsDir, "content", "guide.md"), "# Guide")

        ContentProvider.copyStaticFiles docsDir outputDir

        Assert.True(File.Exists(Path.Combine(outputDir, "content", "img", "logo.svg")))
        Assert.False(File.Exists(Path.Combine(outputDir, "content", "guide.md")))

    [<Fact>]
    let ``parseFrontMatter extracts yaml and body`` () =
        let content = "---\ntitle: Hello\nweight: 10\n---\nBody here"
        match ContentProvider.parseFrontMatter content with
        | Some (meta, body) ->
            Assert.Equal("Hello", meta.Title)
            Assert.Equal("Body here", body.Trim())
        | None -> Assert.Fail("Should have parsed frontmatter")

    [<Fact>]
    let ``resolveSnippets handles transclusion and xrefs`` () =
        let package : PackageModel = { 
            Version = "1.0"
            Entities = [ { Id = "M1"; Name = "add"; Kind = EntityKind.Module; SummaryHtml = ""; Members = [ { Id = "M1.add"; Name = "add"; Signature = "int -> int"; Parameters = []; ReturnType = "int"; SummaryHtml = ""; RemarksHtml = ""; Examples = [ { Name = "E1"; Content = "1+1"; ExpectedOutput = None; Scenario = None; IsSnapshotTest = false } ]; Location = { File = ""; Line = 0 } } ]; Examples = []; Entities = [] } ]
            Scenarios = []; Packages = []
        }
        let body = "Look at {{< example id=\"E1\" >}} and xref:M:M1.add"
        let resolved = ContentProvider.resolveSnippets body "." package "/"
        Assert.Contains("```fsharp origin=xml-example\n1+1\n```", resolved)
        Assert.Contains("[add](/api/M1.html#M1.add)", resolved)

module DocTestRunnerTests =

    [<Fact>]
    let ``project resolver selects the project artifact rather than a copied dependency`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let projectDir = Path.Combine(root, "src", "Example.Project")
        let ownOutput = Path.Combine(root, "artifacts", "bin", "Example.Project", "debug")
        let copiedOutput = Path.Combine(root, "artifacts", "bin", "Unrelated.Tests", "debug")
        Directory.CreateDirectory(projectDir) |> ignore
        Directory.CreateDirectory(ownOutput) |> ignore
        Directory.CreateDirectory(copiedOutput) |> ignore
        let projectPath = Path.Combine(projectDir, "Example.Project.fsproj")
        File.WriteAllText(projectPath, "<Project><PropertyGroup><AssemblyName>Actual.Name</AssemblyName></PropertyGroup></Project>")
        for directory in [ ownOutput; copiedOutput ] do
            File.WriteAllText(Path.Combine(directory, "Actual.Name.dll"), "fixture")
            File.WriteAllText(Path.Combine(directory, "Actual.Name.xml"), "<doc />")

        let resolved = ProjectResolver.resolveAssemblyPath projectPath

        Assert.Equal(Path.Combine(ownOutput, "Actual.Name.dll"), resolved)

    [<Fact>]
    let ``ExampleTranscript parses FSI sessions`` () =
        let parsed =
            ExampleTranscript.parse
                """
                > let x = 1;;
                > x;;
                val x: int = 1
                val it: int = 1
                """

        Assert.Contains("let x = 1;;", parsed.Script)
        Assert.Contains("x;;", parsed.Script)
        Assert.Equal(Some "val x: int = 1\nval it: int = 1", parsed.ExpectedOutput)

    [<Fact>]
    let ``ExampleTranscript preserves multiline FSI input`` () =
        let parsed =
            ExampleTranscript.parse
                """
                > ExampleModel.Create("Basic Usage", "1+1", Some "2", None);;
                val it: ExampleModel = { Name = "Basic Usage"; Content = "1+1"; ExpectedOutput = Some "2"; Scenario = None; IsSnapshotTest = false }
                """

        Assert.Contains("ExampleModel.Create(\"Basic Usage\"", parsed.Script)
        Assert.Equal(Some "val it: ExampleModel = { Name = \"Basic Usage\"; Content = \"1+1\"; ExpectedOutput = Some \"2\"; Scenario = None; IsSnapshotTest = false }", parsed.ExpectedOutput)

    [<Fact>]
    let ``verifyExamples executes transcript style examples`` () =
        let package : PackageModel =
            {
                Version = "1.0"
                Entities =
                    [
                        {
                            Id = "Test.Module"
                            Name = "Module"
                            Kind = EntityKind.Module
                            SummaryHtml = ""
                            Members = []
                            Examples =
                                [
                                    {
                                        Name = "SessionExample"
                                        Content =
                                            """
                                            > let x = 1;;
                                            > x + 2;;
                                            val x: int = 1
                                            val it: int = 3
                                            """
                                        ExpectedOutput = Some "val x: int = 1\nval it: int = 3"
                                        Scenario = None
                                        IsSnapshotTest = true
                                    }
                                ]
                            Entities = []
                        }
                    ]
                Scenarios = []; Packages = []
            }

        let projectPath = Path.GetFullPath("src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj")
        let results = DocTestRunner.verifyExamples package projectPath [] |> Async.RunSynchronously
        let (_, passed, output) = Assert.Single(results)
        Assert.True(passed, output)

    [<Fact>]
    let ``collectSnapshots returns structured snapshot payload`` () =
        let package : PackageModel =
            {
                Version = "1.0"
                Entities =
                    [
                        {
                            Id = "Test.Module"
                            Name = "Module"
                            Kind = EntityKind.Module
                            SummaryHtml = ""
                            Members = []
                            Examples =
                                [
                                    {
                                        Name = "SnapshotExample"
                                        Content =
                                            """
                                            > let x = 1;;
                                            > x + 2;;
                                            val x: int = 1
                                            val it: int = 3
                                            """
                                        ExpectedOutput = Some "val x: int = 1\nval it: int = 3"
                                        Scenario = None
                                        IsSnapshotTest = true
                                    }
                                ]
                            Entities = []
                        }
                    ]
                Scenarios = []; Packages = []
            }

        let projectPath = Path.GetFullPath("src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj")
        let snapshot = DocTestRunner.collectSnapshots package projectPath [] |> Async.RunSynchronously
        let example = Assert.Single(snapshot.Examples)
        Assert.Equal(ExampleStatus.Verified, example.Status)
        Assert.Equal(Some "val x: int = 1\nval it: int = 3", example.ExpectedOutput)

module ViewTests =

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None; FSharpPrelude = None }

    [<Fact>]
    let ``tooltip surface is explicitly opaque`` () =
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }
        let page = { Metadata = { Title = "Guide"; Type = None; Project = None; TargetFramework = None; Platform = None }; ContentHtml = ""; FilePath = "guide.md"; OutputPath = "guide.html"; SectionOrder = 0 }
        let context : SiteBuilder.SiteRenderContext =
            { AllPages = [ page ]; Package = package; Config = defaultSiteConfig; Versions = []; Theme = "dark"; RootPath = "" }

        let html = SiteBuilder.renderPage page context

        Assert.Contains("background: #0f172a !important", html)
        Assert.Contains("background-image: linear-gradient(#0f172a, #0f172a) !important", html)

    [<Fact>]
    let ``sourceLinkHref builds github source links`` () =
        let link = View.sourceLinkHref (Some "https://github.com/user/repo") { File = "src/Example.fs"; Line = 42 }
        Assert.Equal(Some "https://github.com/user/repo/blob/main/src/Example.fs#L42", link)

    [<Fact>]
    let ``renderEntityPage renders record pages with a field table`` () =
        let recordEntity : EntityModel =
            {
                Id = "FsLiveDocs.Core.ParameterModel"
                Name = "ParameterModel"
                Kind = EntityKind.Record
                SummaryHtml = "<p>Represents a parameter of a function or method.</p>"
                Members =
                    [
                        {
                            Id = "FsLiveDocs.Core.ParameterModel.Name"
                            Name = "Name"
                            Signature = "string"
                            Parameters = []
                            ReturnType = "string"
                            SummaryHtml = "<p>The name of the parameter.</p>"
                            RemarksHtml = ""
                            Examples = []
                            Location = { File = ""; Line = 0 }
                        }
                    ]
                Examples = []
                Entities = []
            }

        let package : PackageModel = { Version = "1.0"; Entities = [ recordEntity ]; Scenarios = []; Packages = [ { Name = "FsLiveDocs.Core"; EntityIds = [ recordEntity.Id ] } ] }
        let context : SiteBuilder.SiteRenderContext =
            {
                AllPages = []
                Package = package
                Config = defaultSiteConfig
                Versions = []
                Theme = "light"
                RootPath = "../"
            }
        let html = SiteBuilder.renderEntityPage recordEntity context

        Assert.Contains("Fields", html)
        Assert.Contains("Represents a parameter of a function or method.", html)
        Assert.Contains("FsLiveDocs.Core", html)
        Assert.DoesNotContain("Specification", html)

module SiteBuilderTests =

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None; FSharpPrelude = None }

    [<Fact>]
    let ``history renders persisted semantic hovers without a historical project`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let docsDir = Path.Combine(root, "tagged", "docs")
        let outputDir = Path.Combine(root, "output")
        Directory.CreateDirectory(docsDir) |> ignore
        let markdown = "---\ntitle: Historical\n---\n```fsharp\nlet answer = 42\n```"
        File.WriteAllText(Path.Combine(docsDir, "index.md"), markdown)
        let discovered = DocumentationDiscovery.discoverMarkdown "index.md" None markdown |> List.head
        let semanticBlock = {
            Id = discovered.Id; SourceHash = discovered.SourceHash; ContextHash = DocumentationDiscovery.contextHash "" [ discovered ]
            Lines = [ { Tokens = [ { Text = "let"; Kind = Keyword; Tooltip = None }; { Text = " answer = 42"; Kind = Identifier; Tooltip = Some 0 } ] } ]
            Tooltips = [ { Signature = Some "answer: int"; Documentation = Some "Stored release documentation."; Sections = []; Footer = None } ]
            Diagnostics = []
        }
        let artifact = { SchemaVersion = History.SemanticSchemaVersion; Prelude = ""; Pages = [ { SourcePath = "index.md"; Blocks = [ semanticBlock ] } ] }
        let options = { SemanticCode.defaults with Artifact = Some artifact }
        let package : PackageModel = { Version = "1.0.0"; Entities = []; Scenarios = []; Packages = [] }
        let pages = ContentProvider.scanDocsWithOptions docsDir (Path.GetDirectoryName docsDir) package "" options

        SiteBuilder.buildHistory "1.0.0" [ "1.0.0", package, pages, docsDir ] defaultSiteConfig "light" outputDir

        let html = File.ReadAllText(Path.Combine(outputDir, "index.html"))
        Assert.Contains("answer: int", html)
        Assert.Contains("Stored release documentation.", html)
        Assert.Contains("data-fsdocs-tip", html)

    [<Fact>]
    let ``API summaries link compiler references to generated entity pages`` () =
        let exitEntity = {
            Id = "Example.Exit`2"
            Name = "Exit<'value, 'error>"
            Kind = EntityKind.Union
            SummaryHtml = ""
            Members = []
            Examples = []
            Entities = []
        }
        let deferredEntity = {
            Id = "Example.Deferred`2"
            Name = "Deferred<'error, 'value>"
            Kind = EntityKind.Union
            SummaryHtml = "A handoff containing <a href=\"/reference/example-exit-2.html\">Exit</a>."
            Members = []
            Examples = []
            Entities = []
        }
        let package : PackageModel = { Version = "1.0"; Entities = [ exitEntity; deferredEntity ]; Scenarios = []; Packages = [] }
        let context : SiteBuilder.SiteRenderContext = {
            AllPages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = "../"
        }

        let html = SiteBuilder.renderEntityPage deferredEntity context

        Assert.Contains("href=\"Example.Exit`2.html\"", html)
        Assert.DoesNotContain("/reference/", html)

    [<Fact>]
    let ``build rejects missing generated API page links`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let entity = {
            Id = "Example.Broken"
            Name = "Broken"
            Kind = EntityKind.Type
            SummaryHtml = "See <a href=\"Example.Missing.html\">Missing</a>."
            Members = []
            Examples = []
            Entities = []
        }
        let package : PackageModel = { Version = "1.0"; Entities = [ entity ]; Scenarios = []; Packages = [] }

        let error =
            Assert.Throws<InvalidOperationException>(fun () ->
                SiteBuilder.build {
                    Pages = []
                    Package = package
                    Config = defaultSiteConfig
                    Versions = []
                    Theme = "light"
                    RootPath = ""
                    OutputDir = outputDir
                })

        Assert.Contains("Broken generated API link", error.Message)

    [<Fact>]
    let ``generateLlmsTxt includes the expected heading`` () =
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }
        let summary = SiteBuilder.generateLlmsTxt package

        Assert.StartsWith("# API Reference for LLMs", summary)

    [<Fact>]
    let ``build preserves an authored homepage and nested page paths`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let metadata title = { Title = title; Type = None; Project = None; TargetFramework = None; Platform = None }
        let pages =
            [
                { Metadata = metadata "Home"; ContentHtml = "<h1>Consumer home</h1>"; FilePath = "docs/index.md"; OutputPath = "index.html"; SectionOrder = Int32.MaxValue }
                { Metadata = metadata "Client"; ContentHtml = "<h1>Client</h1>"; FilePath = "docs/01-http/02-client.md"; OutputPath = "http/client.html"; SectionOrder = 1 }
                { Metadata = metadata "Advanced"; ContentHtml = "<h1>Advanced</h1>"; FilePath = "docs/01-http/01-advanced/_index.md"; OutputPath = "http/advanced/index.html"; SectionOrder = 1 }
                { Metadata = metadata "Retries"; ContentHtml = "<h1>Retries</h1>"; FilePath = "docs/01-http/01-advanced/01-retries.md"; OutputPath = "http/advanced/retries.html"; SectionOrder = 1 }
            ]
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }

        SiteBuilder.build {
            Pages = pages
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            OutputDir = outputDir
        }

        let homepage = File.ReadAllText(Path.Combine(outputDir, "index.html"))
        let nestedPage = File.ReadAllText(Path.Combine(outputDir, "http", "client.html"))
        Assert.Contains("Consumer home", homepage)
        Assert.DoesNotContain("Verified documentation for the F# ecosystem", homepage)
        Assert.Contains("href=\"../index.html\"", nestedPage)
        Assert.DoesNotContain("class=\"group\" open=", nestedPage)
        Assert.Contains("data-docs-group=\"http/advanced\"", nestedPage)
        Assert.True(nestedPage.IndexOf("data-docs-group=\"http/advanced\"", StringComparison.Ordinal) < nestedPage.IndexOf(">Client</a>", StringComparison.Ordinal))
        Assert.Contains("currentSidebarLink.setAttribute('aria-current', 'page')", nestedPage)
        Assert.Contains("const isManagedFSharp = code.closest('.livedocs-code') !== null", nestedPage)
        Assert.Contains("if (!isManagedFSharp)", nestedPage)
        Assert.DoesNotContain("code-copy-button", nestedPage)
        Assert.DoesNotContain("navigator.clipboard", nestedPage)
        Assert.Contains("--livedocs-code-background: #f6f8fa;", nestedPage)
        Assert.Contains("html[data-theme=\"dark\"] pre.code-frame", nestedPage)
        Assert.Contains("--livedocs-code-background: #161b22;", nestedPage)
        Assert.Contains(".livedocs-code { margin: 1.7142857em -1.5rem; }", nestedPage)
        Assert.Contains("window.Prism.manual = true", nestedPage)
        Assert.True(nestedPage.IndexOf("window.Prism.manual = true", StringComparison.Ordinal) < nestedPage.IndexOf("prism.min.js", StringComparison.Ordinal))
        Assert.Contains(".livedocs-code .tok-keyword { color: var(--livedocs-code-keyword); }", nestedPage)
        Assert.Contains("#sidebar-root [data-sidebar-item=\"true\"] a[href]", nestedPage)
        Assert.Contains("el.style.display = el.getAttribute('data-theme-variant') === theme ? 'block' : 'none'", nestedPage)

    [<Fact>]
    let ``build renders consumer identity and navigation`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }
        let config = {
            RepoUrl = Some "https://github.com/example/library"
            SiteName = Some "Example Library"
            LogoText = Some "EL"
            LogoPath = Some "content/example-logo.svg"
            LogoDarkPath = Some "content/example-logo-dark.svg"
            ShowSiteName = None
            Stylesheet = Some "content/example.css"
            Themes = Some [ "light"; "dark" ]
            Navigation = Some [ { Label = "Guides"; Href = "index.html" }; { Label = "Source"; Href = "https://github.com/example/library" } ]
            FSharpPrelude = None
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = config
            Versions = []
            Theme = "light"
            RootPath = ""
            OutputDir = outputDir
        }

        let homepage = File.ReadAllText(Path.Combine(outputDir, "index.html"))
        Assert.Contains("Home - Example Library", homepage)
        Assert.Contains("src=\"content/example-logo.svg\"", homepage)
        Assert.Contains("src=\"content/example-logo-dark.svg\"", homepage)
        Assert.Contains("data-theme-variant=\"light\"", homepage)
        Assert.Contains("data-theme-variant=\"dark\"", homepage)
        Assert.Contains("applySiteTheme", homepage)
        Assert.Contains("alt=\"Example Library\"", homepage)
        Assert.Contains(">Example Library<", homepage)
        Assert.Contains("href=\"content/example.css\"", homepage)
        Assert.DoesNotContain("data-set-theme=\"cupcake\"", homepage)
        Assert.Contains("href=\"https://github.com/example/library\"", homepage)

    [<Fact>]
    let ``api index includes nested entities`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let child = {
            Id = "Example.Widget"
            Name = "Widget"
            Kind = EntityKind.Record
            SummaryHtml = "<p>A useful widget.</p>"
            Members = []
            Examples = []
            Entities = []
        }
        let root = {
            Id = "Example"
            Name = "Example"
            Kind = EntityKind.Namespace
            SummaryHtml = "<p>Example APIs.</p>"
            Members = []
            Examples = []
            Entities = [ child ]
        }
        let package : PackageModel = { Version = "1.0"; Entities = [ root ]; Scenarios = []; Packages = [] }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            OutputDir = outputDir
        }

        let apiIndex = File.ReadAllText(Path.Combine(outputDir, "api.html"))
        Assert.Contains("href=\"api/Example.Widget.html\"", apiIndex)
        Assert.Contains("A useful widget.", apiIndex)

module PresentationTests =

    [<Fact>]
    let ``highlightSignatureHtml emphasizes common F# types`` () =
        let html = Presentation.highlightSignatureHtml "string option -> int list"

        Assert.Contains("<span class=\"text-secondary font-semibold\">string</span>", html)
        Assert.Contains("<span class=\"text-secondary font-semibold\">option</span>", html)
        Assert.Contains("<span class=\"text-secondary font-semibold\">list</span>", html)

    [<Fact>]
    let ``synopsisFromHtml strips markup and returns the first sentence`` () =
        let summary = Presentation.synopsisFromHtml "<p>Represents a parameter. Additional details follow.</p>"

        Assert.Equal("Represents a parameter.", summary)
