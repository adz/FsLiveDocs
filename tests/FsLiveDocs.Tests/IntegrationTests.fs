namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core
open FsLiveDocs.Runner

module IntegrationTests =

    let private coreProject = ProjectResolver.resolveProjectPath "FsLiveDocs.Core.fsproj"

    [<Fact>]
    let ``documentation compiler uses project references and page scope without executing`` () = async {
        let blocks =
            DocumentationDiscovery.discoverMarkdown
                "guide.md"
                (Some coreProject)
                "```fsharp\nopen FsLiveDocs.Core\nlet package : PackageModel = { Version = \"1\"; Entities = []; Scenarios = []; Packages = [] }\n```\n```fsharp\nlet version : string = package.Version\n```"
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
