namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Runner

module SymbolListerTests =

    [<Fact>]
    let ``Placeholder for new SymbolLister tests`` () =
        // We will add new tests as we verify the FSharp.Formatting integration
        Assert.True(true)

    [<Fact>]
    let ``merge removes empty synthetic Default namespace`` () =
        let child = { Id = "Default.Sample"; Name = "Sample"; Kind = "Module"; SummaryHtml = ""; Members = []; Entities = [] }
        let defaultNamespace = { Id = "Default"; Name = "Default"; Kind = "Namespace"; SummaryHtml = ""; Members = []; Entities = [ child ] }
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
            Entities = [ { Id = "M1"; Name = "add"; Kind = "Module"; SummaryHtml = ""; Members = [ { Id = "M1.add"; Name = "add"; Signature = "int -> int"; Parameters = []; ReturnType = "int"; SummaryHtml = ""; RemarksHtml = ""; Examples = [ { Name = "E1"; Content = "1+1"; ExpectedOutput = None; Scenario = None } ]; Location = { File = ""; Line = 0 } } ]; Entities = [] } ]
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
