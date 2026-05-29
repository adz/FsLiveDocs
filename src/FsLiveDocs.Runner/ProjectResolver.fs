namespace FsLiveDocs.Runner

open System.IO
open FsLiveDocs.Core

/// <summary>Resolves source projects and built assemblies for doc-test execution.</summary>
module ProjectResolver =
    let resolveProjectPath (projectPath: string) =
        if Path.IsPathRooted(projectPath) && File.Exists(projectPath) then projectPath
        else
            let assemblyDir =
                typeof<PackageModel>.Assembly.Location
                |> Path.GetDirectoryName

            let projectName = Path.GetFileNameWithoutExtension(projectPath)
            let candidate =
                Path.Combine(
                    assemblyDir,
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    projectName,
                    Path.GetFileName(projectPath)
                )
                |> Path.GetFullPath

            if File.Exists(candidate) then candidate
            else Path.GetFullPath(projectPath)

    let resolveAssemblyPath (projectPath: string) =
        let projectName = Path.GetFileNameWithoutExtension(projectPath)
        let projDir = Path.GetDirectoryName(projectPath)

        let searchPaths = [
            Path.Combine(projDir, "../../artifacts/bin")
            Path.Combine(projDir, "bin/Debug/net10.0")
            Path.Combine(projDir, "bin/Release/net10.0")
        ]

        searchPaths
        |> List.filter Directory.Exists
        |> List.tryPick (fun path ->
            let files = Directory.GetFiles(path, $"{projectName}.dll", SearchOption.AllDirectories)
            if files.Length > 0 then Some files.[0] else None)
        |> Option.defaultValue ""
