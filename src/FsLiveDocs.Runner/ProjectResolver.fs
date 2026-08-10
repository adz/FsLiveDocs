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

        let rec ancestors directory =
            seq {
                if not (System.String.IsNullOrWhiteSpace directory) then
                    yield directory
                    let parent = Directory.GetParent(directory)
                    if not (isNull parent) then yield! ancestors parent.FullName
            }
        let searchPaths =
            [ yield Path.Combine(projDir, "bin")
              for ancestor in ancestors projDir do
                  yield Path.Combine(ancestor, "artifacts", "bin", projectName) ]
            |> List.distinct

        searchPaths
        |> List.filter Directory.Exists
        |> List.collect (fun path ->
            Directory.GetFiles(path, $"{assemblyName}.dll", SearchOption.AllDirectories)
            |> Array.filter (fun assembly -> File.Exists(Path.ChangeExtension(assembly, ".xml")))
            |> Array.toList)
        |> List.distinct
        |> List.sortByDescending File.GetLastWriteTimeUtc
        |> List.tryHead
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
