namespace FsLiveDocs.Cli

open System
open System.IO
open Axial
open Axial.FileSystem
open Reified
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects
open FsLiveDocs.Core.Schema
open FsLiveDocs.Runner

/// Extracts and caches the compiler-derived package model.
module internal PackageExtraction =

    let private packageCodec = Json.compile ApiSchema.packageModel
    let private diagnosticsCodec = Json.compile ApiSchema.apiDiagnostics

    /// <summary>Names of every XML example some documentation page transcludes.</summary>
    /// <remarks>
    /// A raw scan of the shortcodes, so it needs no package and can run before extraction. An
    /// example a page transcludes is compiled as part of that page and must not be compiled again
    /// on its own.
    /// </remarks>
    let private transcludedExamples projectPaths =
        let sets = Workspace.loadDocsSets projectPaths

        // Every directory-existence check, directory listing, and file read this scan needs is
        // gathered into one Flow and run exactly once; the filtering below is pure, consuming the
        // gathered listings the same way the original imperative scan did.
        let gatherWork =
            flow {
                let! root = FileSystem.getCurrentDirectory

                let! perSet =
                    sets
                    |> Flow.traverse (fun set ->
                        let docsDir = Path.GetFullPath(set.Source, root)

                        flow {
                            let! docsDirExists = FileSystem.directoryExists docsDir

                            let! files =
                                if docsDirExists then
                                    FileSystem.getFiles docsDir "*.md" SearchOption.AllDirectories
                                    |> Flow.map Array.toList
                                else
                                    Flow.succeed []

                            return set, files
                        })

                let ownedPaths =
                    perSet
                    |> List.collect (fun (set, files) ->
                        files
                        |> List.filter (fun path ->
                            let relative = Path.GetRelativePath(root, path).Replace('\\', '/')
                            DocsSet.ownerOf sets relative |> Option.exists (fun owner -> owner.Id = set.Id)))

                return! ownedPaths |> Flow.traverse FileSystem.readAllText
            }

        Run.orRaise FileSystemError.describe "Could not scan documentation sets for transcluded examples" gatherWork
        |> List.map ContentProvider.transcludedExampleNames
        |> Set.unionMany

    /// <summary>Loads and merges multiple project models into a unified package.</summary>
    let extractWithProgress reportProgress prelude (projectPaths: string list) =
        async {
            let packages = ResizeArray()
            let diagnostics = ResizeArray()
            let covered = transcludedExamples projectPaths
            // The same prelude a page block is compiled with. Without it an example referencing the
            // library by its own namespace fails for want of an open, not for anything wrong with it.
            let builtAssemblies =
                projectPaths
                |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                |> List.filter (String.IsNullOrWhiteSpace >> not)
                |> List.distinct

            for index, projectPath in projectPaths |> List.indexed do
                reportProgress "Extracting API documentation" (index + 1) projectPaths.Length
                let! package, projectDiagnostics = SymbolLister.extractFromProjectWithDiagnostics projectPath
                packages.Add(package)
                diagnostics.AddRange(projectDiagnostics)
                // Every example not covered elsewhere is compiled against the project that declares it,
                // so "the documented code compiles" holds for XML examples as it does for fences.
                let! exampleDiagnostics =
                    GeneratedVerification.compileUncoveredExamples projectPath prelude builtAssemblies covered package

                diagnostics.AddRange(exampleDiagnostics)

            return SymbolLister.merge (Seq.toList packages), List.ofSeq diagnostics
        }

    let extract prelude projectPaths =
        extractWithProgress (fun _ _ _ -> ()) prelude projectPaths

    let private sha256Text (value: string) =
        value
        |> Text.Encoding.UTF8.GetBytes
        |> Security.Cryptography.SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private documentationFingerprint projectPaths =
        let sets = Workspace.loadDocsSets projectPaths

        // As above: gather every existence check, listing, and read into one Flow, run it once,
        // then build the fingerprint text from the gathered snapshot.
        let gatherWork =
            flow {
                let! root = FileSystem.getCurrentDirectory

                let! perSet =
                    sets
                    |> Flow.traverse (fun set ->
                        let sourceDir = Path.GetFullPath(set.Source, root)

                        flow {
                            let! sourceDirExists = FileSystem.directoryExists sourceDir

                            let! files =
                                if sourceDirExists then
                                    FileSystem.getFiles sourceDir "*.md" SearchOption.AllDirectories
                                    |> Flow.map (Array.sort >> Array.toList)
                                else
                                    Flow.succeed []

                            let ownedPaths =
                                files
                                |> List.filter (fun path ->
                                    let relative = Path.GetRelativePath(root, path).Replace('\\', '/')
                                    DocsSet.ownerOf sets relative |> Option.exists (fun owner -> owner.Id = set.Id))

                            let! ownedFiles =
                                ownedPaths
                                |> Flow.traverse (fun path ->
                                    FileSystem.readAllText path
                                    |> Flow.map (fun text -> Path.GetRelativePath(root, path).Replace('\\', '/'), text))

                            return set, ownedFiles
                        })

                return perSet
            }

        let perSet = Run.orRaise FileSystemError.describe "Could not scan documentation sets" gatherWork

        [ for set, ownedFiles in perSet do
              let setPrelude = set.FSharpPrelude |> Option.defaultValue ""
              yield $"set:{set.Id}|source:{set.Source}|prelude:{setPrelude}"

              for relative, text in ownedFiles do
                  yield relative
                  yield text ]
        |> String.concat "\n--fslivedocs-documentation-input--\n"
        |> sha256Text

    /// <summary>Assembly and adjacent XML documentation paths used by the configured projects.</summary>
    let projectOutputPaths (projectPaths: string list) =
        projectPaths
        |> List.collect (fun projectPath ->
            let assemblyPath = (ProjectResolver.resolve projectPath).AssemblyPath
            if String.IsNullOrWhiteSpace assemblyPath then []
            else
                let assemblyPath = Path.GetFullPath assemblyPath
                [ assemblyPath; Path.ChangeExtension(assemblyPath, ".xml") ])
        |> List.distinct

    /// <summary>Content fingerprint for the resolved project assemblies and their XML documentation.</summary>
    let outputFilesFingerprint (outputPaths: string list) =
        outputPaths
        |> List.map Path.GetFullPath
        |> List.distinct
        |> List.sort
        |> List.map (fun path ->
            if File.Exists path then
                use stream = File.OpenRead path
                let hash = Security.Cryptography.SHA256.HashData(stream) |> Convert.ToHexString |> _.ToLowerInvariant()
                $"{path}\n{FileInfo(path).Length}\n{hash}"
            else
                $"{path}\nmissing")
        |> String.concat "\n--project-output--\n"
        |> sha256Text

    let inputFingerprint (projectPaths: string list) =
        let ignoredSegments = set [ ".git"; ".livedocs"; "artifacts"; "bin"; "obj"; "output" ]

        // Gather the directory listings, existence checks, and reads this fingerprint needs into
        // one Flow and run it once; the filtering/sorting/hashing below is pure.
        let gatherWork =
            flow {
                let! root = FileSystem.getCurrentDirectory

                let! projectFileLists =
                    projectPaths
                    |> Flow.traverse (fun projectPath ->
                        let fullPath = Path.GetFullPath(projectPath)
                        let directory = Path.GetDirectoryName(fullPath)

                        FileSystem.getFiles directory "*.fs" SearchOption.AllDirectories
                        |> Flow.map (fun files -> fullPath :: (files |> Array.toList)))

                let repositoryCandidates =
                    [ "Directory.Build.props"; "Directory.Build.targets"; "Directory.Packages.props"; "global.json"; "NuGet.config" ]
                    |> List.map (fun path -> Path.Combine(root, path))

                let! repositoryChecks =
                    repositoryCandidates
                    |> Flow.traverse (fun path -> FileSystem.fileExists path |> Flow.map (fun exists -> path, exists))

                let repositoryInputs =
                    repositoryChecks |> List.choose (fun (path, exists) -> if exists then Some path else None)

                let isIgnored (path: string) =
                    Path.GetRelativePath(root, path).Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
                    |> Array.exists ignoredSegments.Contains

                let candidatePaths =
                    (projectFileLists |> List.collect id) @ repositoryInputs
                    |> List.filter (isIgnored >> not)
                    |> List.distinct
                    |> List.sort

                return!
                    candidatePaths
                    |> Flow.traverse (fun path ->
                        FileSystem.readAllText path
                        |> Flow.map (fun text -> Path.GetRelativePath(root, path).Replace('\\', '/'), text))
            }

        let sourceFingerprint =
            Run.orRaise FileSystemError.describe "Could not compute input fingerprint" gatherWork
            |> List.collect (fun (relative, text) -> [ relative; text ])
            |> String.concat "\n--fslivedocs-project-input--\n"
            |> sha256Text

        let outputFingerprint = projectOutputPaths projectPaths |> outputFilesFingerprint
        sha256Text $"{sourceFingerprint}\n--fslivedocs-project-outputs--\n{outputFingerprint}"

    let private writeCurrentCache (path: string) (pattern: string) (value: string) =
        let directory = Path.GetDirectoryName(path)

        let work =
            flow {
                do! FileSystem.createDirectory directory
                do! FileSystem.writeAllText path value
                let! staleFiles = FileSystem.getFiles directory pattern SearchOption.TopDirectoryOnly

                for stale in staleFiles do
                    if not (Path.GetFullPath(stale).Equals(Path.GetFullPath(path), StringComparison.Ordinal)) then
                        do! FileSystem.deleteFile stale
            }

        Run.orRaise FileSystemError.describe $"Could not write cache {path}" work

    let extractCachedWithProgress reportProgress prelude (projectPaths: string list) =
        let cacheDirectory = Path.GetFullPath(Path.Combine(".livedocs", "cache"))
        let writeArtifact path value =
            let work =
                flow {
                    do! FileSystem.createDirectory cacheDirectory
                    do! FileSystem.writeAllText path value
                }
            Run.orRaise FileSystemError.describe $"Could not write cache {path}" work
        let extractorVersions =
            [ typeof<PackageModel>.Assembly
              typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly
              Reflection.Assembly.GetExecutingAssembly() ]
            |> List.map (fun assembly -> string assembly.ManifestModule.ModuleVersionId)
            |> String.concat ","
        let extractorContext = $"api-schema:{History.ApiModelSchemaVersion}|extractor:{extractorVersions}"

        let tryRead packagePath diagnosticsPath =
            let work =
                flow {
                    let! packageExists = FileSystem.fileExists packagePath
                    let! diagnosticsExist = FileSystem.fileExists diagnosticsPath
                    if packageExists && diagnosticsExist then
                        let! packageText = FileSystem.readAllText packagePath
                        let! diagnosticsText = FileSystem.readAllText diagnosticsPath
                        return Some(Json.deserialize packageCodec packageText, Json.deserialize diagnosticsCodec diagnosticsText)
                    else
                        return None
                }
            Run.orRaise FileSystemError.describe $"Could not read project extraction cache {packagePath}" work

        // Symbol extraction belongs to a project, not to the documentation tree. Persist each
        // project independently so a guide edit cannot reload assemblies or re-walk symbols.
        let projects =
            projectPaths
            |> List.indexed
            |> List.map (fun (index, projectPath) ->
                let inputHash = inputFingerprint [ projectPath ]
                let key = AnalysisCache.projectKey extractorContext inputHash
                let packagePath = Path.Combine(cacheDirectory, key + ".project-package.json")
                let diagnosticsPath = Path.Combine(cacheDirectory, key + ".project-diagnostics.json")
                let package, diagnostics =
                    match tryRead packagePath diagnosticsPath with
                    | Some cached -> cached
                    | None ->
                        let package, diagnostics =
                            SymbolLister.extractFromProjectWithDiagnostics projectPath |> Async.RunSynchronously
                        writeArtifact packagePath (Json.serialize packageCodec package)
                        writeArtifact diagnosticsPath (Json.serialize diagnosticsCodec diagnostics)
                        package, diagnostics
                reportProgress "Extracting API documentation" (index + 1) projectPaths.Length
                projectPath, key, package, diagnostics)

        let docsHash = documentationFingerprint projectPaths
        let projectKeys = projects |> List.map (fun (_, key, _, _) -> key)
        let verificationKey = AnalysisCache.verificationKey extractorContext docsHash prelude projectKeys
        let verificationPath = Path.Combine(cacheDirectory, verificationKey + ".verification-diagnostics.json")
        let verificationDiagnostics =
            let work =
                flow {
                    let! exists = FileSystem.fileExists verificationPath
                    if exists then
                        let! text = FileSystem.readAllText verificationPath
                        return Some(Json.deserialize diagnosticsCodec text)
                    else return None
                }
            match Run.orRaise FileSystemError.describe $"Could not read verification cache {verificationPath}" work with
            | Some diagnostics -> diagnostics
            | None ->
                let covered = transcludedExamples projectPaths
                let builtAssemblies =
                    projectPaths
                    |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                    |> List.filter (String.IsNullOrWhiteSpace >> not)
                    |> List.distinct
                let diagnostics =
                    projects
                    |> List.collect (fun (projectPath, _, package, _) ->
                        GeneratedVerification.compileUncoveredExamples projectPath prelude builtAssemblies covered package
                        |> Async.RunSynchronously)
                writeArtifact verificationPath (Json.serialize diagnosticsCodec diagnostics)
                diagnostics

        let package = projects |> List.map (fun (_, _, package, _) -> package) |> SymbolLister.merge
        let diagnostics =
            (projects |> List.collect (fun (_, _, _, diagnostics) -> diagnostics)) @ verificationDiagnostics
        let inputHash = projectKeys |> String.concat "|" |> sha256Text
        package, diagnostics, inputHash

    let extractCached prelude projectPaths =
        extractCachedWithProgress (fun _ _ _ -> ()) prelude projectPaths
