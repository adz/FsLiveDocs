namespace FsLiveDocs.Runner

open System
open System.IO
open System.Xml.Linq
open Axial
open Axial.FileSystem
open FsLiveDocs.Core

/// <summary>Resolves source projects and built assemblies for doc-test execution.</summary>
module ProjectResolver =

    type private RunnerEnvironment =
        { FileSystem: IFileSystem }
        interface IHasFileSystem with
            member this.FileSystem = this.FileSystem

    let private environment : RunnerEnvironment = { FileSystem = FileSystem.live }

    /// Runs a file-system Flow synchronously, falling back on any typed error -- this module
    /// never throws for a missing file or directory, matching its pre-Axial.FileSystem contract.
    let private run (flow: Flow<RunnerEnvironment, FileSystemError, 'value>) (fallback: 'value) =
        match flow |> Flow.run environment with
        | Exit.Success value -> value
        | Exit.Failure _ -> fallback

    let resolveProjectPath (projectPath: string) =
        if Path.IsPathRooted(projectPath) && run (FileSystem.fileExists projectPath) false then
            projectPath
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

            if run (FileSystem.fileExists candidate) false then candidate
            else Path.GetFullPath(projectPath)

    let resolveAssemblyPath (projectPath: string) =
        let projectName = Path.GetFileNameWithoutExtension(projectPath)
        let projDir = Path.GetDirectoryName(projectPath)
        let assemblyName =
            let document = XDocument.Load(projectPath)
            document.Descendants(XName.Get "AssemblyName")
            |> Seq.tryHead
            |> Option.map _.Value
            |> Option.filter (String.IsNullOrWhiteSpace >> not)
            |> Option.defaultValue projectName

        let rec ancestors directory =
            [ if not (String.IsNullOrWhiteSpace directory) then
                  yield directory
                  match run (FileSystem.getParent directory) None with
                  | Some parent -> yield! ancestors parent
                  | None -> () ]
        let searchPaths =
            [ yield Path.Combine(projDir, "bin")
              for ancestor in ancestors projDir do
                  yield Path.Combine(ancestor, "artifacts", "bin", projectName) ]
            |> List.distinct

        searchPaths
        |> List.filter (fun path -> run (FileSystem.directoryExists path) false)
        |> List.collect (fun path ->
            run (FileSystem.getFiles path $"{assemblyName}.dll" SearchOption.AllDirectories) [||]
            |> Array.filter (fun assembly -> run (FileSystem.fileExists (Path.ChangeExtension(assembly, ".xml"))) false)
            |> Array.toList)
        |> List.distinct
        |> List.sortByDescending (fun path -> run (FileSystem.getFileLastWriteTimeUtc path) DateTime.MinValue)
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
