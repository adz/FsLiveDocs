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

    /// <summary>The build that documentation is compiled and run against.</summary>
    /// <remarks><c>Arguments</c> select it in further <c>dotnet msbuild</c> calls.</remarks>
    type DocumentationBuild = { Framework: string option; Arguments: string list; TargetPath: string option }

    /// <summary>
    /// Selects the build documentation uses: <paramref name="targetFramework" />, or the sole or first framework the
    /// project declares, in the default configuration when that has been built and in Release otherwise.
    /// </summary>
    let documentationBuildFor (targetFramework: string option) (projectPath: string) : DocumentationBuild =
        let fullPath = Path.GetFullPath(projectPath)
        if not (File.Exists fullPath) then invalidOp $"Documentation project does not exist: {fullPath}"

        let dimensions = MsBuild.evaluate fullPath [ "-getProperty:TargetFramework,TargetFrameworks" ]
        let declaredFrameworks =
            match MsBuild.property dimensions "TargetFramework", MsBuild.property dimensions "TargetFrameworks" with
            | Some framework, _ -> [ framework ]
            | None, Some frameworks ->
                frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries) |> Array.toList
            | None, None -> []

        match targetFramework with
        | Some requested when not (declaredFrameworks |> List.contains requested) ->
            let declared = String.concat ", " declaredFrameworks
            invalidOp $"Target framework '{requested}' is not declared by {fullPath}. Declared frameworks: {declared}."
        | _ -> ()

        let framework = targetFramework |> Option.orElseWith (fun () -> List.tryHead declaredFrameworks)
        let frameworkArgument = framework |> Option.map (fun value -> $"-property:TargetFramework={value}") |> Option.toList

        let builtTarget configurationArgument =
            let json = MsBuild.evaluate fullPath (configurationArgument @ frameworkArgument @ [ "-getProperty:Configuration,TargetPath" ])
            MsBuild.property json "TargetPath" |> Option.filter File.Exists

        match builtTarget [] with
        | Some target -> { Framework = framework; Arguments = frameworkArgument; TargetPath = Some target }
        | None ->
            let release = [ "-property:Configuration=Release" ]
            match builtTarget release with
            | Some target -> { Framework = framework; Arguments = release @ frameworkArgument; TargetPath = Some target }
            | None -> { Framework = framework; Arguments = frameworkArgument; TargetPath = None }

    /// The most recently written documented build of the project under `bin` or an ancestor's `artifacts/bin`, for
    /// when MSBuild cannot evaluate it.
    let private newestBuiltAssembly (projectPath: string) =
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

    let private builtAssemblies = Collections.Concurrent.ConcurrentDictionary<string, Lazy<string>>()

    /// <summary>The assembly examples run against: the same build <c>DocumentationCompiler</c> type-checks them with.</summary>
    /// <remarks>
    /// Picking the newest file instead mixed target frameworks: after a multi-targeting build, one package could resolve
    /// to `net8.0` and its dependency to `netstandard2.1`, and examples failed to compile. Cached per project for the
    /// process, since every example asks.
    /// </remarks>
    let resolveAssemblyPath (projectPath: string) =
        builtAssemblies
            .GetOrAdd(
                Path.GetFullPath projectPath,
                fun path ->
                    lazy
                        (let evaluated =
                            try
                                (documentationBuildFor None path).TargetPath
                            with :? InvalidOperationException ->
                                None

                         evaluated |> Option.defaultWith (fun () -> newestBuiltAssembly path))
            )
            .Value

    let resolve (projectPath: string) =
        let resolvedProjectPath = resolveProjectPath projectPath
        let assemblyPath = resolveAssemblyPath resolvedProjectPath
        let projectNamespace = Path.GetFileNameWithoutExtension(resolvedProjectPath)

        {
            ProjectPath = resolvedProjectPath
            AssemblyPath = assemblyPath
            ProjectNamespace = projectNamespace
        }
