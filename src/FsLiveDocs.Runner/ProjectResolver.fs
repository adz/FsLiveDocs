namespace FsLiveDocs.Runner

open System.IO
open System.Xml.Linq
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
        let assemblyName =
            let document = XDocument.Load(projectPath)
            document.Descendants(XName.Get "AssemblyName")
            |> Seq.tryHead
            |> Option.map _.Value
            |> Option.filter (System.String.IsNullOrWhiteSpace >> not)
            |> Option.defaultValue projectName

        let searchPaths = [
            Path.GetFullPath(Path.Combine(projDir, "../../artifacts/bin", projectName))
            Path.Combine(projDir, "bin")
        ]

        searchPaths
        |> List.filter Directory.Exists
        |> List.tryPick (fun path ->
            Directory.GetFiles(path, $"{assemblyName}.dll", SearchOption.AllDirectories)
            |> Array.filter (fun assembly -> File.Exists(Path.ChangeExtension(assembly, ".xml")))
            |> Array.sortByDescending File.GetLastWriteTimeUtc
            |> Array.tryHead)
        |> Option.defaultValue ""

    let resolve (projectPath: string) =
        let resolvedProjectPath = resolveProjectPath projectPath
        let assemblyPath = resolveAssemblyPath resolvedProjectPath
        let projectNamespace = Path.GetFileNameWithoutExtension(resolvedProjectPath)

        {
            ProjectPath = resolvedProjectPath
            AssemblyPath = assemblyPath
            ProjectNamespace = projectNamespace
        }
