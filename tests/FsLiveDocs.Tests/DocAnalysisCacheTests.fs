namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Cli
open FsLiveDocs.Runner

[<Collection("Working directory")>]
module DocAnalysisCacheTests =

    let private runProcess workingDirectory executable arguments =
        let startInfo = Diagnostics.ProcessStartInfo(executable)
        startInfo.WorkingDirectory <- workingDirectory
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        for argument in arguments do startInfo.ArgumentList.Add argument
        use child = Diagnostics.Process.Start startInfo
        let output = child.StandardOutput.ReadToEnd()
        let error = child.StandardError.ReadToEnd()
        child.WaitForExit()
        let commandLine = String.concat " " arguments
        Assert.True(child.ExitCode = 0, $"{executable} {commandLine} failed:\n{output}\n{error}")

    let private withRoot name action =
        let root = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", name + "-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let original = Directory.GetCurrentDirectory()
        try
            Directory.SetCurrentDirectory root
            action root
        finally
            Directory.SetCurrentDirectory original
            if Directory.Exists root then Directory.Delete(root, true)

    let private writeProject (directory: string) (name: string) (framework: string) (source: string) (extraItems: string) =
        Directory.CreateDirectory(directory) |> ignore
        let project = Path.Combine(directory, name + ".fsproj")
        File.WriteAllText(
            project,
            $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>{framework}</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
    {extraItems}
  </ItemGroup>
</Project>""")
        File.WriteAllText(Path.Combine(directory, "Library.fs"), source)
        runProcess directory "dotnet" [ "build"; project; "--nologo"; "-v"; "q" ]
        project

    let private packageFor names =
        { Version = "1.0"
          Entities = []
          Scenarios = []
          Packages = names |> List.map (fun name -> { Name = name; EntityIds = []; Description = None })
          Organization = ApiOrganizationModel.empty }

    let private docsSet root id projects =
        Directory.CreateDirectory(Path.Combine(root, "docs-" + id)) |> ignore
        { Id = id
          Title = id
          Source = "docs-" + id
          Path = ""
          Projects = projects
          IsDefault = true
          Sidebar = true
          Api = true
          FSharpPrelude = None }

    let private analyze set projects package (events: ResizeArray<string * string option * EvaluatedProject>) =
        DocAnalysis.analyzeDocsSetsWithObserver
            (fun _ _ _ -> ())
            (fun event -> events.Add event)
            [ set ]
            projects
            ""
            package

    let private block value =
        String.concat "\n" [ "# Guide"; ""; "```fsharp"; value; "```"; "" ]

    let private withProjectPage text project =
        String.concat "\n" [ "---"; $"project: {project}"; "---"; text ]

    let private assertNoErrors (analysis: DocAnalysis.Analysis) = Assert.Empty analysis.Errors

    [<Fact>]
    let ``page cache changes when only front matter project changes`` () =
        withRoot "front-matter-project" (fun root ->
            let first = writeProject (Path.Combine(root, "First")) "First" "netstandard2.1" "namespace First\nmodule Library = let value = 1" ""
            let second = writeProject (Path.Combine(root, "Second")) "Second" "net8.0" "namespace Second\nmodule Library = let value = 1" ""
            let docs = Path.Combine(root, "docs-docs")
            Directory.CreateDirectory(docs) |> ignore
            let page = Path.Combine(docs, "guide.md")
            File.WriteAllText(page, block "let value = 1")
            let package = packageFor [ "First"; "Second" ]
            let set = docsSet root "docs" [ first; second ]

            let firstEvents = ResizeArray()
            let firstAnalysis = analyze set [ first; second ] package firstEvents
            assertNoErrors firstAnalysis
            Assert.Contains(firstEvents, fun (selected, requested, _) -> selected = Path.GetFullPath first && requested = None)

            File.WriteAllText(page, withProjectPage (block "let value = 1") second)
            let secondEvents = ResizeArray()
            let secondAnalysis = analyze set [ first; second ] package secondEvents
            assertNoErrors secondAnalysis
            Assert.Contains(secondEvents, fun (selected, requested, _) -> selected = Path.GetFullPath second && requested = None)
            Assert.DoesNotContain(secondEvents, fun (selected, _, _) -> selected = Path.GetFullPath first)

            let thirdEvents = ResizeArray()
            let thirdAnalysis = analyze set [ first; second ] package thirdEvents
            assertNoErrors thirdAnalysis
            Assert.Empty thirdEvents

            Directory.Delete(Path.Combine(root, ".livedocs"), true)
            let freshEvents = ResizeArray()
            let freshSet = docsSet root "docs" [ second ]
            let freshAnalysis = analyze freshSet [ second ] package freshEvents
            assertNoErrors freshAnalysis
            Assert.NotEmpty freshEvents
            Assert.Equal<SemanticPage list>(freshAnalysis.Artifact.Value.Pages, secondAnalysis.Artifact.Value.Pages))

    [<Fact>]
    let ``default project reorder invalidates only default selected pages`` () =
        withRoot "default-reorder" (fun root ->
            let first = writeProject (Path.Combine(root, "First")) "First" "netstandard2.1" "namespace First\nmodule Library = let value = 1" ""
            let second = writeProject (Path.Combine(root, "Second")) "Second" "net8.0" "namespace Second\nmodule Library = let value = 1" ""
            let docs = Path.Combine(root, "docs-docs")
            Directory.CreateDirectory(docs) |> ignore
            File.WriteAllText(Path.Combine(docs, "default.md"), block "let value = 1")
            File.WriteAllText(Path.Combine(docs, "explicit.md"), withProjectPage (block "let value = 2") first)
            let package = packageFor [ "First"; "Second" ]
            let set = docsSet root "docs" [ first; second ]

            let firstEvents = ResizeArray()
            assertNoErrors (analyze set [ first; second ] package firstEvents)
            let explicitEvents = firstEvents |> Seq.filter (fun (selected, _, _) -> selected = Path.GetFullPath first) |> Seq.length
            Assert.True(explicitEvents > 0)

            let reordered = { set with Projects = [ second; first ] }
            let secondEvents = ResizeArray()
            assertNoErrors (analyze reordered [ first; second ] package secondEvents)
            Assert.Contains(secondEvents, fun (selected, requested, _) -> selected = Path.GetFullPath second && requested = None)
            Assert.DoesNotContain(secondEvents, fun (selected, _, _) -> selected = Path.GetFullPath first))

    [<Fact>]
    let ``same project in two sets cannot alias page caches`` () =
        withRoot "two-sets" (fun root ->
            let project = writeProject (Path.Combine(root, "Shared")) "Shared" "netstandard2.1" "namespace Shared\nmodule Library = let value = 1" ""
            let firstSet = docsSet root "first" [ project ]
            let secondSet = { docsSet root "second" [ project ] with IsDefault = false }
            File.WriteAllText(Path.Combine(root, "docs-first", "guide.md"), block "let value = 1")
            File.WriteAllText(Path.Combine(root, "docs-second", "guide.md"), block "let value = 1")
            let package = packageFor [ "Shared" ]

            let pagesFirst = DocAnalysis.pagesForDocsSets [ firstSet ] [ project ] package
            let pagesSecond = DocAnalysis.pagesForDocsSets [ secondSet ] [ project ] package
            let firstPage = Assert.Single pagesFirst
            let secondPage = Assert.Single pagesSecond
            Assert.NotEqual<string>(firstPage.Relative, secondPage.Relative)
            Assert.NotEqual<string list>(firstPage.Blocks |> List.map _.Id, secondPage.Blocks |> List.map _.Id)

            let events = ResizeArray()
            let analysis =
                DocAnalysis.analyzeDocsSetsWithObserver
                    (fun _ _ _ -> ())
                    (fun event -> events.Add event)
                    [ firstSet; secondSet ]
                    [ project ]
                    ""
                    package
            assertNoErrors analysis
            Assert.NotEmpty events
            Assert.Equal(2, analysis.Artifact.Value.Pages.Length))

    [<Fact>]
    let ``semantic cache tracks effective reference bytes`` () =
        withRoot "reference-bytes" (fun root ->
            let dependency = Path.Combine(root, "Dependency")
            let dependencyProject = writeProject dependency "Dependency" "netstandard2.1" "namespace Dependency\nmodule Api =\n    /// <summary>First dependency documentation.</summary>\n    let value = 1" ""
            let consumer = Path.Combine(root, "Consumer")
            let consumerProject =
                writeProject
                    consumer
                    "Consumer"
                    "netstandard2.1"
                    "namespace Consumer\nmodule Library = let value = 1"
                    $"""<ProjectReference Include="{dependencyProject}" />"""
            let docs = Path.Combine(root, "docs-docs")
            Directory.CreateDirectory(docs) |> ignore
            File.WriteAllText(Path.Combine(docs, "guide.md"), block "open Dependency\nlet value = Api.value")
            let package = packageFor [ "Consumer" ]
            let set = docsSet root "docs" [ consumerProject ]

            let firstEvents = ResizeArray()
            let firstAnalysis = analyze set [ consumerProject ] package firstEvents
            assertNoErrors firstAnalysis
            let firstPages = firstAnalysis.Artifact.Value.Pages

            File.WriteAllText(
                Path.Combine(dependency, "Library.fs"),
                "namespace Dependency\nmodule Api =\n    /// <summary>Second dependency documentation.</summary>\n    let value = 2")
            runProcess dependency "dotnet" [ "build"; dependencyProject; "--nologo"; "-v"; "q" ]

            let secondEvents = ResizeArray()
            let secondAnalysis = analyze set [ consumerProject ] package secondEvents
            assertNoErrors secondAnalysis
            Assert.Contains(secondEvents, fun (selected, requested, _) -> selected = Path.GetFullPath consumerProject && requested = None)
            Assert.NotEqual<SemanticPage list>(firstPages, secondAnalysis.Artifact.Value.Pages)

            Directory.Delete(Path.Combine(root, ".livedocs"), true)
            let freshEvents = ResizeArray()
            let freshAnalysis = analyze set [ consumerProject ] package freshEvents
            assertNoErrors freshAnalysis
            Assert.NotEmpty freshEvents
            Assert.Equal<SemanticPage list>(freshAnalysis.Artifact.Value.Pages, secondAnalysis.Artifact.Value.Pages))

    [<Fact>]
    let ``pinned semantic cache tracks nondefault outputs`` () =
        withRoot "pinned-output" (fun root ->
            let projectDirectory = Path.Combine(root, "Multi")
            Directory.CreateDirectory projectDirectory |> ignore
            let project = Path.Combine(projectDirectory, "Multi.fsproj")
            File.WriteAllText(
                project,
                """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Common.fs" />
    <Compile Include="Net8.fs" Condition="'$(TargetFramework)' == 'net8.0'" />
  </ItemGroup>
</Project>""")
            File.WriteAllText(Path.Combine(projectDirectory, "Common.fs"), "namespace Multi\nmodule Common = let common () = 1")
            let net8 = Path.Combine(projectDirectory, "Net8.fs")
            File.WriteAllText(net8, "namespace Multi\nmodule Api =\n    /// <summary>First net8 documentation.</summary>\n    let net8 () = 1")
            runProcess projectDirectory "dotnet" [ "build"; project; "--nologo"; "-v"; "q" ]
            let docs = Path.Combine(root, "docs-docs")
            Directory.CreateDirectory(docs) |> ignore
            File.WriteAllText(Path.Combine(docs, "guide.md"), "---\ntargetFramework: net8.0\n---\n# Guide\n\n```fsharp\nopen Multi\nlet value = Api.net8()\n```\n")
            let package = packageFor [ "Multi" ]
            let set = docsSet root "docs" [ project ]

            let firstEvents = ResizeArray()
            let firstAnalysis = analyze set [ project ] package firstEvents
            assertNoErrors firstAnalysis
            let firstPages = firstAnalysis.Artifact.Value.Pages
            Assert.Contains(firstEvents, fun (_, requested, evaluated) -> requested = Some "net8.0" && evaluated.TargetFramework = "net8.0")

            File.WriteAllText(net8, "namespace Multi\nmodule Api =\n    /// <summary>Second net8 documentation.</summary>\n    let net8 () = 2")
            runProcess projectDirectory "dotnet" [ "build"; project; "--nologo"; "-v"; "q" ]

            let secondEvents = ResizeArray()
            let secondAnalysis = analyze set [ project ] package secondEvents
            assertNoErrors secondAnalysis
            Assert.Contains(secondEvents, fun (_, requested, evaluated) -> requested = Some "net8.0" && evaluated.TargetFramework = "net8.0")
            Assert.NotEqual<SemanticPage list>(firstPages, secondAnalysis.Artifact.Value.Pages)

            Directory.Delete(Path.Combine(root, ".livedocs"), true)
            let freshEvents = ResizeArray()
            let freshAnalysis = analyze set [ project ] package freshEvents
            assertNoErrors freshAnalysis
            Assert.NotEmpty freshEvents
            Assert.Equal<SemanticPage list>(freshAnalysis.Artifact.Value.Pages, secondAnalysis.Artifact.Value.Pages))

    [<Fact>]
    let ``pinned and unpinned pages keep distinct framework policies`` () =
        withRoot "pinned-unpinned" (fun root ->
            let projectDirectory = Path.Combine(root, "Multi")
            Directory.CreateDirectory projectDirectory |> ignore
            let project = Path.Combine(projectDirectory, "Multi.fsproj")
            File.WriteAllText(
                project,
                """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
</Project>""")
            File.WriteAllText(Path.Combine(projectDirectory, "Library.fs"), "namespace Multi\nmodule Library = let value = 1")
            runProcess projectDirectory "dotnet" [ "build"; project; "--nologo"; "-v"; "q" ]
            let docs = Path.Combine(root, "docs-docs")
            Directory.CreateDirectory(docs) |> ignore
            File.WriteAllText(Path.Combine(docs, "default.md"), block "open System.IO\nlet info : DirectoryInfo = DirectoryInfo(\".\")")
            File.WriteAllText(Path.Combine(docs, "pinned.md"), "---\ntargetFramework: net8.0\n---\n" + block "open System.IO\nlet info : DirectoryInfo = DirectoryInfo(\".\")")
            let package = packageFor [ "Multi" ]
            let set = docsSet root "docs" [ project ]
            let expectedReferences requested =
                let evaluated =
                    match requested with
                    | None -> DocumentationCompiler.evaluateProject project
                    | Some framework -> DocumentationCompiler.evaluateProjectFor (Some framework) project
                let additions =
                    match requested with
                    | None -> [ ProjectResolver.resolve project |> _.AssemblyPath ]
                    | Some framework ->
                        let target =
                            try (ProjectResolver.documentationBuildFor (Some framework) project).TargetPath
                            with :? InvalidOperationException -> None
                        [ target |> Option.defaultWith (fun () -> ProjectResolver.resolveAssemblyPath project) ]
                (evaluated.References @ additions)
                |> List.filter (String.IsNullOrWhiteSpace >> not)
                |> List.distinctBy (Path.GetFileName >> _.ToUpperInvariant())

            let firstEvents = ResizeArray()
            assertNoErrors (analyze set [ project ] package firstEvents)
            let unpinned = firstEvents |> Seq.find (fun (_, requested, _) -> requested = None)
            let pinned = firstEvents |> Seq.find (fun (_, requested, _) -> requested = Some "net8.0")
            Assert.Equal("netstandard2.1", unpinned |> fun (_, _, evaluated) -> evaluated.TargetFramework)
            Assert.Equal("net8.0", pinned |> fun (_, _, evaluated) -> evaluated.TargetFramework)
            Assert.Equal<string list>(expectedReferences None, unpinned |> fun (_, _, evaluated) -> evaluated.References)
            Assert.Equal<string list>(expectedReferences (Some "net8.0"), pinned |> fun (_, _, evaluated) -> evaluated.References)

            let secondEvents = ResizeArray()
            assertNoErrors (analyze set [ project ] package secondEvents)
            Assert.Empty secondEvents)

    [<Fact>]
    let ``semantic whole artifact and page keys share compiler identity`` () =
        withRoot "shared-compiler-identity" (fun root ->
            let dependency = Path.Combine(root, "Dependency")
            let dependencyProject = writeProject dependency "Dependency" "netstandard2.1" "namespace Dependency\nmodule Api =\n    /// <summary>First documentation.</summary>\n    let value = 1" ""
            let consumer = Path.Combine(root, "Consumer")
            let consumerProject =
                writeProject
                    consumer
                    "Consumer"
                    "netstandard2.1"
                    "namespace Consumer\nmodule Library = let value = 1"
                    $"""<ProjectReference Include="{dependencyProject}" />"""
            let docs = Path.Combine(root, "docs-docs")
            Directory.CreateDirectory(docs) |> ignore
            File.WriteAllText(Path.Combine(docs, "one.md"), block "open Dependency\nlet value = Api.value")
            File.WriteAllText(Path.Combine(docs, "two.md"), block "open Dependency\nlet other = Api.value + 1")
            let package = packageFor [ "Consumer" ]
            let set = docsSet root "docs" [ consumerProject ]

            let warmEvents = ResizeArray()
            assertNoErrors (analyze set [ consumerProject ] package warmEvents)
            let cache = Path.Combine(root, ".livedocs", "cache")
            let backup = Path.Combine(root, "cache-backup")
            Directory.CreateDirectory backup |> ignore
            for file in Directory.GetFiles cache do File.Copy(file, Path.Combine(backup, Path.GetFileName file))

            File.WriteAllText(
                Path.Combine(dependency, "Library.fs"),
                "namespace Dependency\nmodule Api =\n    /// <summary>Second documentation.</summary>\n    let value = 2")
            runProcess dependency "dotnet" [ "build"; dependencyProject; "--nologo"; "-v"; "q" ]

            // Whole-artifact path: remove page caches, keep the old complete artifact.
            for file in Directory.GetFiles(cache, "*.semantic-page.json") do File.Delete file
            let wholeEvents = ResizeArray()
            assertNoErrors (analyze set [ consumerProject ] package wholeEvents)
            Assert.NotEmpty wholeEvents

            // Page path: restore the old caches, then remove only the complete artifact.
            for file in Directory.GetFiles cache do File.Delete file
            for file in Directory.GetFiles backup do File.Copy(file, Path.Combine(cache, Path.GetFileName file))
            for file in Directory.GetFiles(cache, "*.semantic.json") do File.Delete file
            let pageEvents = ResizeArray()
            let pageAnalysis = analyze set [ consumerProject ] package pageEvents
            assertNoErrors pageAnalysis
            Assert.NotEmpty pageEvents

            Directory.Delete(Path.Combine(root, ".livedocs"), true)
            let freshEvents = ResizeArray()
            let freshAnalysis = analyze set [ consumerProject ] package freshEvents
            assertNoErrors freshAnalysis
            Assert.Equal<SemanticPage list>(freshAnalysis.Artifact.Value.Pages, pageAnalysis.Artifact.Value.Pages))
