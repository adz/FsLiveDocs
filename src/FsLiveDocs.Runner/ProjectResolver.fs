namespace FsLiveDocs.Runner

open System
open System.IO
open System.Xml.Linq
open Axial
open Axial.FileSystem
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects

/// <summary>Resolves source projects and built assemblies for doc-test execution. Never throws
/// for a missing file or directory -- matches its pre-Axial.FileSystem contract -- so every
/// resolution falls back through Run.orFallback rather than raising.</summary>
module ProjectResolver =

    let resolveProjectPath (projectPath: string) =
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

        let resolution =
            flow {
                let! rooted =
                    if Path.IsPathRooted(projectPath) then FileSystem.fileExists projectPath
                    else Flow.succeed false
                if rooted then
                    return projectPath
                else
                    let! candidateExists = FileSystem.fileExists candidate
                    return if candidateExists then candidate else Path.GetFullPath(projectPath)
            }
        Run.orFallback resolution (Path.GetFullPath(projectPath))

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

        let rec ancestors directory : Flow<LiveEnvironment, FileSystemError, string list> =
            flow {
                if String.IsNullOrWhiteSpace directory then
                    return []
                else
                    let! parent = FileSystem.getParent directory
                    match parent with
                    | Some parentDirectory ->
                        let! rest = ancestors parentDirectory
                        return directory :: rest
                    | None -> return [ directory ]
            }

        let resolution =
            flow {
                let! ancestorDirectories = ancestors projDir
                let searchPaths =
                    [ yield Path.Combine(projDir, "bin")
                      for ancestor in ancestorDirectories do
                          yield Path.Combine(ancestor, "artifacts", "bin", projectName) ]
                    |> List.distinct

                let! existingSearchPaths =
                    searchPaths
                    |> Flow.traverse (fun path ->
                        FileSystem.directoryExists path
                        |> Flow.map (fun exists -> if exists then Some path else None))
                let existingSearchPaths = existingSearchPaths |> List.choose id

                let! candidateLists =
                    existingSearchPaths
                    |> Flow.traverse (fun path -> FileSystem.getFiles path $"{assemblyName}.dll" SearchOption.AllDirectories)
                let candidates = candidateLists |> List.collect Array.toList |> List.distinct

                let! documentedCandidates =
                    candidates
                    |> Flow.traverse (fun assembly ->
                        FileSystem.fileExists (Path.ChangeExtension(assembly, ".xml"))
                        |> Flow.map (fun hasDocs -> if hasDocs then Some assembly else None))
                let documentedCandidates = documentedCandidates |> List.choose id

                let! withTimestamps =
                    documentedCandidates
                    |> Flow.traverse (fun assembly ->
                        FileSystem.getFileLastWriteTimeUtc assembly
                        |> Flow.map (fun writtenAt -> assembly, writtenAt))

                return
                    withTimestamps
                    |> List.sortByDescending snd
                    |> List.tryHead
                    |> Option.map fst
                    |> Option.defaultValue ""
            }
        Run.orFallback resolution ""

    let resolve (projectPath: string) =
        let resolvedProjectPath = resolveProjectPath projectPath
        let assemblyPath = resolveAssemblyPath resolvedProjectPath
        let projectNamespace = Path.GetFileNameWithoutExtension(resolvedProjectPath)

        {
            ProjectPath = resolvedProjectPath
            AssemblyPath = assemblyPath
            ProjectNamespace = projectNamespace
        }
