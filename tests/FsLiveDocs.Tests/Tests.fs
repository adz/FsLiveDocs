namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Runner
open FsLiveDocs.Renderer

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
        let child = { Id = "Default.Sample"; Name = "Sample"; Kind = "Module"; SummaryHtml = ""; Members = []; Examples = []; Entities = [] }
        let defaultNamespace = { Id = "Default"; Name = "Default"; Kind = "Namespace"; SummaryHtml = ""; Members = []; Examples = []; Entities = [ child ] }
        let package = SymbolLister.merge [ { Version = "1.0"; Entities = [ defaultNamespace; child ]; Scenarios = [] } ]

        let onlyEntity = Assert.Single(package.Entities)
        Assert.Equal("Default.Sample", onlyEntity.Id)
        Assert.Equal("Sample", onlyEntity.Name)

module ContentProviderTests =

    [<Fact>]
    let ``parseFrontMatter extracts yaml and body`` () =
        let content = "---\ntitle: Hello\nweight: 10\n---\nBody here"
        match ContentProvider.parseFrontMatter content with
        | Some (meta, body) ->
            Assert.Equal("Hello", meta.Title)
            Assert.Equal(10, meta.Weight)
            Assert.Equal("Body here", body.Trim())
        | None -> Assert.Fail("Should have parsed frontmatter")

    [<Fact>]
    let ``resolveSnippets handles transclusion and xrefs`` () =
        let package : PackageModel = { 
            Version = "1.0"
            Entities = [ { Id = "M1"; Name = "add"; Kind = "Module"; SummaryHtml = ""; Members = [ { Id = "M1.add"; Name = "add"; Signature = "int -> int"; Parameters = []; ReturnType = "int"; SummaryHtml = ""; RemarksHtml = ""; Examples = [ { Name = "E1"; Content = "1+1"; ExpectedOutput = None; Scenario = None; IsSnapshotTest = false } ]; Location = { File = ""; Line = 0 } } ]; Examples = []; Entities = [] } ]
            Scenarios = []
        }
        let body = "Look at {{< example id=\"E1\" >}} and xref:M:M1.add"
        let resolved = ContentProvider.resolveSnippets body "." package "/"
        Assert.Contains("```fsharp\n1+1\n```", resolved)
        Assert.Contains("[add](/api/M1.html#M1.add)", resolved)

module DocTestRunnerTests =

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
                            Kind = "Module"
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
                Scenarios = []
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
                            Kind = "Module"
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
                Scenarios = []
            }

        let projectPath = Path.GetFullPath("src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj")
        let snapshot = DocTestRunner.collectSnapshots package projectPath [] |> Async.RunSynchronously
        let example = Assert.Single(snapshot.Examples)
        Assert.Equal("verified", example.Status)
        Assert.Equal(Some "val x: int = 1\nval it: int = 3", example.ExpectedOutput)

module ViewTests =

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
                Kind = "Record"
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

        let package : PackageModel = { Version = "1.0"; Entities = [ recordEntity ]; Scenarios = [] }
        let html = SiteBuilder.renderEntityPage recordEntity [] package { RepoUrl = None } [] "light" "../"

        Assert.Contains("Fields", html)
        Assert.Contains("Represents a parameter of a function or method.", html)
        Assert.DoesNotContain("Specification", html)

module SiteBuilderTests =

    [<Fact>]
    let ``generateLlmsTxt includes the expected heading`` () =
        let package : PackageModel = { Version = "1.0"; Entities = []; Scenarios = [] }
        let summary = SiteBuilder.generateLlmsTxt package

        Assert.StartsWith("# API Reference for LLMs", summary)
