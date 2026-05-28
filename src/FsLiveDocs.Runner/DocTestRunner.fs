namespace FsLiveDocs.Runner

open System
open System.IO
open System.Diagnostics
open FsLiveDocs.Core

/// <summary>The execution engine for verified docstrings (DocTests).</summary>
/// <example name="VerifyExamplesExample">
/// let package = { Version = "1.0"; Entities = []; Scenarios = [] }
/// let results = Async.RunSynchronously(DocTestRunner.verifyExamples package "FsLiveDocs.Runner.fsproj" [])
/// printfn "RESULTS: %d" results.Length
/// // EXPECTED: RESULTS: 0
/// </example>
module DocTestRunner =

    let private indentExample (content: string) (spaces: int) =
        let lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
        let nonEmpty = lines |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        if nonEmpty.Length = 0 then ""
        else
            let minIndent =
                nonEmpty
                |> Array.map (fun line -> line.Length - line.TrimStart().Length)
                |> Array.fold min Int32.MaxValue

            let indent = String(' ', spaces)
            lines
            |> Array.map (fun line ->
                let stripped = 
                    if line.Length >= minIndent then line.Substring(minIndent)
                    else line.TrimStart()
                
                // Final safety: if it looks like it should be top-level but has a lingering space, kill it
                let normalized = 
                    if stripped.StartsWith(" ") && (stripped.TrimStart().StartsWith("let ") || stripped.TrimStart().StartsWith("printfn ") || stripped.TrimStart().StartsWith("Say.")) then
                        stripped.TrimStart()
                    else stripped

                indent + normalized.TrimEnd())
            |> String.concat "\n"

    /// <summary>Generates a temporary .fsproj and Program.fs to execute code examples.</summary>
    /// <param name="examples">The examples to embed into the generated runner.</param>
    /// <param name="scenarios">The available setup scenarios keyed by example scenario name.</param>
    /// <param name="projectPath">The project under test.</param>
    /// <param name="references">Additional assembly references required by examples.</param>
    /// <param name="tempDir">The temporary directory used for generated files.</param>
    /// <returns>The generated test project file path.</returns>
    let generateTestProject (examples: ExampleModel list) (scenarios: ScenarioModel list) (projectPath: string) (references: string list) (tempDir: string) =
        let projectFile = Path.Combine(tempDir, "LiveDocs.Generated.Tests.fsproj")
        let programFile = Path.Combine(tempDir, "Program.fs")

        let refNodes = 
            references 
            |> List.map (fun r -> sprintf "<Reference Include=\"%s\"><HintPath>%s</HintPath></Reference>" (Path.GetFileNameWithoutExtension(r)) r) 
            |> String.concat "\n"
        
        let projectContent = 
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference>\n" +
            "    <OtherFlags>$(OtherFlags) --strict-indentation-</OtherFlags>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"FSharp.Core\" Version=\"10.1.201\" />\n" +
            "    <ProjectReference Include=\"" + Path.GetFullPath(projectPath) + "\" />\n" +
            "    " + refNodes + "\n" +
            "  </ItemGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"Program.fs\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>"
            
        File.WriteAllText(projectFile, projectContent)

        let scenarioNamespaces = 
            scenarios |> List.map (fun s -> 
                let lastDot = s.MethodId.LastIndexOf('.')
                if lastDot <> -1 then s.MethodId.Substring(0, lastDot) else ""
            )
            |> List.filter (fun s -> s <> "")

        // Also add the project name as a potential namespace
        let projNamespace = Path.GetFileNameWithoutExtension(projectPath)
        
        let allNamespaces = 
            "FsLiveDocs.Core" :: projNamespace :: scenarioNamespaces
            |> List.distinct
            |> List.map (fun ns -> "    open " + ns)
            |> String.concat "\n"

        // <snippet:ScenarioBinding>
        let testModules = 
            examples 
            |> List.mapi (fun i ex -> 
                let scenarioCall = 
                    match ex.Scenario with
                    | Some sName -> 
                        match scenarios |> List.tryFind (fun s -> s.Name = sName) with
                        | Some s -> sprintf "        do\n            %s()\n" s.MethodId
                        | None -> ""
                    | None -> ""
                
                let body = indentExample ex.Content 12
                
                sprintf "module Test%d =\n    open System\n%s\n    printfn \"--- TEST: %s ---\"\n    try\n%s%s\n            ()\n    with e -> printfn \"ERROR: %%s\" e.Message\n    printfn \"--- END TEST ---\"\n" i allNamespaces ex.Name scenarioCall body)
            |> String.concat "\n"
        // </snippet:ScenarioBinding>

        let programContent = sprintf "%s\n\n[<EntryPoint>]\nlet main _ = 0" testModules
        File.WriteAllText(programFile, programContent)
        projectFile

    /// <summary>Runs the generated test project and captures console output.</summary>
    /// <param name="projectFile">The generated test project file.</param>
    /// <returns>The combined stdout/stderr output produced by the execution.</returns>
    let runProject (projectFile: string) =
        let psi = ProcessStartInfo("dotnet", sprintf "run --project \"%s\"" projectFile)
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        let proc = Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        let error = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if not (String.IsNullOrWhiteSpace(error)) then output + "\nSTDERR:\n" + error else output

    /// <summary>Verifies all docstring examples extracted from a package.</summary>
    /// <param name="package">The package model containing examples and scenarios.</param>
    /// <param name="projectPath">The project that produced the package.</param>
    /// <param name="references">Additional references needed by generated examples.</param>
    /// <returns>A list of example names paired with pass/fail results and diagnostic output.</returns>
    let verifyExamples (package: PackageModel) (projectPath: string) (references: string list) = async {
        let tempDir = Path.Combine(Path.GetTempPath(), "FsLiveDocs", Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        
        let rec getAllExamples (e: EntityModel) =
            let entityExamples = if isNull (box e.Examples) then [] else e.Examples
            let members = entityExamples @ (e.Members |> List.collect (fun m -> m.Examples))
            let nested = e.Entities |> List.collect getAllExamples
            members @ nested

        let allExamples = package.Entities |> List.collect getAllExamples

        if allExamples.IsEmpty then 
            return []
        else
            let projectFile = generateTestProject allExamples package.Scenarios projectPath references tempDir
            let output = runProject projectFile
            
            let mutable results = []
            for ex in allExamples do
                let startMarker = sprintf "--- TEST: %s ---" ex.Name
                let endMarker = "--- END TEST ---"
                let startIndex = output.IndexOf(startMarker)
                let ran = startIndex <> -1
                if not ran then
                    let programContent = if Directory.Exists(tempDir) && File.Exists(Path.Combine(tempDir, "Program.fs")) then File.ReadAllText(Path.Combine(tempDir, "Program.fs")) else "Program.fs not found"
                    results <- (ex.Name, false, "Test did not run. Output:\n" + output + "\n\nGenerated Program.fs:\n" + programContent) :: results
                elif startIndex <> -1 then
                    let contentStart = startIndex + startMarker.Length
                    let endIndex = output.IndexOf(endMarker, contentStart)
                    if endIndex <> -1 then
                        let actual = output.Substring(contentStart, endIndex - contentStart).Trim()
                        match ex.ExpectedOutput with
                        | Some expected -> 
                            if actual.Contains(expected) then results <- (ex.Name, true, actual) :: results
                            else results <- (ex.Name, false, sprintf "Expected: %s, Actual: %s" expected actual) :: results
                        | None -> results <- (ex.Name, true, actual) :: results
                    else
                        results <- (ex.Name, false, "Test crashed or timed out") :: results
            
            return List.rev results
    }
