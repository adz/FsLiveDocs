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
            Entries = [ { Version = "1.0.0"; ModelPath = "model.json"; ModelSha256 = "checksum"; DocsPath = "docs" } ]
        }
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Serialization.jsonSettings))

        let error = Assert.Throws<InvalidOperationException>(fun () -> History.loadManifest path |> ignore)
        Assert.Contains("has no manifest entry", error.Message)

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
    let ``semantic F sharp fences contain compiler tooltips`` () =
        let markdown = "```fsharp\nlet answer = List.sum [ 40; 2 ]\n```"
        let html = SemanticCode.formatFences SemanticCode.defaults "guide.fsx" markdown

        Assert.DoesNotContain("```fsharp", html)
        Assert.Contains("answer", html)
        Assert.Contains("fsdocs-tip", html)
        Assert.Contains("livedocs-tooltips not-prose", html)

    [<Fact>]
    let ``semantic F sharp fences resolve referenced project types`` () =
        let markdown = "```fsharp\nlet package : PackageModel = Unchecked.defaultof<_>\n```"
        let options =
            {
                SemanticCode.defaults with
                    References = [ typeof<PackageModel>.Assembly.Location ]
                    Opens = [ "FsLiveDocs.Core" ]
            }
        let html = SemanticCode.formatFences options "guide.fsx" markdown

        Assert.Contains("val package: PackageModel", html)
        Assert.DoesNotContain("val package: obj", html)
        Assert.Contains("The root model representing a documented package or solution.", html)
        Assert.Contains("fsdocs-tip-docs", html)
        Assert.DoesNotContain("&lt;summary&gt;", html)

    [<Fact>]
    let ``no-check F sharp fences remain Markdown`` () =
        let markdown = "```fsharp no-check\nlet incomplete =\n```"
        let formatted = SemanticCode.formatFences SemanticCode.defaults "guide.fsx" markdown

        Assert.Equal(markdown, formatted)

    [<Fact>]
    let ``invalid F sharp fences do not publish compiler recovery types`` () =
        let markdown = "```fsharp\nlet value : MissingType = ...\n```"
        let html = SemanticCode.formatFences SemanticCode.defaults "guide.fsx" markdown

        Assert.Contains("table class=\"pre\"", html)
        Assert.Contains("pre class=\"fssnip\"", html)
        Assert.DoesNotContain("fsdocs-tip", html)
        Assert.DoesNotContain("val value: obj", html)

    [<Fact>]
    let ``inferred obj recovery tooltips are omitted`` () =
        let markdown = "```fsharp\nlet placeholder = Unchecked.defaultof<_>\n```"
        let html = SemanticCode.formatFences SemanticCode.defaults "guide.fsx" markdown

        Assert.Contains("placeholder", html)
        Assert.DoesNotContain("val placeholder: obj", html)
        Assert.DoesNotContain("data-fsdocs-tip", html)

    [<Fact>]
    let ``identical F sharp fences receive distinct semantic output`` () =
        let fence = "```fsharp\nlet value = 42\n```"
        let html = SemanticCode.formatFences SemanticCode.defaults "guide.fsx" (fence + "\n\n" + fence)

        Assert.Equal(2, Regex.Matches(html, "val value: int").Count)

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
    let ``scanDocs preserves semantic spans across indented code`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(docsDir) |> ignore
        File.WriteAllText(
            Path.Combine(docsDir, "guide.md"),
            "```fsharp\nlet choose value =\n\n    match value with\n    | Some item -> item\n    | None -> 0\n```")

        let page = ContentProvider.scanDocs docsDir docsDir emptyPackage "" |> List.exactlyOne

        Assert.Contains("<span class=\"k\">match</span>", page.ContentHtml)
        Assert.DoesNotContain("&lt;span", page.ContentHtml)
        Assert.DoesNotContain("data-fslivedocs-semantic-placeholder", page.ContentHtml)

    [<Fact>]
    let ``valid and invalid F sharp fences use the same code frame`` () =
        let docsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(docsDir) |> ignore
        File.WriteAllText(
            Path.Combine(docsDir, "guide.md"),
            "```fsharp\nlet valid = 42\n```\n\n```fsharp\nlet invalid : MissingType = ...\n```")

        let page = ContentProvider.scanDocs docsDir docsDir emptyPackage "" |> List.exactlyOne

        Assert.Equal(2, Regex.Matches(page.ContentHtml, "<table class=\"pre\">").Count)
        Assert.Equal(2, Regex.Matches(page.ContentHtml, "<pre class=\"fssnip\">").Count / 2)

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
        Assert.Contains("```fsharp\n1+1\n```", resolved)
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

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None }

    [<Fact>]
    let ``tooltip surface is explicitly opaque`` () =
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }
        let page = { Metadata = { Title = "Guide"; Type = None }; ContentHtml = ""; FilePath = "guide.md"; OutputPath = "guide.html"; SectionOrder = 0 }
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

    let private defaultSiteConfig = { RepoUrl = None; SiteName = None; LogoText = None; LogoPath = None; LogoDarkPath = None; ShowSiteName = None; Stylesheet = None; Themes = None; Navigation = None }

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
        let metadata title = { Title = title; Type = None }
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
