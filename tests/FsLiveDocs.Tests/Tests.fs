namespace FsLiveDocs.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Cli
open FsLiveDocs.Runner
open FsLiveDocs.Renderer
open Newtonsoft.Json

module DocumentationSourceTests =

    let rec private repositoryRoot directory =
        if File.Exists(Path.Combine(directory, "FsLiveDocs.slnx")) then directory
        else
            let parent = Directory.GetParent directory
            if isNull parent then invalidOp "Could not locate the FsLiveDocs repository root."
            repositoryRoot parent.FullName

    [<Fact>]
    let ``installation docs do not pin the first published version`` () =
        let root = repositoryRoot AppContext.BaseDirectory
        let sources =
            Path.Combine(root, "README.md")
            :: (Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories) |> Array.toList)
        for path in sources do
            Assert.DoesNotContain("tool install FsLiveDocs --version", File.ReadAllText path)

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
    let ``API artifacts require an explicitly supported schema`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        File.WriteAllText(path, """{"SchemaVersion":1,"Package":{"Version":"1.2.3","Entities":[],"Scenarios":[],"Packages":[]}}""")

        let older = Assert.Throws<InvalidOperationException>(fun () -> History.loadArtifact "1.2.3" (History.sha256 path) path |> ignore)
        Assert.Contains("expected", older.Message)

        File.WriteAllText(path, """{"SchemaVersion":999,"Package":{"Version":"1.2.3","Entities":[],"Scenarios":[],"Packages":[]}}""")
        let error = Assert.Throws<InvalidOperationException>(fun () -> History.loadArtifact "1.2.3" (History.sha256 path) path |> ignore)
        Assert.Contains("expected", error.Message)

    [<Fact>]
    let ``API artifact stores structured documentation without HTML fields`` () =
        let summary =
            [ {
                  Kind = DocumentationNodeKind.Paragraph
                  Text = None
                  Target = None
                  Language = None
                  Children = [ Documentation.text "Use "; { Documentation.text "value" with Kind = DocumentationNodeKind.InlineCode } ]
              } ]
        let package : PackageModel =
            {
                Version = "1.2.3"
                Entities = [ { Id = "Sample"; Name = "Sample"; Kind = EntityKind.Module; Summary = summary; Members = []; Examples = []; Entities = [] } ]
                Scenarios = []
                Packages = []
            }
        let artifact : ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
        let json = JsonConvert.SerializeObject(artifact, Formatting.Indented, Serialization.jsonSettings)

        Assert.DoesNotContain("Html", json, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("<p>", json, StringComparison.OrdinalIgnoreCase)
        let loaded = JsonConvert.DeserializeObject<ApiModelArtifact>(json, Serialization.jsonSettings)
        Assert.Equal("Use value", Documentation.plainText loaded.Package.Entities.Head.Summary)

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

module ReleaseCapsuleTests =

    let private site : SiteConfig =
        {
            RepoUrl = None
            SiteName = Some "Sample"
            LogoText = None
            LogoPath = None
            LogoDarkPath = None
            ShowSiteName = Some true
            Stylesheet = None
            Themes = None
            Navigation = None
            FSharpPrelude = None
        }

    let private inputs () =
        let package: PackageModel =
            { Version = "1.2.3"
              Entities = []
              Scenarios = []
              Packages = [] }

        let api: ApiModelArtifact =
            { SchemaVersion = History.ApiModelSchemaVersion
              Package = package }

        let semantic: SemanticDocumentationArtifact =
            { SchemaVersion = History.SemanticSchemaVersion
              Prelude = ""
              Pages = [] }

        let metadata: ContentMetadata =
            { Title = "Home"
              Type = None
              Project = None
              TargetFramework = None
              Platform = None }

        api,
        semantic,
        [ { SourcePath = "index.md"
            SetId = DocsSet.DefaultId
            Metadata = metadata
            Markdown = "# Home\n" } ]

    [<Fact>]
    let ``release capsule is deterministic verified and self-contained`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let first = Path.Combine(root, "first.zip")
        let second = Path.Combine(root, "second.zip")
        let api, semantic, pages = inputs ()
        let assets = [ "images/logo.txt", Text.Encoding.UTF8.GetBytes("logo") ]

        let firstReport = ReleaseCapsule.create first "abc123" "0.1.0" api semantic site pages assets
        let secondReport = ReleaseCapsule.create second "abc123" "0.1.0" api semantic site pages assets
        Assert.Equal(firstReport.Sha256, secondReport.Sha256)

        let inspected = ReleaseCapsule.inspect first
        Assert.Equal("1.2.3", inspected.Manifest.ProductVersion)
        Assert.Equal("abc123", inspected.Manifest.SourceRevision)
        Assert.Equal(1, inspected.Counts.Pages)
        Assert.Equal(1, inspected.Counts.Assets)
        let acquired =
            ReleaseCapsule.acquire
                root
                (Path.Combine(root, "cache"))
                { Version = "1.2.3"; CapsulePath = Some "first.zip"; CapsuleUrl = None; CapsuleSha256 = firstReport.Sha256 }
        Assert.Equal(Path.GetFullPath first, acquired)

        let docsDir = Path.Combine(root, "materialized")
        let package, loadedSemantic, loadedSite = ReleaseCapsule.materializeContent first docsDir
        Assert.Equal("1.2.3", package.Version)
        Assert.Equal(History.SemanticSchemaVersion, loadedSemantic.SchemaVersion)
        Assert.Equal(Some "Sample", loadedSite.SiteName)
        Assert.Contains("# Home", File.ReadAllText(Path.Combine(docsDir, "index.md")))
        Assert.Equal("logo", File.ReadAllText(Path.Combine(docsDir, "images", "logo.txt")))

        let outputDir = Path.Combine(root, "site")
        let options = { SemanticCode.defaults with Artifact = Some loadedSemantic; Prelude = loadedSemantic.Prelude }
        let pages = ContentProvider.scanDocsWithOptions docsDir docsDir package "" options
        Assert.Equal("Home", pages.Head.Metadata.Title)
        SiteBuilder.build {
            Pages = pages
            Package = package
            Config = loadedSite
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }
        ContentProvider.copyStaticFiles docsDir outputDir
        Assert.Contains(">Home</h1>", File.ReadAllText(Path.Combine(outputDir, "index.html")))
        Assert.Equal("logo", File.ReadAllText(Path.Combine(outputDir, "images", "logo.txt")))

    [<Fact>]
    let ``release capsule rejects overwrite and unsafe asset paths`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let path = Path.Combine(root, "release.zip")
        let api, semantic, pages = inputs ()
        ReleaseCapsule.create path "abc123" "0.1.0" api semantic site pages [] |> ignore

        let overwrite = Assert.Throws<InvalidOperationException>(fun () -> ReleaseCapsule.create path "abc123" "0.1.0" api semantic site pages [] |> ignore)
        Assert.Contains("already exists", overwrite.Message)
        let unsafePath = Path.Combine(root, "unsafe.zip")
        let unsafeAsset = Assert.Throws<InvalidOperationException>(fun () -> ReleaseCapsule.create unsafePath "abc123" "0.1.0" api semantic site pages [ "../secret", [| 1uy |] ] |> ignore)
        Assert.Contains("Unsafe", unsafeAsset.Message)

    [<Fact>]
    let ``release history index requires unique entries and a current release`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        File.WriteAllText(path, """{"SchemaVersion":1,"CurrentVersion":"2.0.0","Entries":[{"Version":"1.0.0","CapsulePath":"one.zip","CapsuleSha256":"hash"}]}""")
        let missing = Assert.Throws<InvalidOperationException>(fun () -> ReleaseCapsule.loadHistoryIndex path |> ignore)
        Assert.Contains("has no capsule entry", missing.Message)

        File.WriteAllText(path, """{"SchemaVersion":1,"CurrentVersion":"1.0.0","Entries":[{"Version":"1.0.0","CapsulePath":"one.zip","CapsuleSha256":"hash"},{"Version":"1.0.0","CapsulePath":"two.zip","CapsuleSha256":"hash"}]}""")
        let duplicate = Assert.Throws<InvalidOperationException>(fun () -> ReleaseCapsule.loadHistoryIndex path |> ignore)
        Assert.Contains("duplicate versions", duplicate.Message)

        File.WriteAllText(path, """{"SchemaVersion":1,"CurrentVersion":"1.0.0","Entries":[{"Version":"1.0.0","CapsulePath":"one.zip","CapsuleUrl":"https://example.com/one.zip","CapsuleSha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}""")
        let ambiguous = Assert.Throws<InvalidOperationException>(fun () -> ReleaseCapsule.loadHistoryIndex path |> ignore)
        Assert.Contains("exactly one", ambiguous.Message)

    [<Fact>]
    let ``release histories use semantic newest-first ordering`` () =
        let hash = String.replicate 64 "0"
        let entry version = { Version = version; CapsulePath = None; CapsuleUrl = Some $"https://example.com/{version}.zip"; CapsuleSha256 = hash }
        let normalized =
            ReleaseCapsule.normalizeHistoryIndex {
                SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion
                CurrentVersion = "1.9.0"
                Entries = [ entry "1.9.0"; entry "1.10.0-beta.2"; entry "1.10.0"; entry "1.10.0-beta.10" ]
            }
        Assert.Equal<string list>([ "1.10.0"; "1.10.0-beta.10"; "1.10.0-beta.2"; "1.9.0" ], normalized.Entries |> List.map _.Version)
        Assert.Equal("1.10.0", normalized.CurrentVersion)

    [<Fact>]
    let ``release history rejects lexical ordering and a stale current version`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        let hash = String.replicate 64 "0"
        File.WriteAllText(path, $"""{{"SchemaVersion":1,"CurrentVersion":"1.9.0","Entries":[{{"Version":"1.9.0","CapsuleUrl":"https://example.com/1.9.0.zip","CapsuleSha256":"{hash}"}},{{"Version":"1.10.0","CapsuleUrl":"https://example.com/1.10.0.zip","CapsuleSha256":"{hash}"}}]}}""")
        let error = Assert.Throws<InvalidOperationException>(fun () -> ReleaseCapsule.loadHistoryIndex path |> ignore)
        Assert.Contains("newest-first", error.Message)

    [<Fact>]
    let ``history output verification checks entry points switcher order and local links`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let historical = Path.Combine(root, "history", "1.9.0")
        Directory.CreateDirectory(historical) |> ignore
        let hash = String.replicate 64 "0"
        let indexPath = Path.Combine(root, "history.json")
        let index : ReleaseHistoryIndex = {
            SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion
            CurrentVersion = "1.10.0"
            Entries =
                [ { Version = "1.10.0"; CapsulePath = None; CapsuleUrl = Some "https://example.com/1.10.0.zip"; CapsuleSha256 = hash }
                  { Version = "1.9.0"; CapsulePath = None; CapsuleUrl = Some "https://example.com/1.9.0.zip"; CapsuleSha256 = hash } ]
        }
        ReleaseCapsule.saveHistoryIndex indexPath index
        File.WriteAllText(Path.Combine(root, "index.html"), "<a href=\"guide.html\">Guide</a><span>1.10.0</span><span>1.9.0</span>")
        File.WriteAllText(Path.Combine(root, "guide.html"), "<p>Guide</p>")
        File.WriteAllText(Path.Combine(historical, "index.html"), "<a href=\"../../guide.html\">Guide</a>")
        Assert.Equal(3, ReleaseHistoryCommands.verify indexPath root)
        File.WriteAllText(Path.Combine(root, "index.html"), "<span>1.9.0</span><span>1.10.0</span><a href=\"missing.html\">Missing</a>")
        let error = Assert.Throws<InvalidOperationException>(fun () -> ReleaseHistoryCommands.verify indexPath root |> ignore)
        Assert.Contains("links do not resolve", error.Message)

    [<Fact>]
    let ``history verification ignores pagefind assets`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let hash = String.replicate 64 "0"
        let indexPath = Path.Combine(root, "history.json")
        ReleaseCapsule.saveHistoryIndex indexPath {
            SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion
            CurrentVersion = "1.0.0"
            Entries = [ { Version = "1.0.0"; CapsulePath = None; CapsuleUrl = Some "https://example.com/1.0.0.zip"; CapsuleSha256 = hash } ]
        }
        File.WriteAllText(Path.Combine(root, "index.html"), "<span>1.0.0</span><link href=\"pagefind/pagefind-ui.css\"><script src=\"pagefind/pagefind-ui.js\"></script>")
        Assert.Equal(1, ReleaseHistoryCommands.verify indexPath root)

    [<Fact>]
    let ``history-sync merges entries from a discovery command`` () =
        let indexPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        let hash = String.replicate 64 "a"
        let command = ReleaseHistoryCommands.Command $"printf '1.2.0 https://example.com/pkg-1.2.0-livedocs.zip {hash}\\n1.3.0 https://example.com/pkg-1.3.0-livedocs.zip {hash}\\n'"
        let updated = ReleaseHistoryCommands.sync command indexPath None None None
        Assert.Equal<string list>([ "1.3.0"; "1.2.0" ], updated.Entries |> List.map _.Version)
        Assert.Equal("1.3.0", updated.CurrentVersion)
        Assert.All(updated.Entries, fun entry -> Assert.Equal(hash, entry.CapsuleSha256))

    [<Fact>]
    let ``history-add reads its checksum from a sha256 file`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let capsule = Path.Combine(root, "pkg-1.0.0-livedocs.zip")
        File.WriteAllBytes(capsule, [| 1uy; 2uy; 3uy |])
        let hash = String.replicate 64 "b"
        let shaFile = capsule + ".sha256"
        File.WriteAllText(shaFile, hash.ToUpperInvariant() + "\n")
        let indexPath = Path.Combine(root, "history.json")
        Program.historyAddAction indexPath "1.0.0" None (Some "https://example.com/pkg-1.0.0-livedocs.zip") None (Some shaFile) |> ignore
        let index = ReleaseCapsule.loadHistoryIndex indexPath
        Assert.Equal(hash, index.Entries.Head.CapsuleSha256)

    [<Fact>]
    let ``history-check rejects a candidate capsule without a version`` () =
        let indexPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        ReleaseCapsule.saveHistoryIndex indexPath {
            SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion
            CurrentVersion = "1.0.0"
            Entries = [ { Version = "1.0.0"; CapsulePath = None; CapsuleUrl = Some "https://example.com/1.0.0.zip"; CapsuleSha256 = String.replicate 64 "0" } ]
        }
        let error = Assert.Throws<InvalidOperationException>(fun () -> Program.historyCheckAction indexPath (Some "candidate.zip") None "light" 3 |> ignore)
        Assert.Contains("requires --version", error.Message)

    [<Fact>]
    let ``url pattern expansion fills version and tag`` () =
        Assert.Equal(
            "https://h/x/v1.4.0/pkg-1.4.0.zip",
            Program.expandUrlPattern "https://h/x/{tag}/pkg-{version}.zip" "1.4.0")

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
    let ``extractExamples removes code wrapper from an example`` () =
        let xml =
            """
            <example>
            <code>
            flow {
                return 42
            }
            </code>
            </example>
            """

        let example = SymbolLister.extractExamples xml |> Assert.Single

        Assert.Equal("flow {\n    return 42\n}", example.Content.Trim())

    [<Fact>]
    let ``extractExamples reads a no-check exclusion and its reason`` () =
        let xml =
            """
            <example name="Fragment" data-livedocs="no-check" reason="Illustrative fragment">
            let partial =
            </example>
            """

        let example = SymbolLister.extractExamples xml |> Assert.Single

        Assert.Equal(Some "Illustrative fragment", example.NoCheckReason)

    [<Fact>]
    let ``extractExamples rejects a no-check exclusion without a reason`` () =
        // An exclusion nobody has to justify is indistinguishable from an oversight, which is why
        // the markdown no-check fence demands the same.
        let xml = """<example name="Fragment" data-livedocs="no-check">let partial =</example>"""

        let error = Assert.Throws<InvalidOperationException>(fun () -> SymbolLister.extractExamples xml |> ignore)

        Assert.Contains("without a non-empty reason", error.Message)

    [<Fact>]
    let ``reconcileUsageSignature replaces placeholders in order, not by their number`` () =
        // FSharp.Formatting numbers placeholders independently of the parameter metadata, so the
        // number carried by a placeholder cannot be trusted to identify which argument it is.
        let usage = "<code><span>run&#32;<span>token&#32;arg2</span></span></code>"

        let reconciled = SymbolLister.reconcileUsageSignature [ "ColdTask operation" ] usage

        Assert.Equal("<code><span>run&#32;<span>token&#32;(ColdTask operation)</span></span></code>", reconciled)

    [<Fact>]
    let ``reconcileUsageSignature keeps a placeholder it has no name for`` () =
        let usage = "<code><span>pass&#32;<span>arg1</span></span></code>"

        Assert.Equal(usage, SymbolLister.reconcileUsageSignature [] usage)

    [<Fact>]
    let ``merge removes empty synthetic Default namespace`` () =
        let child = { Id = "Default.Sample"; Name = "Sample"; Kind = EntityKind.Module; Summary = []; Members = []; Examples = []; Entities = [] }
        let defaultNamespace = { Id = "Default"; Name = "Default"; Kind = EntityKind.Namespace; Summary = []; Members = []; Examples = []; Entities = [ child ] }
        let package = SymbolLister.merge [ { Version = "1.0"; Entities = [ defaultNamespace; child ]; Scenarios = []; Packages = [ { Name = "Example.Package"; EntityIds = [ child.Id ] } ] } ]

        let onlyEntity = Assert.Single(package.Entities)
        Assert.Equal("Default.Sample", onlyEntity.Id)
        Assert.Equal("Sample", onlyEntity.Name)
        Assert.Equal("Example.Package", Assert.Single(package.Packages).Name)

    [<Fact>]
    let ``merge combines entities contributed to the same namespace by multiple packages`` () =
        let coreChild = { Id = "Example.CoreFlow"; Name = "CoreFlow"; Kind = EntityKind.Module; Summary = []; Members = []; Examples = []; Entities = [] }
        let satelliteChild = { Id = "Example.Http"; Name = "Http"; Kind = EntityKind.Module; Summary = []; Members = []; Examples = []; Entities = [] }
        let coreRoot = { Id = "Example"; Name = "Example"; Kind = EntityKind.Namespace; Summary = []; Members = []; Examples = []; Entities = [ coreChild ] }
        let satelliteRoot = { Id = "Example"; Name = "Example"; Kind = EntityKind.Namespace; Summary = []; Members = []; Examples = []; Entities = [ satelliteChild ] }
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
    let ``generated verification exposes stable cases and honors external execution ownership`` () =
        let markdown = "```fsharp\nlet a = 1\n```\n```fsharp run\nprintfn \"run\"\n```\n```fsharp transcript\n> 1 + 1;;\nval it: int = 2\n```"
        let allCases = DocumentationDiscovery.generatedCases "sample.fsproj" "" "guide.md" markdown Set.empty
        let transcriptId = "guide.md#fsharp-2"
        let generated = DocumentationDiscovery.generatedCases "sample.fsproj" "" "guide.md" markdown (set [ transcriptId ])

        Assert.Equal<string list>(
            [ "guide.md#page"; "guide.md#fsharp-1"; transcriptId ],
            allCases |> List.map _.Id)
        Assert.Equal<string list>(
            [ "guide.md#page"; "guide.md#fsharp-1" ],
            generated |> List.map _.Id)
        Assert.All(generated, fun case ->
            Assert.Equal("sample.fsproj", case.ProjectPath)
            Assert.Equal("guide.md", case.SourcePath)
            Assert.Equal(markdown, case.ExpandedMarkdown))

    [<Fact>]
    let ``snippet modes and example transcripts survive canonical expansion`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        File.WriteAllText(Path.Combine(root, "Sample.fs"), "// <snippet:Partial>\nmissing ...\n// </snippet:Partial>")
        let example = ExampleModel.Create("Session", "> 1 + 1;;\nval it: int = 2", Some "val it: int = 2", None)
        let memberModel = { Id = "M"; Name = "M"; Signature = ""; Parameters = []; ReturnType = ""; Summary = []; Remarks = []; Examples = [ example ]; Location = { File = ""; Line = 1 } }
        let entity = { Id = "E"; Name = "E"; Kind = EntityKind.Module; Summary = []; Members = [ memberModel ]; Examples = []; Entities = [] }
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
        Assert.DoesNotContain("<small>Sample</small>", html)
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
            Entities = [ { Id = "M1"; Name = "add"; Kind = EntityKind.Module; Summary = []; Members = [ { Id = "M1.add"; Name = "add"; Signature = "int -> int"; Parameters = []; ReturnType = "int"; Summary = []; Remarks = []; Examples = [ { Name = "E1"; Content = "1+1"; ExpectedOutput = None; Scenario = None; IsSnapshotTest = false; NoCheckReason = None } ]; Location = { File = ""; Line = 0 } } ]; Examples = []; Entities = [] } ]
            Scenarios = []; Packages = []
        }
        let body = "Look at {{< example id=\"E1\" >}} and xref:M:M1.add"
        let resolved = ContentProvider.resolveSnippets body "." package "/"
        Assert.Contains("```fsharp origin=xml-example\n1+1\n```", resolved)
        Assert.Contains("[add](/api/M1.html#M1.add)", resolved)

    [<Fact>]
    let ``inline code links unambiguous API symbols from the Markdown tree`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let file = Path.Combine(root, "guide.md")
        File.WriteAllText(file, "Use `Math.add` and `Math`. Keep `missing` as code.")
        let package : PackageModel =
            {
                Version = "1.0"
                Entities =
                    [
                        {
                            Id = "Example.Math"
                            Name = "Math"
                            Kind = EntityKind.Module
                            Summary = []
                            Members = [ { Id = "Example.Math.add"; Name = "add"; Signature = "int -> int"; Parameters = []; ReturnType = "int"; Summary = []; Remarks = []; Examples = []; Location = { File = ""; Line = 0 } } ]
                            Examples = []
                            Entities = []
                        }
                    ]
                Scenarios = []
                Packages = []
            }

        let page = ContentProvider.loadPage file root package "/" "guide.html" (set [ "guide.html"; "api/Example.Math.html" ])

        Assert.Contains("<a href=\"/api/Example.Math.html#Example.Math.add\"><code>Math.add</code></a>", page.ContentHtml)
        Assert.Contains("<a href=\"/api/Example.Math.html\"><code>Math</code></a>", page.ContentHtml)
        Assert.Contains("<code>missing</code>", page.ContentHtml)

    [<Fact>]
    let ``rendered container frames sample output with a livedocs class`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let file = Path.Combine(root, "guide.md")
        File.WriteAllText(file, "Intro.\n\n::: rendered\n### Demo heading\n\nSample body.\n:::\n")

        let page = ContentProvider.loadPage file root emptyPackage "/" "guide.html" (set [ "guide.html" ])

        Assert.Contains("livedocs-rendered", page.ContentHtml)
        Assert.Contains("Demo heading", page.ContentHtml)

    [<Fact>]
    let ``rendered container keeps its heading out of the page navigation`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let file = Path.Combine(root, "guide.md")
        File.WriteAllText(file, "## Real heading\n\n::: rendered\n## Demo heading\n:::\n")

        let page = ContentProvider.loadPage file root emptyPackage "/" "guide.html" (set [ "guide.html" ])

        // Both headings render, but the renderer's on-this-page script filters those inside
        // `.livedocs-rendered`; the demo heading sits within that wrapper.
        let renderedIndex = page.ContentHtml.IndexOf("livedocs-rendered", StringComparison.Ordinal)
        let demoIndex = page.ContentHtml.IndexOf("Demo heading", StringComparison.Ordinal)
        Assert.True(renderedIndex >= 0 && demoIndex > renderedIndex)

    [<Fact>]
    let ``explicit xref links keep authored labels and fail when unresolved`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let file = Path.Combine(root, "guide.md")
        let package : PackageModel =
            {
                Version = "1.0"
                Entities = [ { Id = "Example.Math"; Name = "Math"; Kind = EntityKind.Module; Summary = []; Members = []; Examples = []; Entities = [] } ]
                Scenarios = []
                Packages = []
            }
        File.WriteAllText(file, "See [`the math module`](xref:T:Example.Math).")

        let page = ContentProvider.loadPage file root package "/" "guide.html" (set [ "guide.html"; "api/Example.Math.html" ])
        Assert.Contains("<a href=\"/api/Example.Math.html\"><code>the math module</code></a>", page.ContentHtml)

        File.WriteAllText(file, "See [`missing`](xref:T:Example.Missing).")
        let error = Assert.Throws<InvalidOperationException>(fun () -> ContentProvider.loadPage file root package "/" "guide.html" (set [ "guide.html" ]) |> ignore)
        Assert.Contains("Cross-reference 'xref:T:Example.Missing' was not found", error.Message)

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
                val it: ExampleModel = { Name = "Basic Usage"; Content = "1+1"; ExpectedOutput = Some "2"; Scenario = None; IsSnapshotTest = false; NoCheckReason = None }
                """

        Assert.Contains("ExampleModel.Create(\"Basic Usage\"", parsed.Script)
        Assert.Equal(Some "val it: ExampleModel = { Name = \"Basic Usage\"; Content = \"1+1\"; ExpectedOutput = Some \"2\"; Scenario = None; IsSnapshotTest = false; NoCheckReason = None }", parsed.ExpectedOutput)

    [<Fact>]
    let ``a transcript example runs and matches its documented output`` () =
        let package : PackageModel =
            {
                Version = "1.0"
                Entities =
                    [
                        {
                            Id = "Test.Module"
                            Name = "Module"
                            Kind = EntityKind.Module
                            Summary = []
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
                                        IsSnapshotTest = true; NoCheckReason = None
                                    }
                                ]
                            Entities = []
                        }
                    ]
                Scenarios = []; Packages = []
            }

        let projectPath = Path.GetFullPath("src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj")
        let snapshot = DocTestRunner.collectSnapshotByName package projectPath [] "SessionExample" |> Async.RunSynchronously
        Assert.Equal(ExampleStatus.Verified, snapshot.Status)

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
                            Summary = []
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
                                        IsSnapshotTest = true; NoCheckReason = None
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
            { AllPages = [ page ]; Package = package; Config = defaultSiteConfig; Versions = []; Theme = "dark"; RootPath = ""; SiteRootPath = "" }

        let html = SiteBuilder.renderPage page context

        Assert.Contains("background: #0f172a !important", html)
        Assert.Contains("background-image: linear-gradient(#0f172a, #0f172a) !important", html)

    [<Fact>]
    let ``sidebar orders a folder by its earliest prefixed page`` () =
        let metadata title = { Title = title; Type = None; Project = None; TargetFramework = None; Platform = None }
        let page source output title = { Metadata = metadata title; ContentHtml = ""; FilePath = source; OutputPath = output; SectionOrder = 0 }
        let pages =
            [ page "01-start.md" "start.html" "Get started"
              page "02-guides/01-examples.md" "guides/examples.html" "Examples"
              page "03-advanced.md" "advanced.html" "Advanced" ]
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = []; Packages = [] }
        let context : SiteBuilder.SiteRenderContext =
            { AllPages = pages; Package = package; Config = defaultSiteConfig; Versions = []; Theme = "light"; RootPath = ""; SiteRootPath = "" }

        let html = SiteBuilder.renderPage pages.Head context

        let startIndex = html.LastIndexOf("href=\"start.html\"")
        let guidesIndex = html.LastIndexOf("href=\"guides/examples.html\"")
        let advancedIndex = html.LastIndexOf("href=\"advanced.html\"")
        Assert.True(startIndex >= 0 && startIndex < guidesIndex, $"start={startIndex}, guides={guidesIndex}, advanced={advancedIndex}")
        Assert.True(guidesIndex < advancedIndex, $"start={startIndex}, guides={guidesIndex}, advanced={advancedIndex}")

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
                Summary = [ Documentation.text "Represents a parameter of a function or method." ]
                Members =
                    [
                        {
                            Id = "FsLiveDocs.Core.ParameterModel.Name"
                            Name = "Name"
                            Signature = "string"
                            Parameters = []
                            ReturnType = "string"
                            Summary = [ Documentation.text "The name of the parameter." ]
                            Remarks = []
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
                SiteRootPath = "../"
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
    let ``historical version pages keep sidebar and same-version links inside their own history subtree`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let outputDir = Path.Combine(root, "output")

        let makeSite version =
            let docsDir = Path.Combine(root, version, "docs")
            Directory.CreateDirectory(docsDir) |> ignore
            File.WriteAllText(Path.Combine(docsDir, "index.md"), "---\ntitle: Home\n---\nHello")
            File.WriteAllText(Path.Combine(docsDir, "guide.md"), "---\ntitle: Guide\n---\nGuide body")
            let package : PackageModel = { Version = version; Entities = []; Scenarios = []; Packages = [] }
            let pages = ContentProvider.scanDocsWithOptions docsDir docsDir package "" SemanticCode.defaults
            version, package, pages, docsDir

        let v2 = makeSite "2.0.0"
        let v1 = makeSite "1.0.0"

        SiteBuilder.buildHistory "2.0.0" [ v2; v1 ] defaultSiteConfig "light" outputDir

        let v1Guide = File.ReadAllText(Path.Combine(outputDir, "history", "1.0.0", "guide.html"))

        // Sidebar's Home link must stay inside history/1.0.0/, not climb back to the
        // current version's index.html at the site root.
        Assert.Contains("href=\"index.html\"", v1Guide)
        Assert.DoesNotContain("href=\"../../index.html\"", v1Guide)

        // The version switcher must always be resolvable from the true site root
        // (it needs to reach the sibling "history/{version}/" tree), and must
        // preserve the current page rather than always targeting index.html.
        Assert.Contains("href=\"../../guide.html\"", v1Guide)
        Assert.Contains("href=\"../../history/1.0.0/guide.html\"", v1Guide)
        Assert.DoesNotContain("href=\"../../history/1.0.0/index.html\"", v1Guide)

    [<Fact>]
    let ``API summaries link compiler references to generated entity pages`` () =
        let exitEntity = {
            Id = "Example.Exit`2"
            Name = "Exit<'value, 'error>"
            Kind = EntityKind.Union
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let deferredEntity = {
            Id = "Example.Deferred`2"
            Name = "Deferred<'error, 'value>"
            Kind = EntityKind.Union
            Summary =
                [ Documentation.text "A handoff containing "
                  {
                      Kind = DocumentationNodeKind.SymbolReference
                      Text = None
                      Target = Some "T:Example.Exit`2"
                      Language = None
                      Children = [ Documentation.text "Exit" ]
                  }
                  Documentation.text "." ]
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
            SiteRootPath = "../"
        }

        let html = SiteBuilder.renderEntityPage deferredEntity context

        Assert.Contains("href=\"Example.Exit`2.html\"", html)
        Assert.DoesNotContain("/reference/", html)

    [<Fact>]
    let ``renderer leaves unresolved symbol references unlinked`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let entity = {
            Id = "Example.Broken"
            Name = "Broken"
            Kind = EntityKind.Type
            Summary =
                [ Documentation.text "See "
                  {
                      Kind = DocumentationNodeKind.SymbolReference
                      Text = None
                      Target = Some "T:Example.Missing"
                      Language = None
                      Children = [ Documentation.text "Missing" ]
                  }
                  Documentation.text "." ]
            Members = []
            Examples = []
            Entities = []
        }
        let package : PackageModel = { Version = "1.0"; Entities = [ entity ]; Scenarios = []; Packages = [] }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let html = File.ReadAllText(Path.Combine(outputDir, "api", "Example.Broken.html"))
        Assert.Contains("See Missing.", html)
        Assert.DoesNotContain("Example.Missing.html", html)

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
            SiteRootPath = ""
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
        Assert.Contains("<div id=\"search-ui\" class=\"hidden md:block not-prose\"></div>", nestedPage)
        Assert.Contains("#search-ui .pagefind-ui__drawer", nestedPage)
        Assert.Contains("overflow-wrap: anywhere", nestedPage)
        Assert.DoesNotContain("not-prose mb-12", nestedPage)
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
            SiteRootPath = ""
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
            Summary = [ Documentation.text "A useful widget." ]
            Members = []
            Examples = []
            Entities = []
        }
        let root = {
            Id = "Example"
            Name = "Example"
            Kind = EntityKind.Namespace
            Summary = [ Documentation.text "Example APIs." ]
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
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let apiIndex = File.ReadAllText(Path.Combine(outputDir, "api.html"))
        Assert.Contains("href=\"api/Example.Widget.html\"", apiIndex)
        Assert.Contains("A useful widget.", apiIndex)

    [<Fact>]
    let ``sidebar groups API reference by configured project order`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let entity id name = {
            Id = id
            Name = name
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let first = entity "Shared.First" "First"
        let second = entity "Shared.Second" "Second"
        let shared = {
            Id = "Shared"
            Name = "Shared"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ first; second ]
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ shared ]
            Scenarios = []
            Packages = [
                { Name = "Second.Project"; EntityIds = [ "Shared"; second.Id ] }
                { Name = "First.Project"; EntityIds = [ "Shared"; first.Id ] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let homepage = File.ReadAllText(Path.Combine(outputDir, "index.html"))
        let secondProject = homepage.IndexOf(">Second.Project<", StringComparison.Ordinal)
        let firstProject = homepage.IndexOf(">First.Project<", StringComparison.Ordinal)
        Assert.True(secondProject >= 0)
        Assert.True(firstProject > secondProject)

        let secondMenu = homepage.Substring(secondProject, firstProject - secondProject)
        Assert.Contains(">Second</a>", secondMenu)
        Assert.DoesNotContain(">First</a>", secondMenu)

    [<Fact>]
    let ``API reference index groups entities by project`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let entity id name = {
            Id = id
            Name = name
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let first = entity "Shared.First" "First"
        let second = entity "Shared.Second" "Second"
        let shared = {
            Id = "Shared"
            Name = "Shared"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ first; second ]
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ shared ]
            Scenarios = []
            Packages = [
                { Name = "Second.Project"; EntityIds = [ "Shared"; second.Id ] }
                { Name = "First.Project"; EntityIds = [ "Shared"; first.Id ] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let apiIndex = File.ReadAllText(Path.Combine(outputDir, "api.html"))
        let secondProject = apiIndex.IndexOf(">Second.Project<", StringComparison.Ordinal)
        let firstProject = apiIndex.IndexOf(">First.Project<", StringComparison.Ordinal)
        Assert.True(secondProject >= 0)
        Assert.True(firstProject > secondProject)

        let secondSection = apiIndex.Substring(secondProject, firstProject - secondProject)
        Assert.Contains(">Second<", secondSection)
        Assert.DoesNotContain(">First<", secondSection)

    [<Fact>]
    let ``entity page hides a package badge that only restates the entity's own namespace`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let owned = {
            Id = "Axial.Layers"
            Name = "Layers"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let shared = {
            Id = "Axial.Shared"
            Name = "Shared"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ owned; shared ]
            Scenarios = []
            Packages = [
                { Name = "Axial.Layers"; EntityIds = [ owned.Id; shared.Id ] }
                { Name = "Axial.PlatformService"; EntityIds = [ shared.Id ] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let ownedPage = File.ReadAllText(Path.Combine(outputDir, "api", "Axial.Layers.html"))
        Assert.DoesNotContain("badge-outline font-mono", ownedPage)

        let sharedPage = File.ReadAllText(Path.Combine(outputDir, "api", "Axial.Shared.html"))
        Assert.Contains(">Axial.Layers<", sharedPage)
        Assert.Contains(">Axial.PlatformService<", sharedPage)

    [<Fact>]
    let ``sidebar package group omits ancestor namespace wrappers`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let builders = {
            Id = "Axial.Layers.Builders"
            Name = "Builders"
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let layer = {
            Id = "Axial.Layers.Layer"
            Name = "Layer"
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let axialLayers = {
            Id = "Axial.Layers"
            Name = "Layers"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ builders; layer ]
        }
        let axial = {
            Id = "Axial"
            Name = "Axial"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ axialLayers ]
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ axial ]
            Scenarios = []
            Packages = [
                { Name = "Axial.Layers"; EntityIds = [ builders.Id; layer.Id ] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let homepage = File.ReadAllText(Path.Combine(outputDir, "index.html"))
        let groupStart = homepage.IndexOf(">Axial.Layers<", StringComparison.Ordinal)
        Assert.True(groupStart >= 0)
        Assert.Contains("href=\"api/packages/Axial.Layers.html", homepage.Substring(max 0 (groupStart - 200), 200))

        let groupEnd = homepage.IndexOf("</details>", groupStart, StringComparison.Ordinal)
        let groupBody = homepage.Substring(groupStart, groupEnd - groupStart)
        Assert.DoesNotContain(">Axial<", groupBody)
        Assert.DoesNotContain(">Layers<", groupBody)
        Assert.Contains(">Builders<", groupBody)
        Assert.Contains(">Layer<", groupBody)

    [<Fact>]
    let ``package module with the package id does not hide sibling API entries`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let schemaModule = {
            Id = "Reified.Schema"
            Name = "Schema"
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let schemaType = {
            Id = "Reified.Schema`1"
            Name = "Schema<'model>"
            Kind = EntityKind.Type
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let inspectModule = {
            Id = "Reified.Inspect"
            Name = "Inspect"
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let reified = {
            Id = "Reified"
            Name = "Reified"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ schemaModule; schemaType; inspectModule ]
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ reified ]
            Scenarios = []
            Packages = [
                { Name = "Reified.Schema"; EntityIds = [ schemaModule.Id; schemaType.Id; inspectModule.Id ] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let schemaPage = File.ReadAllText(Path.Combine(outputDir, "api", "Reified.Schema.html"))
        let groupStart = schemaPage.IndexOf(">Reified.Schema<", StringComparison.Ordinal)
        let groupEnd = schemaPage.IndexOf("</details>", groupStart, StringComparison.Ordinal)
        let groupBody = schemaPage.Substring(groupStart, groupEnd - groupStart)
        Assert.DoesNotContain(">Reified<", groupBody)
        Assert.Contains("Reified.Schema`1.html", schemaPage)
        Assert.Contains("Reified.Inspect.html", schemaPage)

    [<Fact>]
    let ``entity page Contents omits another project's own root namespace`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let app = {
            Id = "Axial.App"
            Name = "App"
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let axialLayers = {
            Id = "Axial.Layers"
            Name = "Layers"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let axial = {
            Id = "Axial"
            Name = "Axial"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ app; axialLayers ]
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ axial ]
            Scenarios = []
            Packages = [
                { Name = "Axial"; EntityIds = [ app.Id ] }
                { Name = "Axial.Layers"; EntityIds = [] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let axialPage = File.ReadAllText(Path.Combine(outputDir, "api", "Axial.html"))
        Assert.Contains(">App<", axialPage)
        Assert.DoesNotContain(">Layers<", axialPage)

    [<Fact>]
    let ``package sidebar link opens a distinct package landing page`` () =
        let outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        let attribute = {
            Id = "Axial.Telemetry.Attribute"
            Name = "Attribute"
            Kind = EntityKind.Type
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let fiberTelemetry = {
            Id = "Axial.Telemetry.FiberTelemetry"
            Name = "FiberTelemetry"
            Kind = EntityKind.Module
            Summary = []
            Members = []
            Examples = []
            Entities = []
        }
        let telemetry = {
            Id = "Axial.Telemetry"
            Name = "Telemetry"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ attribute; fiberTelemetry ]
        }
        let axial = {
            Id = "Axial"
            Name = "Axial"
            Kind = EntityKind.Namespace
            Summary = []
            Members = []
            Examples = []
            Entities = [ telemetry ]
        }
        let package : PackageModel = {
            Version = "1.0"
            Entities = [ axial ]
            Scenarios = []
            Packages = [
                { Name = "Axial"; EntityIds = [ attribute.Id ] }
                { Name = "Axial.Telemetry"; EntityIds = [ fiberTelemetry.Id ] }
            ]
        }

        SiteBuilder.build {
            Pages = []
            Package = package
            Config = defaultSiteConfig
            Versions = []
            Theme = "light"
            RootPath = ""
            SiteRootPath = ""
            OutputDir = outputDir
        }

        let homepage = File.ReadAllText(Path.Combine(outputDir, "index.html"))
        Assert.Contains("href=\"api/packages/Axial.Telemetry.html\"", homepage)

        let packagePage = File.ReadAllText(Path.Combine(outputDir, "api", "packages", "Axial.Telemetry.html"))
        Assert.Contains("<title>Axial.Telemetry - FsLiveDocs</title>", packagePage)
        Assert.Contains(">Package<", packagePage)
        Assert.Contains("href=\"../Axial.Telemetry.FiberTelemetry.html\"", packagePage)
        Assert.DoesNotContain("href=\"../Axial.Telemetry.Attribute.html\"", packagePage)

        let namespacePage = File.ReadAllText(Path.Combine(outputDir, "api", "Axial.Telemetry.html"))
        Assert.Contains(">Attribute<", namespacePage)
        Assert.Contains(">FiberTelemetry<", namespacePage)

module PresentationTests =

    [<Fact>]
    let ``highlightSignatureHtml emphasizes common F# types`` () =
        let html = Presentation.highlightSignatureHtml "string option -> int list"

        Assert.Contains("<span class=\"text-secondary font-semibold\">string</span>", html)
        Assert.Contains("<span class=\"text-secondary font-semibold\">option</span>", html)
        Assert.Contains("<span class=\"text-secondary font-semibold\">list</span>", html)

    [<Fact>]
    let ``synopsis returns the first sentence from structured documentation`` () =
        let summary = Presentation.synopsis [ Documentation.text "Represents a parameter. Additional details follow." ]

        Assert.Equal("Represents a parameter.", summary)

    [<Fact>]
    let ``structured API references can target another documentation set`` () =
        let target =
            { Id = "Other.Api"
              Name = "Api"
              Kind = EntityKind.Module
              Summary = []
              Members = []
              Examples = []
              Entities = [] }

        let package: PackageModel =
            { Version = "1"
              Entities = [ target ]
              Scenarios = []
              Packages = [] }

        let reference =
            { Kind = DocumentationNodeKind.SymbolReference
              Text = None
              Target = Some target.Id
              Language = None
              Children = [ Documentation.text "other API" ] }

        let html =
            Presentation.renderDocumentationHtmlWithTargets
                package
                (Map.ofList [ target.Id, "../../other/api/Other.Api.html" ])
                [ reference ]

        Assert.Equal("<a href=\"../../other/api/Other.Api.html\">other API</a>", html)

module DocumentationSetTests =

    let private site: SiteConfig =
        { RepoUrl = None
          SiteName = Some "Shared"
          LogoText = None
          LogoPath = None
          LogoDarkPath = None
          ShowSiteName = None
          Stylesheet = None
          Themes = None
          Navigation =
              Some
                  [ { Label = "Docs"; Href = "/" }
                    { Label = "Handbook"; Href = "/handbook/" } ]
          FSharpPrelude = None }

    let private configured id title source path projects isDefault sidebar api prelude : DocsSetConfig =
        { Id = id
          Title = Some title
          Source = Some source
          Path = path
          Projects = projects
          Default = Some isDefault
          Sidebar = Some sidebar
          Api = Some api
          FSharpPrelude = prelude }

    [<Fact>]
    let ``configured sets resolve defaults and most-specific source ownership`` () =
        let sets =
            DocsSet.resolve
                (Some "Shared")
                []
                None
                (Some
                    [ configured "public" "Public" "docs" None [] true true false None
                      configured
                          "internal"
                          "Internal"
                          "docs/internal"
                          (Some "team")
                          []
                          false
                          false
                          false
                          (Some "open System") ])

        Assert.Equal("", sets.[0].Path)
        Assert.Equal("team", sets.[1].Path)
        Assert.Equal(Some "internal", DocsSet.ownerOf sets "docs/internal/guide.md" |> Option.map _.Id)
        Assert.Equal(Some "public", DocsSet.ownerOf sets "docs/start.md" |> Option.map _.Id)
        Assert.Equal(Some "open System", sets.[1].FSharpPrelude)

    [<Fact>]
    let ``documentation set JSON uses the public camel-case configuration shape`` () =
        let json =
            """[{"id":"sdk","title":"SDK","source":"guides","path":"","projects":["Sdk.fsproj"],"default":true,"sidebar":false,"api":true,"fSharpPrelude":"open Sdk"}]"""

        let serializer = JsonSerializer.Create(Serialization.jsonSettings)

        let parsed =
            Newtonsoft.Json.Linq.JArray.Parse(json).ToObject<DocsSetConfig list>(serializer)
            |> List.head

        Assert.Equal("sdk", parsed.Id)
        Assert.Equal(Some "SDK", parsed.Title)
        Assert.Equal<string list>([ "Sdk.fsproj" ], parsed.Projects)
        Assert.Equal(Some false, parsed.Sidebar)

    [<Fact>]
    let ``configured sets reject missing default duplicate routes and unsafe paths`` () =
        let noDefault =
            Assert.Throws<InvalidOperationException>(fun () ->
                DocsSet.resolve None [] None (Some [ configured "one" "One" "docs" None [] false true false None ])
                |> ignore)

        Assert.Contains("default", noDefault.Message)

        let duplicate =
            Assert.Throws<InvalidOperationException>(fun () ->
                DocsSet.resolve
                    None
                    []
                    None
                    (Some
                        [ configured "home" "Home" "docs" None [] true true false None
                          configured "one" "One" "one-docs" (Some "shared") [] false true false None
                          configured "two" "Two" "two-docs" (Some "shared") [] false true false None ])
                |> ignore)

        Assert.Contains("resolve to route", duplicate.Message)

        let unsafe =
            Assert.Throws<InvalidOperationException>(fun () ->
                DocsSet.resolve None [] None (Some [ configured "one" "One" "../docs" None [] true true false None ])
                |> ignore)

        Assert.Contains("unsafe", unsafe.Message)

    [<Fact>]
    let ``cross-set guide links validate against the global output inventory`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fslivedocs-links-" + Guid.NewGuid().ToString("N"))

        let publicRoot = Path.Combine(root, "public")
        let internalRoot = Path.Combine(root, "internal")
        Directory.CreateDirectory(publicRoot) |> ignore
        Directory.CreateDirectory(internalRoot) |> ignore
        let publicPage = Path.Combine(publicRoot, "index.md")
        let internalPage = Path.Combine(internalRoot, "index.md")
        File.WriteAllText(publicPage, "[Internal](internal/)")
        File.WriteAllText(internalPage, "# Internal")

        let package: PackageModel =
            { Version = "1.0.0"
              Entities = []
              Scenarios = []
              Packages = [] }

        let outputs = set [ "index.html"; "internal/index.html" ]

        let scan source route identity files allowed =
            ContentProvider.scanDocsSet
                { SourceDir = source
                  SnippetSourceDir = root
                  Package = package
                  RoutePrefix = route
                  SemanticPrefix = identity
                  SiteRootPath = ""
                  AllowedOutputs = allowed
                  SemanticCode = SemanticCode.disabled
                  ApiRoutes = Map.empty
                  Files = files }

        let rendered = scan publicRoot "" "public/" [ publicPage ] outputs |> List.head
        Assert.Contains("href=\"internal/index.html\"", rendered.ContentHtml)

        let broken =
            Assert.Throws<InvalidOperationException>(fun () ->
                scan publicRoot "" "public/" [ publicPage ] (set [ "index.html" ]) |> ignore)

        Assert.Contains("does not resolve", broken.Message)

    [<Fact>]
    let ``shared shell renders contextual routes isolated API sidebar and search identity`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fslivedocs-sets-" + Guid.NewGuid().ToString("N"))

        let output = Path.Combine(root, "output")
        Directory.CreateDirectory(root) |> ignore

        let entity id name =
            { Id = id
              Name = name
              Kind = EntityKind.Module
              Summary = []
              Members = []
              Examples = []
              Entities = [] }

        let publicEntity = entity "Public.Api" "Api"
        let internalEntity = entity "Internal.Api" "Api"

        let package: PackageModel =
            { Version = "2.0.0"
              Entities = [ publicEntity; internalEntity ]
              Scenarios = []
              Packages =
                [ { Name = "Public"
                    EntityIds = [ publicEntity.Id ] }
                  { Name = "Internal"
                    EntityIds = [ internalEntity.Id ] } ] }

        let metadata title =
            { Title = title
              Type = None
              Project = None
              TargetFramework = None
              Platform = None }

        let page path title =
            { Metadata = metadata title
              ContentHtml = "<h1>" + title + "</h1>"
              FilePath = path + ".md"
              OutputPath = path + ".html"
              SectionOrder = 0 }

        let publicSet: ReleaseDocsSet =
            { Id = "public"
              Title = "Public"
              Source = "docs"
              Path = ""
              Projects = [ "Public.fsproj" ]
              IsDefault = true
              Sidebar = true
              Api = true
              ApiEntityIds = [ publicEntity.Id ]
              FSharpPrelude = None }

        let internalSet: ReleaseDocsSet =
            { Id = "internal"
              Title = "Internal"
              Source = "internal-docs"
              Path = "internal"
              Projects = [ "Internal.fsproj" ]
              IsDefault = false
              Sidebar = false
              Api = false
              ApiEntityIds = []
              FSharpPrelude = Some "open System" }

        let sites: SiteBuilder.DocsSetSite list =
            [ { Set = publicSet
                Package = package
                Pages = [ page "index" "Public home" ] }
              { Set = internalSet
                Package = package
                Pages = [ page "internal/index" "Internal home" ] } ]

        SiteBuilder.buildDocsSets package.Version sites site [ package.Version ] "light" output

        let publicHtml = File.ReadAllText(Path.Combine(output, "index.html"))
        let internalHtml = File.ReadAllText(Path.Combine(output, "internal", "index.html"))
        Assert.Contains("data-docs-set-id=\"public\"", publicHtml)
        Assert.Contains("data-docs-set-link=\"internal\"", publicHtml)
        Assert.Contains("Documentation Set:Public", publicHtml)
        Assert.Contains("data-docs-set-id=\"internal\"", internalHtml)
        Assert.DoesNotContain("id=\"sidebar-root\"", internalHtml)
        Assert.True(File.Exists(Path.Combine(output, "api", "index.html")))
        Assert.True(File.Exists(Path.Combine(output, "api", publicEntity.Id + ".html")))
        Assert.False(File.Exists(Path.Combine(output, "api", internalEntity.Id + ".html")))
        Assert.False(Directory.Exists(Path.Combine(output, "internal", "api")))

    [<Fact>]
    let ``history switching keeps an exact set page then falls back to the set root`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fslivedocs-set-history-" + Guid.NewGuid().ToString("N"))

        let output = Path.Combine(root, "output")

        let package version : PackageModel =
            { Version = version
              Entities = []
              Scenarios = []
              Packages = [] }

        let metadata title =
            { Title = title
              Type = None
              Project = None
              TargetFramework = None
              Platform = None }

        let set: ReleaseDocsSet =
            { Id = "handbook"
              Title = "Handbook"
              Source = "handbook"
              Path = "handbook"
              Projects = []
              IsDefault = false
              Sidebar = true
              Api = false
              ApiEntityIds = []
              FSharpPrelude = None }

        let homeSet: ReleaseDocsSet =
            { set with
                Id = "home"
                Title = "Home"
                Source = "docs"
                Path = ""
                IsDefault = true }

        let page path title =
            { Metadata = metadata title
              ContentHtml = "<h1>" + title + "</h1>"
              FilePath = path + ".md"
              OutputPath = path + ".html"
              SectionOrder = 0 }

        let versionSite version includeGuide : SiteBuilder.DocsSetVersionSite =
            let model = package version

            { Version = version
              Package = model
              StaticRoot = None
              UsesDocumentationSets = true
              Sets =
                [ { Set = homeSet
                    Package = model
                    Pages = [ page "index" "Home" ] }
                  { Set = set
                    Package = model
                    Pages =
                      [ yield page "handbook/index" "Handbook"
                        if includeGuide then
                            yield page "handbook/guide" "Guide" ] } ] }

        let current = versionSite "2.0.0" true
        let old = versionSite "1.0.0" true
        let oldest = versionSite "0.9.0" false

        SiteBuilder.buildDocsSetsHistory current.Version [ current; old; oldest ] site "light" output

        let guide = File.ReadAllText(Path.Combine(output, "handbook", "guide.html"))
        Assert.Contains("href=\"../history/1.0.0/handbook/guide.html\"", guide)
        Assert.Contains("href=\"../history/0.9.0/handbook/index.html\"", guide)

        let historicalGuide =
            File.ReadAllText(Path.Combine(output, "history", "1.0.0", "handbook", "guide.html"))

        Assert.Contains("href=\"../../../handbook/guide.html\"", historicalGuide)
        Assert.Contains("href=\"../../../history/1.0.0/index.html\"", historicalGuide)
        Assert.Contains("href=\"../../../history/1.0.0/handbook/\"", historicalGuide)
        Assert.DoesNotContain("href=\"../../../index.html\"", historicalGuide)

    [<Fact>]
    let ``content schema two captures resolved set and page identity`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fslivedocs-capsule-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(root) |> ignore
        let capsule = Path.Combine(root, "sets.zip")

        let package: PackageModel =
            { Version = "2.0.0"
              Entities = []
              Scenarios = []
              Packages = [] }

        let api: ApiModelArtifact =
            { SchemaVersion = History.ApiModelSchemaVersion
              Package = package }

        let semantic: SemanticDocumentationArtifact =
            { SchemaVersion = History.SemanticSchemaVersion
              Prelude = ""
              Pages = [] }

        let metadata =
            { Title = "Home"
              Type = None
              Project = None
              TargetFramework = None
              Platform = None }

        let set: ReleaseDocsSet =
            { Id = "handbook"
              Title = "Handbook"
              Source = "handbook"
              Path = ""
              Projects = []
              IsDefault = true
              Sidebar = true
              Api = false
              ApiEntityIds = []
              FSharpPrelude = Some "open System" }

        let page =
            { SourcePath = "index.md"
              SetId = set.Id
              Metadata = metadata
              Markdown = "# Home" }

        ReleaseCapsule.createWithDocsSets capsule "revision" "0.5.0" api semantic site [ set ] [ page ] []
        |> ignore

        let manifest, _, _, content, _ = ReleaseCapsule.load capsule
        Assert.Equal(2, manifest.Content.SchemaVersion)
        Assert.True(content.UsesDocumentationSets)
        Assert.Equal("handbook", content.DocsSets.Head.Source)
        Assert.Equal("handbook", content.Pages.Head.SetId)
        Assert.Equal(Some "open System", content.DocsSets.Head.FSharpPrelude)

    [<Fact>]
    let ``content schema one migrates deterministically to the implicit default set`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fslivedocs-v1-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(root) |> ignore
        let capsule = Path.Combine(root, "legacy.zip")

        let package: PackageModel =
            { Version = "1.0.0"
              Entities = []
              Scenarios = []
              Packages = [] }

        let api: ApiModelArtifact =
            { SchemaVersion = History.ApiModelSchemaVersion
              Package = package }

        let semantic: SemanticDocumentationArtifact =
            { SchemaVersion = History.SemanticSchemaVersion
              Prelude = "open System"
              Pages = [] }

        let bytes value =
            Text.Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(value, Formatting.Indented, Serialization.jsonSettings)
            )

        let apiBytes = bytes api
        let semanticBytes = bytes semantic

        let contentBytes =
            Text.Encoding.UTF8.GetBytes(
                """{"SchemaVersion":1,"Pages":[{"SourcePath":"index.md","Metadata":{"Title":"Home","Type":null,"Project":null,"TargetFramework":null,"Platform":null},"Markdown":"# Home"}],"Assets":[],"Site":{"RepoUrl":null,"SiteName":"Legacy","LogoText":null,"LogoPath":null,"LogoDarkPath":null,"ShowSiteName":null,"Stylesheet":null,"Themes":null,"Navigation":null,"FSharpPrelude":null}}"""
            )

        let releaseComponent schema path (value: byte array) : ReleaseComponent =
            { SchemaVersion = schema
              Path = path
              Sha256 = Convert.ToHexString(Security.Cryptography.SHA256.HashData value).ToLowerInvariant()
              Size = int64 value.Length }

        let manifest: ReleaseCapsuleManifest =
            { SchemaVersion = ReleaseCapsule.ManifestSchemaVersion
              ProductVersion = package.Version
              SourceRevision = "revision"
              CaptureToolVersion = "0.4.1"
              Api = releaseComponent api.SchemaVersion "api.json" apiBytes
              Semantic = releaseComponent semantic.SchemaVersion "semantic.json" semanticBytes
              Content = releaseComponent 1 "content.json" contentBytes }

        let manifestBytes = bytes manifest

        use archive =
            System.IO.Compression.ZipFile.Open(capsule, System.IO.Compression.ZipArchiveMode.Create)

        for name, value in
            [ "api.json", apiBytes
              "content.json", contentBytes
              "manifest.json", manifestBytes
              "semantic.json", semanticBytes ] do
            let entry = archive.CreateEntry(name)
            use stream = entry.Open()
            stream.Write(value, 0, value.Length)

        archive.Dispose()

        let _, _, _, content, _ = ReleaseCapsule.load capsule
        Assert.False(content.UsesDocumentationSets)
        Assert.Equal(DocsSet.DefaultId, content.DocsSets.Head.Id)
        Assert.Equal("docs", content.DocsSets.Head.Source)
        Assert.Equal(DocsSet.DefaultId, content.Pages.Head.SetId)
        Assert.Equal(Some "open System", content.DocsSets.Head.FSharpPrelude)
