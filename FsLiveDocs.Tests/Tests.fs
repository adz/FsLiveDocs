namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Runner

module SymbolListerTests =

    [<Fact>]
    let ``normalizeName removes Module and backticks`` () =
        Assert.Equal("My", SymbolLister.normalizeName "MyModule")
        Assert.Equal("List", SymbolLister.normalizeName "List`1")
        Assert.Equal("Map", SymbolLister.normalizeName "Map`2")

    [<Fact>]
    let ``extractExamples parses example tags correctly`` () =
        let xml = """
        <summary>Test</summary>
        <example name="Test1">
        let x = 1
        // EXPECTED: 1
        </example>
        <example scenario="S1">
        let y = 2
        </example>
        """
        let examples = SymbolLister.extractExamples xml
        Assert.Equal(2, examples.Length)
        Assert.Equal("Test1", examples.[0].Name)
        Assert.Equal("let x = 1", examples.[0].Content)
        Assert.Equal(Some "1", examples.[0].ExpectedOutput)
        Assert.Equal("Example", examples.[1].Name)
        Assert.Equal("let y = 2", examples.[1].Content)
        Assert.Equal(Some "S1", examples.[1].Scenario)

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
        let resolved = ContentProvider.resolveSnippets body "." package
        Assert.Contains("```fsharp\n1+1\n```", resolved)
        Assert.Contains("[add](/api.html#M1.add)", resolved)

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
