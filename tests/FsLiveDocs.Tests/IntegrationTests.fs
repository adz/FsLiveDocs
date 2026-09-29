namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Cli
open FsLiveDocs.Runner

module IntegrationTests =

    let private coreProject = ProjectResolver.resolveProjectPath "FsLiveDocs.Core.fsproj"

    [<Fact>]
    let ``documentation compiler uses project references and page scope without executing`` () = async {
        let blocks =
            DocumentationDiscovery.discoverMarkdown
                "guide.md"
                (Some coreProject)
                "```fsharp\nopen FsLiveDocs.Core\nlet package : PackageModel = { Version = \"1\"; Entities = []; Scenarios = []; Packages = []; Organization = ApiOrganizationModel.empty }\n```\n```fsharp\nlet version : string = package.Version\n```"
        let! results = DocumentationCompiler.checkBlocks coreProject "" blocks
        let result = Assert.Single(results)
        let errors = result.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
        Assert.Empty(errors)
        let artifact = SemanticExtractor.artifact results
        let semanticBlocks = artifact.Pages |> List.collect _.Blocks
        let semanticBlock = List.head semanticBlocks
        let signatures = semanticBlock.Tooltips |> List.choose _.Signature
        Assert.Contains(signatures, fun signature -> signature.Contains("package:") && signature.Contains("PackageModel"))
        Assert.Contains(semanticBlock.Tooltips, fun tooltip ->
            tooltip.Documentation
            |> Option.exists (fun documentation -> documentation.Contains("root model representing a documented package")))
        Assert.Contains(semanticBlock.Tooltips, fun tooltip -> tooltip.Signature |> Option.exists _.StartsWith("package:") && tooltip.Footer.IsNone)
        Assert.All(semanticBlocks |> List.collect _.Tooltips, fun tooltip -> Assert.True(tooltip.Footer.IsNone))
        let original = semanticBlock.Lines |> List.map (fun line -> line.Tokens |> List.map _.Text |> String.concat "") |> String.concat "\n"
        Assert.Equal(blocks.[0].ExpandedSource, original)
    }

    [<Fact>]
    let ``batched documentation project preserves page isolation and semantic meaning`` () = async {
        let evaluated = DocumentationCompiler.evaluateProject coreProject
        let first =
            DocumentationDiscovery.discoverMarkdown
                "first.md"
                (Some coreProject)
                "```fsharp\nlet pagePrivateValue = 42\n```"
        let second =
            DocumentationDiscovery.discoverMarkdown
                "second.md"
                (Some coreProject)
                "```fsharp\nlet illegalLeak: int = pagePrivateValue\n```"
        let standaloneFirst = DocumentationCompiler.checkBlocksWithProject evaluated "" first
        let! standalone = standaloneFirst
        let! batched =
            DocumentationCompiler.checkPagesWithProject
                evaluated
                [ "first", "", first
                  "second", "", second ]

        let firstBatch = batched |> Map.find "first"
        let secondBatch = batched |> Map.find "second"
        let standaloneArtifact = SemanticExtractor.artifact standalone
        let batchedArtifact = SemanticExtractor.artifact firstBatch

        Assert.Equal<SemanticPage list>(standaloneArtifact.Pages, batchedArtifact.Pages)
        Assert.Empty(firstBatch |> List.collect _.Diagnostics |> List.filter (fun item -> item.Severity = SemanticDiagnosticSeverity.Error))
        Assert.NotEmpty(secondBatch |> List.collect _.Diagnostics |> List.filter (fun item -> item.Severity = SemanticDiagnosticSeverity.Error))
    }

    [<Fact>]
    let ``batched documentation pages are wrapped in public modules`` () = async {
        let evaluated = DocumentationCompiler.evaluateProject coreProject
        let page =
            DocumentationDiscovery.discoverMarkdown "public.md" (Some coreProject) "```fsharp\ntype Shape = { Sides: int }\n```"
        let other =
            DocumentationDiscovery.discoverMarkdown "other.md" (Some coreProject) "```fsharp\nlet other = 1\n```"

        let! batched = DocumentationCompiler.checkPagesWithProject evaluated [ "public", "", page; "other", "", other ]

        let source = batched |> Map.find "public" |> List.head |> _.SyntheticSource
        Assert.Contains("module FsLiveDocsGeneratedPage", source)
        Assert.DoesNotContain("module internal", source)
    }

    [<Fact>]
    let ``batched documentation project keeps entry points in standalone final files`` () = async {
        let evaluated = DocumentationCompiler.evaluateProject coreProject
        let entryPoint =
            DocumentationDiscovery.discoverMarkdown
                "entrypoint.md"
                (Some coreProject)
                "```fsharp\n[<EntryPoint>]\nlet main _ = 0\n```"
        let ordinary =
            DocumentationDiscovery.discoverMarkdown
                "ordinary.md"
                (Some coreProject)
                "```fsharp\nlet ordinary = 1\n```"

        let! checkedPages =
            DocumentationCompiler.checkPagesWithProject
                evaluated
                [ "entrypoint", "", entryPoint
                  "ordinary", "", ordinary ]

        Assert.Empty(checkedPages |> Map.toList |> List.collect (snd >> List.collect _.Diagnostics) |> List.filter (fun item -> item.Severity = SemanticDiagnosticSeverity.Error))
    }

    [<Fact>]
    let ``documentation compiler checks blocks correctly when compilation units exceed the checker pool size`` () = async {
        let markdown =
            [ 1 .. 8 ]
            |> List.map (fun index -> $"```fsharp isolated\nlet value{index} : int = {index}\n```")
            |> String.concat "\n"
        let blocks = DocumentationDiscovery.discoverMarkdown "guide.md" (Some coreProject) markdown
        let! results = DocumentationCompiler.checkBlocks coreProject "" blocks
        Assert.Equal(8, results.Length)
        Assert.All(results, fun result ->
            let errors = result.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
            Assert.Empty(errors))
        for index in 1 .. 8 do
            let result = results.[index - 1]
            Assert.Contains($"value{index}", result.SyntheticSource)
            for other in 1 .. 8 do
                if other <> index then
                    Assert.DoesNotContain($"value{other}", result.SyntheticSource)
    }

    [<Fact>]
    let ``diagnostic-only checking streams isolated blocks without retaining check results`` () = async {
        let projectPath = coreProject
        let evaluated = DocumentationCompiler.evaluateProject projectPath
        let markdown = "```fsharp isolated\nlet value = 1\n```\n\n```fsharp isolated\nlet broken: int = \"wrong\"\n```"
        let blocks = DocumentationDiscovery.discoverMarkdown "streamed.md" (Some projectPath) markdown

        let! diagnostics = DocumentationCompiler.checkBlocksForDiagnosticsWithProject evaluated "" blocks

        Assert.Single(diagnostics |> List.filter (fun item -> item.Severity = SemanticDiagnosticSeverity.Error)) |> ignore
    }

    let ``checkBlocksWithProject checks blocks using a pre-evaluated project`` () = async {
        let evaluated = DocumentationCompiler.evaluateProject coreProject
        let blocks =
            DocumentationDiscovery.discoverMarkdown
                "guide.md"
                (Some coreProject)
                "```fsharp\nopen FsLiveDocs.Core\nlet package : PackageModel = { Version = \"1\"; Entities = []; Scenarios = []; Packages = []; Organization = ApiOrganizationModel.empty }\n```"
        let! results = DocumentationCompiler.checkBlocksWithProject evaluated "" blocks
        let result = Assert.Single(results)
        let errors = result.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
        Assert.Empty(errors)
    }

    [<Fact>]
    let ``documentation compiler checks blocks from two different projects concurrently without cross-project diagnostic bleed`` () = async {
        let secondDirectory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", "ConcurrentSecondProject")
        if Directory.Exists(secondDirectory) then Directory.Delete(secondDirectory, true)
        Directory.CreateDirectory(secondDirectory) |> ignore
        let secondProject = Path.Combine(secondDirectory, "ConcurrentSecondProject.fsproj")
        File.WriteAllText(
            secondProject,
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
</Project>""")
        File.WriteAllText(
            Path.Combine(secondDirectory, "Library.fs"),
            "namespace ConcurrentSecondProject\ntype SecondProjectOnlyMarker = { Value: int }")
        let build = System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo("dotnet", $"build \"{secondProject}\" --nologo"))
        build.WaitForExit()
        Assert.Equal(0, build.ExitCode)

        let coreBlocks =
            DocumentationDiscovery.discoverMarkdown
                "core-guide.md"
                (Some coreProject)
                "```fsharp\nopen FsLiveDocs.Core\nlet package : PackageModel = { Version = \"1\"; Entities = []; Scenarios = []; Packages = []; Organization = ApiOrganizationModel.empty }\n```"
        let secondBlocks =
            DocumentationDiscovery.discoverMarkdown
                "second-guide.md"
                (Some secondProject)
                "```fsharp\nopen ConcurrentSecondProject\nlet marker : SecondProjectOnlyMarker = { Value = 1 }\n```"

        let! results =
            Async.Parallel(
                [ DocumentationCompiler.checkBlocks coreProject "" coreBlocks
                  DocumentationCompiler.checkBlocks secondProject "" secondBlocks ])
        let coreResults, secondResults = results.[0], results.[1]

        let coreErrors = coreResults |> List.collect _.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
        let secondErrors = secondResults |> List.collect _.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
        Assert.Empty(coreErrors)
        Assert.Empty(secondErrors)
        Assert.DoesNotContain(coreResults |> List.collect _.Diagnostics, fun diagnostic -> diagnostic.Message.Contains("SecondProjectOnlyMarker"))
        Assert.DoesNotContain(secondResults |> List.collect _.Diagnostics, fun diagnostic -> diagnostic.Message.Contains("PackageModel"))
    }

    [<Fact>]
    let ``transcript references do not assume assembly names are namespaces`` () =
        let context : FsiTranscriptRunner.DocTestExecutionContext =
            { Project =
                { ProjectPath = coreProject
                  AssemblyPath = typeof<PackageModel>.Assembly.Location
                  ProjectNamespace = "FsLiveDocs.Core" }
              References = [ typeof<FsLiveDocs.DocScenarioAttribute>.Assembly.Location ]
              Scenario = None
              Example =
                { Name = "reference-name"
                  Content = "> 20 + 22;;\nval it: int = 42"
                  ExpectedOutput = None
                  Scenario = None
                  IsSnapshotTest = false
                  NoCheckReason = None } }

        let output, _, _ = FsiTranscriptRunner.runExample context
        Assert.Equal("val it: int = 42", output)

    [<Fact>]
    let ``documentation compiler maps errors back to owning block coordinates`` () = async {
        let blocks =
            DocumentationDiscovery.discoverMarkdown
                "guides/broken.md"
                (Some coreProject)
                "```fsharp prepare\nlet available = 42\n```\n```fsharp\n\nlet value : MissingDocumentationType = available\n```"
        let! results = DocumentationCompiler.checkBlocks coreProject "" blocks
        let diagnostic =
            results
            |> List.collect _.Diagnostics
            |> List.find (fun item -> item.Severity = SemanticDiagnosticSeverity.Error && item.Message.Contains("MissingDocumentationType"))
        Assert.Equal(Some "guides/broken.md#fsharp-1", diagnostic.BlockId)
        Assert.Equal("guides/broken.md", diagnostic.SourcePath)
        Assert.Equal(2, diagnostic.StartLine)
    }

    [<Fact>]
    let ``audit diagnostic details include every compiler error with coordinates`` () = async {
        let blocks =
            DocumentationDiscovery.discoverMarkdown
                "guides/two-errors.md"
                (Some coreProject)
                "```fsharp\nlet first : MissingDocumentationTypeOne = Unchecked.defaultof<_>\nlet second : MissingDocumentationTypeTwo = Unchecked.defaultof<_>\n```"
        let! results = DocumentationCompiler.checkBlocks coreProject "" blocks
        let errors =
            results
            |> List.collect _.Diagnostics
            |> List.filter (fun diagnostic ->
                diagnostic.Severity = SemanticDiagnosticSeverity.Error
                && (diagnostic.Message.Contains("MissingDocumentationTypeOne")
                    || diagnostic.Message.Contains("MissingDocumentationTypeTwo")))
        let detail =
            errors
            |> List.map (fun diagnostic -> diagnostic.StartLine, diagnostic.StartColumn, diagnostic.Message)
            |> Actions.formatAuditErrors

        Assert.Equal(2, errors.Length)
        Assert.Contains($"{errors.[0].StartLine}:{errors.[0].StartColumn}", detail)
        Assert.Contains($"{errors.[1].StartLine}:{errors.[1].StartColumn}", detail)
        Assert.Contains("MissingDocumentationTypeOne", detail)
        Assert.Contains("MissingDocumentationTypeTwo", detail)
    }

    [<Fact>]
    let ``documentation compiler evaluates a cross-targeting project in an inner build`` () =
        let directory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore
        let projectPath = Path.Combine(directory, "MultiTarget.fsproj")
        File.WriteAllText(
            projectPath,
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>""")
        let restore = System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo("dotnet", $"restore \"{projectPath}\" --nologo"))
        restore.WaitForExit()
        Assert.Equal(0, restore.ExitCode)

        let evaluated = DocumentationCompiler.evaluateProject projectPath

        Assert.Equal("netstandard2.1", evaluated.TargetFramework)
        Assert.NotEmpty(evaluated.References)

        let net8 = DocumentationCompiler.evaluateProjectFor (Some "net8.0") projectPath
        Assert.Equal("net8.0", net8.TargetFramework)

        let unsupported = Assert.Throws<InvalidOperationException>(fun () -> DocumentationCompiler.evaluateProjectFor (Some "net6.0") projectPath |> ignore)
        Assert.Contains("is not declared", unsupported.Message)

    [<Fact>]
    let ``documentation compiler falls back to the built Release configuration`` () =
        let directory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", Guid.NewGuid().ToString("N"))
        let libraryDirectory = Path.Combine(directory, "Referenced")
        let appDirectory = Path.Combine(directory, "Documented")
        Directory.CreateDirectory(libraryDirectory) |> ignore
        Directory.CreateDirectory(appDirectory) |> ignore
        let libraryProject = Path.Combine(libraryDirectory, "Referenced.fsproj")
        let appProject = Path.Combine(appDirectory, "Documented.fsproj")
        File.WriteAllText(Path.Combine(libraryDirectory, "Library.fs"), "namespace Referenced\ntype PublicType = { Value: int }")
        File.WriteAllText(libraryProject, """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="Library.fs" /></ItemGroup>
</Project>""")
        File.WriteAllText(Path.Combine(appDirectory, "Library.fs"), "namespace Documented\nmodule Library = let value = 42")
        File.WriteAllText(appProject, $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="Library.fs" /></ItemGroup>
  <ItemGroup><ProjectReference Include="{libraryProject}" /></ItemGroup>
</Project>""")
        let build = System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo("dotnet", $"build \"{appProject}\" --configuration Release --nologo"))
        build.WaitForExit()
        Assert.Equal(0, build.ExitCode)

        let evaluated = DocumentationCompiler.evaluateProject appProject
        Assert.Contains(evaluated.References, fun reference ->
            reference.EndsWith("Referenced.dll", StringComparison.Ordinal)
            && reference.Contains($"{Path.DirectorySeparatorChar}release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))

    [<Fact>]
    let ``evaluating an unrestored project explains that it needs restoring`` () =
        // A project outside the solution is never restored by a solution-level build, so this is
        // what a caller hits after passing a project the solution does not build.
        let directory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore
        let projectPath = Path.Combine(directory, "Unrestored.fsproj")
        File.WriteAllText(
            projectPath,
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>""")

        let failure =
            Assert.Throws<InvalidOperationException>(fun () -> DocumentationCompiler.evaluateProject projectPath |> ignore)

        Assert.Contains("is not restored", failure.Message)
        Assert.Contains("dotnet restore", failure.Message)
        Assert.Contains("Unrestored.fsproj", failure.Message)

    [<Fact>]
    let ``evaluating a project that fails to build reports the mixed-stream MSBuild error`` () =
        let directory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore
        let projectPath = Path.Combine(directory, "BrokenBuild.fsproj")
        File.WriteAllText(
            projectPath,
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
  </ItemGroup>
  <Target Name="FailDeliberately" BeforeTargets="ResolveReferences">
    <Error Text="Deliberate FsLiveDocsTests build failure FS0010" />
  </Target>
</Project>""")
        File.WriteAllText(
            Path.Combine(directory, "Library.fs"),
            "module Library =\n    let value = 1\n")

        let restore = System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo("dotnet", $"restore \"{projectPath}\" --nologo"))
        restore.WaitForExit()
        Assert.Equal(0, restore.ExitCode)

        let failure =
            Assert.Throws<InvalidOperationException>(fun () -> DocumentationCompiler.evaluateProject projectPath |> ignore)

        Assert.Contains("MSBuild evaluation failed for", failure.Message)
        Assert.Contains("Deliberate FsLiveDocsTests build failure FS0010", failure.Message)

    let createTestProject dirName files =
        let baseDir = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", dirName)
        if Directory.Exists(baseDir) then Directory.Delete(baseDir, true)
        Directory.CreateDirectory(baseDir) |> ignore
        
        let projFile = Path.Combine(baseDir, dirName + ".fsproj")
        let projContent = $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    {files |> List.map (fun f -> $"<Compile Include=\"{f}\" />") |> String.concat "\n    "}
  </ItemGroup>
</Project>"""
        File.WriteAllText(projFile, projContent)
        
        for (name, content) in files |> List.map (fun (f: string) -> f, "namespace TestNamespace\nmodule " + Path.GetFileNameWithoutExtension(f) + " = let x = 1") do
            File.WriteAllText(Path.Combine(baseDir, name), content)
            
        projFile

    /// Writes a project whose source files carry the supplied content verbatim, then builds it.
    let private buildProjectWithSource dirName (files: (string * string) list) =
        let baseDir = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", dirName)
        if Directory.Exists(baseDir) then Directory.Delete(baseDir, true)
        Directory.CreateDirectory(baseDir) |> ignore

        let projFile = Path.Combine(baseDir, dirName + ".fsproj")
        let compileItems =
            files |> List.map (fun (name, _) -> $"<Compile Include=\"{name}\" />") |> String.concat "\n    "
        File.WriteAllText(
            projFile,
            $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    {compileItems}
  </ItemGroup>
</Project>""")

        for (name, content) in files do
            File.WriteAllText(Path.Combine(baseDir, name), content)

        let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"build \"{projFile}\"")
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        let proc = System.Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        projFile

    [<Fact>]
    let ``extraction shows the source pattern for a parameter destructured in the parameter list`` () = async {
        // A union destructured directly in the parameter list has no source-level name, so
        // the usage signature and the parameter table would each invent a different one.
        let source =
            """namespace TestNamespace

module Destructured =
    type ColdTask<'value> = ColdTask of (int -> 'value)

    /// <summary>Runs it.</summary>
    let run (token: int) (ColdTask operation) = operation token
"""

        let projFile = buildProjectWithSource "UnnamedParameter" [ "Destructured.fs", source ]

        let! package, diagnostics = SymbolLister.extractFromProjectWithDiagnostics projFile

        let rec allMembers (entities: EntityModel list) =
            entities |> List.collect (fun e -> e.Members @ allMembers e.Entities)

        let run = allMembers package.Entities |> List.find (fun m -> m.Name.StartsWith("run"))

        // The pattern the author wrote is shown, in both renderings, rather than a placeholder.
        Assert.Equal<string list>([ "token"; "ColdTask operation" ], run.Parameters |> List.map _.Name)
        Assert.Contains("(ColdTask operation)", run.Signature)
        Assert.DoesNotContain("arg", run.Signature)

        // Presentable, so nothing to report.
        Assert.Empty(diagnostics)
    }

    [<Fact>]
    let ``extraction accepts named and unit parameters`` () = async {
        let source =
            """namespace TestNamespace

module Named =
    type ColdTask<'value> = ColdTask of (int -> 'value)

    /// <summary>Runs it.</summary>
    let run (token: int) (coldTask: ColdTask<'value>) =
        let (ColdTask operation) = coldTask
        operation token

    /// <summary>Does nothing.</summary>
    let reset () = ()
"""

        let projFile = buildProjectWithSource "NamedParameter" [ "Named.fs", source ]

        let! package = SymbolLister.extractFromProject projFile

        let rec allMembers (entities: EntityModel list) =
            entities |> List.collect (fun e -> e.Members @ allMembers e.Entities)

        let run = allMembers package.Entities |> List.find (fun m -> m.Name.StartsWith("run"))

        // The names come from source, and each also appears in the rendered signature.
        Assert.Equal<string list>([ "token"; "coldTask" ], run.Parameters |> List.map _.Name)
        for parameter in run.Parameters do
            Assert.Contains(parameter.Name, run.Signature)
    }

    [<Fact>]
    let ``overloaded members receive distinct stable IDs`` () = async {
        let source =
            """namespace TestNamespace

open System.Text

/// <summary>Writes values.</summary>
type IWriter =
    /// <summary>Writes values.</summary>
    abstract Write : path: string * values: seq<string> -> unit

    /// <summary>Writes values with an encoding.</summary>
    abstract Write : path: string * values: seq<string> * encoding: Encoding -> unit
"""

        let projFile = buildProjectWithSource "OverloadedMembers" [ "Writer.fs", source ]
        let! package = SymbolLister.extractFromProject projFile

        let rec allMembers (entities: EntityModel list) =
            entities |> List.collect (fun entity -> entity.Members @ allMembers entity.Entities)

        let overloads =
            allMembers package.Entities
            |> List.filter (fun memberModel -> memberModel.Id.StartsWith("TestNamespace.IWriter.Write"))

        Assert.Equal(2, overloads.Length)
        Assert.Equal<string list>(
            [ "TestNamespace.IWriter.Write(System.String,System.Collections.Generic.IEnumerable{System.String})"
              "TestNamespace.IWriter.Write(System.String,System.Collections.Generic.IEnumerable{System.String},System.Text.Encoding)" ],
            overloads |> List.map _.Id |> List.sort)
    }

    [<Fact>]
    let ``Full Extraction and Merging Integration`` () = async {
        let files = [ "File1.fs"; "File2.fs" ]
        let projFile = createTestProject "Integration1" files
        
        // We MUST build the project because ApiDocs reads the DLL
        let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"build {projFile}")
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        let proc = System.Diagnostics.Process.Start(psi)
        proc.WaitForExit()

        let! flatPackage = SymbolLister.extractFromProject projFile
        let package = SymbolLister.merge [flatPackage]
        
        // FSharp.Formatting might organize things differently. 
        // It often has a top-level entity for the assembly or namespace.
        Assert.NotEmpty(package.Entities)
        
        let rec findEntity id (entities: EntityModel list) =
            entities |> List.tryPick (fun e ->
                if e.Id = id then Some e
                else findEntity id e.Entities
            )

        let ns = findEntity "TestNamespace" package.Entities
        Assert.True(ns.IsSome, "Should find TestNamespace")
        
        let nsVal = ns.Value
        Assert.Equal(EntityKind.Namespace, nsVal.Kind)
        
        // Should have two modules as children
        Assert.Equal(2, nsVal.Entities.Length)
        let moduleNames = nsVal.Entities |> List.map (fun e -> e.Name) |> Set.ofList
        Assert.Contains("File1", moduleNames)
        Assert.Contains("File2", moduleNames)
    }

    let private annotationsContext content : FsiTranscriptRunner.DocTestExecutionContext =
        { Project =
            { ProjectPath = coreProject
              AssemblyPath = typeof<FsLiveDocs.DocScenarioAttribute>.Assembly.Location
              ProjectNamespace = "FsLiveDocs" }
          References = []
          Scenario = None
          Example =
            { Name = "isolation"
              Content = content
              ExpectedOutput = None
              Scenario = None
              IsSnapshotTest = false
              NoCheckReason = None } }

    [<Fact>]
    let ``transcripts run in a worker process, not the process running FsLiveDocs`` () =
        let output, _, _ =
            FsiTranscriptRunner.runExample (annotationsContext $"> System.Environment.ProcessId = {Environment.ProcessId};;")

        Assert.Equal("val it: bool = false", output)

    [<Fact>]
    let ``the transcript worker's dependency graph excludes the tool's own libraries`` () =
        // The worker exists so a documented project's dependencies are never unified with the tool's. If it gained a
        // reference to FsLiveDocs.Core or Axial, the tool's copies would load before the documented project's again.
        let runnerDirectory = Path.GetDirectoryName(typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly.Location)
        let manifest = File.ReadAllText(Path.Combine(runnerDirectory, "FsLiveDocs.TranscriptHost.deps.json"))
        Assert.DoesNotContain("Axial", manifest)
        Assert.DoesNotContain("FsLiveDocs.Core", manifest)
        Assert.DoesNotContain("FsLiveDocs.Runner", manifest)

    [<Fact>]
    let ``multiple examples share one worker session and keep their own output`` () =
        let first = annotationsContext "> let shared = 20;;\nval shared: int = 20"
        let second = annotationsContext "> shared + 22;;\nval it: int = 42"
        let outputs = FsiTranscriptRunner.runExamples [ first; second ] |> List.map (fun (output, _, _) -> output)
        Assert.Equal<string list>([ "val shared: int = 20"; "val it: int = 42" ], outputs)

    [<Fact>]
    let ``a transcript that outlives its time limit stops the worker`` () =
        let stopwatch = Diagnostics.Stopwatch.StartNew()
        let error =
            Assert.Throws<InvalidOperationException>(fun () ->
                TranscriptHostClient.runWithin
                    (TimeSpan.FromSeconds 3.0)
                    [ { Blocks = [| "System.Threading.Thread.Sleep 60000" |]; SetupCount = 0 } ]
                |> ignore)
        Assert.Contains("timed out", error.Message)
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds 30.0, $"The worker was not stopped promptly: {stopwatch.Elapsed}")

    let private runWorker (input: string) =
        let runnerDirectory = Path.GetDirectoryName(typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly.Location)
        let start =
            Diagnostics.ProcessStartInfo(
                "dotnet",
                $"exec \"{Path.Combine(runnerDirectory, FsLiveDocs.TranscriptHost.Protocol.WorkerFileName)}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true)
        use worker = Diagnostics.Process.Start start
        worker.StandardInput.Write input
        worker.StandardInput.Close()
        let output = worker.StandardOutput.ReadToEnd()
        worker.StandardError.ReadToEnd() |> ignore
        worker.WaitForExit()
        worker.ExitCode, FsLiveDocs.TranscriptHost.Protocol.deserializeResponse output

    [<Fact>]
    let ``the transcript worker rejects malformed input and unknown protocol versions`` () =
        let malformedExit, malformed = runWorker "not json"
        Assert.Equal(1, malformedExit)
        Assert.NotNull(malformed.Error)

        let versionExit, unknownVersion = runWorker """{"ProtocolVersion":999,"Examples":[]}"""
        Assert.Equal(1, versionExit)
        Assert.Contains("Unsupported transcript protocol version 999", unknownVersion.Error)

    [<Fact>]
    let ``examples run against the framework the audit compiles, not the newest build`` () =
        // Resolving by timestamp once picked net8.0 for one package and netstandard2.1 for its dependency, and examples
        // failed with "System.Runtime did not contain CancellationToken".
        let directory = Path.Combine(Path.GetTempPath(), "fslivedocs-tests", Guid.NewGuid().ToString "N")
        Directory.CreateDirectory directory |> ignore
        try
            let project = Path.Combine(directory, "Multi.fsproj")
            File.WriteAllText(
                project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks></PropertyGroup></Project>")

            let build framework =
                let output = Path.Combine(directory, "bin", "Debug", framework, "Multi.dll")
                Directory.CreateDirectory(Path.GetDirectoryName output) |> ignore
                File.WriteAllText(output, "")
                File.WriteAllText(Path.ChangeExtension(output, ".xml"), "<doc />")
                output

            let first = build "netstandard2.1"
            let newest = build "net8.0"
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddHours -1.0)
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow)

            Assert.Equal(first, ProjectResolver.resolveAssemblyPath project)
        finally
            Directory.Delete(directory, true)
