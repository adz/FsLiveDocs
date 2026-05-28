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
            Entities = [ { Id = "M1"; Name = "add"; Kind = "Module"; SummaryHtml = ""; Members = [ { Id = "M1.add"; Name = "add"; Signature = "int -> int"; Parameters = []; ReturnType = "int"; SummaryHtml = ""; RemarksHtml = ""; Examples = [ { Name = "E1"; Content = "1+1"; ExpectedOutput = None; Scenario = None } ]; Location = { File = ""; Line = 0 } } ]; Examples = []; Entities = [] } ]
            Scenarios = []
        }
        let body = "Look at {{< example id=\"E1\" >}} and xref:M:M1.add"
        let resolved = ContentProvider.resolveSnippets body "." package "/"
        Assert.Contains("```fsharp\n1+1\n```", resolved)
        Assert.Contains("[add](/api/M1.html#M1.add)", resolved)

module DocTestRunnerTests =

    [<Fact>]
    let ``generateTestProject creates valid fsproj content`` () =
        let temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        Directory.CreateDirectory(temp) |> ignore
        let examples = [ { Name = "T1"; Content = "printfn \"hi\""; ExpectedOutput = None; Scenario = None } ]
        let proj = DocTestRunner.generateTestProject examples [] "Sample.fsproj" [] temp
        let content = File.ReadAllText(proj)
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", content)
        Assert.Contains("<ProjectReference Include=\"", content)
        Assert.Contains("Sample.fsproj", content)
        
        let prog = File.ReadAllText(Path.Combine(temp, "Program.fs"))
        Assert.Contains("printfn \"--- TEST: T1 ---\"", prog)
        Assert.Contains("printfn \"hi\"", prog)

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
        Assert.Contains("Use a record when you want a single value made up of named fields.", html)
        Assert.DoesNotContain("Specification", html)
