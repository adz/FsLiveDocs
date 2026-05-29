namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Core

module IntegrationTests =

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
