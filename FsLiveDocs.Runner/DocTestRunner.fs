namespace FsLiveDocs.Runner

open System
open System.IO
open System.Diagnostics
open FsLiveDocs.Core

module DocTestRunner =

    let generateTestProject (examples: ExampleModel list) (references: string list) (tempDir: string) =
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
            "    <TargetFramework>net9.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    " + refNodes + "\n" +
            "  </ItemGroup>\n" +
            "</Project>"
            
        File.WriteAllText(projectFile, projectContent)

        let testCases = 
            examples 
            |> List.mapi (fun i ex -> 
                sprintf "\nprintfn \"--- TEST: %s ---\"\ntry\n%s\nprintfn \"--- END TEST ---\"\nwith e ->\nprintfn \"ERROR: %%s\" e.Message\nprintfn \"--- END TEST ---\"\n" ex.Name ex.Content)
            |> String.concat "\n"

        let programContent = "open System\n" + testCases
        File.WriteAllText(programFile, programContent)
        projectFile

    let runProject (projectFile: string) =
        let psi = ProcessStartInfo("dotnet", sprintf "run --project \"%s\"" projectFile)
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        let proc = Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit()
        output

    let verifyExamples (package: PackageModel) (references: string list) = async {
        let tempDir = Path.Combine(Path.GetTempPath(), "FsLiveDocs", Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        
        let allExamples = 
            package.Entities 
            |> List.collect (fun e -> 
                let members = e.Members |> List.collect (fun m -> m.Examples)
                let nested = e.Entities |> List.collect (fun ne -> ne.Members |> List.collect (fun m -> m.Examples))
                members @ nested
            )

        if allExamples.IsEmpty then 
            return []
        else
            let projectFile = generateTestProject allExamples references tempDir
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
                            if actual = expected then results <- (ex.Name, true, actual) :: results
                            else results <- (ex.Name, false, sprintf "Expected: %s, Actual: %s" expected actual) :: results
                        | None -> results <- (ex.Name, true, actual) :: results
                else
                    results <- (ex.Name, false, "Test did not run") :: results
            return List.rev results
    }
