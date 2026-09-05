namespace FsLiveDocs.Cli

open System
open System.IO
open FsLiveDocs.Core
open FsLiveDocs.Runner

/// Extracts and caches the compiler-derived package model.
module internal PackageExtraction =

    /// <summary>Names of every XML example some documentation page transcludes.</summary>
    /// <remarks>
    /// A raw scan of the shortcodes, so it needs no package and can run before extraction. An
    /// example a page transcludes is compiled as part of that page and must not be compiled again
    /// on its own.
    /// </remarks>
    let private transcludedExamples projectPaths =
        let root = Directory.GetCurrentDirectory()
        let sets = Workspace.loadDocsSets projectPaths

        sets
        |> List.collect (fun set ->
            let docsDir = Path.GetFullPath(set.Source, root)

            if not (Directory.Exists docsDir) then
                []
            else
                Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                |> Array.filter (fun path ->
                    let relative = Path.GetRelativePath(root, path).Replace('\\', '/')
                    DocsSet.ownerOf sets relative |> Option.exists (fun owner -> owner.Id = set.Id))
                |> Array.toList)
        |> List.map (File.ReadAllText >> ContentProvider.transcludedExampleNames)
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
        let root = Directory.GetCurrentDirectory()
        let sets = Workspace.loadDocsSets projectPaths

        [ for set in sets do
              let setPrelude = set.FSharpPrelude |> Option.defaultValue ""
              yield $"set:{set.Id}|source:{set.Source}|prelude:{setPrelude}"
              let sourceDir = Path.GetFullPath(set.Source, root)

              if Directory.Exists sourceDir then
                  for path in Directory.GetFiles(sourceDir, "*.md", SearchOption.AllDirectories) |> Array.sort do
                      let relative = Path.GetRelativePath(root, path).Replace('\\', '/')

                      if DocsSet.ownerOf sets relative |> Option.exists (fun owner -> owner.Id = set.Id) then
                          yield relative
                          yield File.ReadAllText path ]
        |> String.concat "\n--fslivedocs-documentation-input--\n"
        |> sha256Text

    let inputFingerprint (projectPaths: string list) =
        let root = Directory.GetCurrentDirectory()
        let ignoredSegments = set [ ".git"; ".livedocs"; "artifacts"; "bin"; "obj"; "output" ]
        let isIgnored (path: string) =
            Path.GetRelativePath(root, path).Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
            |> Array.exists ignoredSegments.Contains
        let projectFiles =
            projectPaths
            |> List.collect (fun projectPath ->
                let fullPath = Path.GetFullPath(projectPath)
                let directory = Path.GetDirectoryName(fullPath)
                fullPath :: (Directory.GetFiles(directory, "*.fs", SearchOption.AllDirectories) |> Array.toList))
        let repositoryInputs =
            [ "Directory.Build.props"; "Directory.Build.targets"; "Directory.Packages.props"; "global.json"; "NuGet.config" ]
            |> List.map (fun path -> Path.Combine(root, path))
            |> List.filter File.Exists
        projectFiles @ repositoryInputs
        |> List.filter (isIgnored >> not)
        |> List.distinct
        |> List.sort
        |> List.collect (fun path -> [ Path.GetRelativePath(root, path).Replace('\\', '/'); File.ReadAllText(path) ])
        |> String.concat "\n--fslivedocs-project-input--\n"
        |> sha256Text

    let private writeCurrentCache (path: string) (pattern: string) (value: string) =
        let directory = Path.GetDirectoryName(path)
        Directory.CreateDirectory(directory) |> ignore
        File.WriteAllText(path, value)
        for stale in Directory.GetFiles(directory, pattern) do
            if not (Path.GetFullPath(stale).Equals(Path.GetFullPath(path), StringComparison.Ordinal)) then File.Delete(stale)

    let extractCachedWithProgress reportProgress prelude (projectPaths: string list) =
        let inputHash = inputFingerprint projectPaths
        let cacheDirectory = Path.GetFullPath(Path.Combine(".livedocs", "cache"))
        // The key covers every assembly whose code shapes what is cached. Keying on Core alone
        // silently replayed stale diagnostics whenever the Runner or the CLI changed, which is
        // indistinguishable from a fix having no effect.
        let extractorVersions =
            [ typeof<PackageModel>.Assembly
              typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly
              Reflection.Assembly.GetExecutingAssembly() ]
            |> List.map (fun assembly -> string assembly.ManifestModule.ModuleVersionId)
            |> String.concat ","

        let docsHash = documentationFingerprint projectPaths

        let cacheKey =
            sha256Text
                $"api-schema:{History.ApiModelSchemaVersion}|extractor:{extractorVersions}|projects:{inputHash}|documentation:{docsHash}|prelude:{prelude}"

        let cachePath = Path.Combine(cacheDirectory, cacheKey + ".package.json")
        // Diagnostics describe the run, not the snapshot, so they live beside the cached package
        // rather than inside it — otherwise a warning would be reported once and never again.
        let diagnosticsPath = Path.Combine(cacheDirectory, cacheKey + ".diagnostics.json")
        if File.Exists cachePath then
            reportProgress "Extracting API documentation" projectPaths.Length projectPaths.Length
            let package = Newtonsoft.Json.JsonConvert.DeserializeObject<PackageModel>(File.ReadAllText(cachePath), FsLiveDocs.Core.Serialization.jsonSettings)
            if isNull (box package) then invalidOp $"Invalid cached package model: {cachePath}"
            let diagnostics =
                if File.Exists diagnosticsPath then
                    Newtonsoft.Json.JsonConvert.DeserializeObject<ApiDiagnostic list>(File.ReadAllText(diagnosticsPath), FsLiveDocs.Core.Serialization.jsonSettings)
                    |> Option.ofObj
                    |> Option.defaultValue []
                else []
            package, diagnostics, inputHash
        else
            let package, diagnostics = extractWithProgress reportProgress prelude projectPaths |> Async.RunSynchronously
            writeCurrentCache cachePath "*.package.json" (Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
            writeCurrentCache diagnosticsPath "*.diagnostics.json" (Newtonsoft.Json.JsonConvert.SerializeObject(diagnostics, Newtonsoft.Json.Formatting.Indented, FsLiveDocs.Core.Serialization.jsonSettings))
            package, diagnostics, inputHash

    let extractCached prelude projectPaths =
        extractCachedWithProgress (fun _ _ _ -> ()) prelude projectPaths
