namespace FsLiveDocs.Runner

open System
open System.IO
open System.Diagnostics
open FsLiveDocs.Core

/// <summary>The execution engine for verified docstrings (DocTests).</summary>
module DocTestRunner =

    /// <summary>Generates a temporary .fsproj and Program.fs to execute code examples.</summary>
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

        let testFuncs = 
            examples 
            |> List.mapi (fun i ex -> 
                let scenarioCall = 
                    match ex.Scenario with
                    | Some sName -> 
                        match scenarios |> List.tryFind (fun s -> s.Name = sName) with
                        | Some s -> sprintf "    %s()\n" s.MethodId
                        | None -> ""
                    | None -> ""
                
                let body = 
                    ex.Content.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries) 
                    |> Array.map (fun l -> "        " + l.Trim()) 
                    |> String.concat "\n"
                
                sprintf "let test%d () =\n%s\n    let _ =\n%s\n        ()\n    ()\n" i scenarioCall body)
            |> String.concat "\n"

        let testCalls =
            examples
            |> List.mapi (fun i ex ->
                sprintf "    printfn \"--- TEST: %s ---\"\n    try test%d() with e -> printfn \"ERROR: %%s\" e.Message\n    printfn \"--- END TEST ---\"\n" ex.Name i)
            |> String.concat "\n"

        let scenarioNamespaces = 
            scenarios |> List.map (fun s -> 
                let lastDot = s.MethodId.LastIndexOf('.')
                if lastDot <> -1 then s.MethodId.Substring(0, lastDot) else ""
            )
            |> List.filter (fun s -> s <> "")

        // Also add the project name as a potential namespace
        let projNamespace = Path.GetFileNameWithoutExtension(projectPath)
        
        let allNamespaces = 
            projNamespace :: scenarioNamespaces
            |> List.distinct
            |> List.map (fun ns -> "open " + ns)
            |> String.concat "\n"

        let programContent = sprintf "module GeneratedTests\nopen System\n%s\n\n%s\n\n[<EntryPoint>]\nlet main _ =\n%s\n    0" allNamespaces testFuncs testCalls
        File.WriteAllText(programFile, programContent)
        projectFile

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

    let verifyExamples (package: PackageModel) (projectPath: string) (references: string list) = async {
        let tempDir = Path.Combine(Path.GetTempPath(), "FsLiveDocs", Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        
        let rec getAllExamples (e: EntityModel) =
            let members = e.Members |> List.collect (fun m -> m.Examples)
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
                if startIndex <> -1 then
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
                else
                    results <- (ex.Name, false, "Test did not run. Output:\n" + output) :: results
            return List.rev results
    }
