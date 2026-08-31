namespace FsLiveDocs.Cli

open System
open System.IO
open FsLiveDocs.Core
open FsLiveDocs.Runner

/// Discovers and compiler-checks canonical documentation pages.
module internal DocAnalysis =

    let private sha256Text (value: string) =
        value
        |> Text.Encoding.UTF8.GetBytes
        |> Security.Cryptography.SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    /// <summary>One documentation page, resolved against the projects passed to livedocs.</summary>
    type Page =
        {
            /// <summary>Documentation-set identity carried through discovery and release capture.</summary>
            SetId: string
            /// <summary>Path of the page relative to the documentation directory.</summary>
            Relative: string
            /// <summary>Path of the canonical page relative to its owning set's source root.</summary>
            SourcePath: string
            /// <summary>The resolved set-specific verification setup.</summary>
            Prelude: string
            /// <summary>The project this page's F# blocks are compiled against.</summary>
            SelectedProject: string
            /// <summary>Page body after snippet and example transclusion.</summary>
            Expanded: string
            /// <summary>F# blocks discovered in the expanded body.</summary>
            Blocks: DocumentationBlock list
            /// <summary>Renderer-neutral page metadata retained in release content.</summary>
            Metadata: ContentMetadata
            /// <summary>Target framework this page pins, if any.</summary>
            TargetFramework: string option
        }

    type Analysis =
        { Blocks: DocumentationBlock list
          Results: CheckedCompilationUnit list
          Prelude: string
          CachedArtifact: SemanticDocumentationArtifact option
          CachePath: string }

    /// <summary>
    /// Walks the documentation directory once, resolving every page against the projects passed to
    /// livedocs.
    /// </summary>
    /// <remarks>
    /// Audit, build and generated tests all need the same page set resolved the same way. When
    /// they each walked the directory themselves the copies drifted, and the copy behind
    /// generated tests silently omitted the check that a selected project was actually passed.
    /// </remarks>
    let private pagesForResolvedSets
        useSetIdentity
        (sets: DocsSet list)
        (projectPaths: string list)
        (package: PackageModel)
        =
        if List.isEmpty projectPaths then
            invalidOp "Documentation analysis requires at least one project path."

        let sourceDir = Directory.GetCurrentDirectory()
        let resolvedProjects = projectPaths |> List.map Path.GetFullPath

        let describe (path: string) =
            Path.GetRelativePath(sourceDir, path).Replace('\\', '/')

        let resolveSetProject (set: DocsSet) project =
            let full = Path.GetFullPath(project, sourceDir)

            if not (File.Exists full) then
                invalidOp $"Documentation set {set.Id} project does not exist: {project}"

            if not (resolvedProjects |> List.contains full) then
                invalidOp
                    $"Documentation set {set.Id} selects {describe full}, but that project was not passed to livedocs. Build, test, watch, and capture operate on the union of every set's projects."

            full

        [ for set in sets do
              let docsDir = Path.GetFullPath(set.Source, sourceDir)

              if not (Directory.Exists docsDir) then
                  invalidOp $"Documentation directory for set {set.Id} is missing: {docsDir}"

              let setProjects = set.Projects |> List.map (resolveSetProject set)

              let defaultProject =
                  setProjects |> List.tryHead |> Option.defaultValue (List.head resolvedProjects)

              for path in Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories) |> Array.sort do
                  let repositoryRelative = Path.GetRelativePath(sourceDir, path).Replace('\\', '/')

                  match DocsSet.ownerOf sets repositoryRelative with
                  | Some owner when owner.Id = set.Id ->
                      let sourcePath = Path.GetRelativePath(docsDir, path).Replace('\\', '/')

                      let relative =
                          if useSetIdentity then
                              set.Id + "/" + sourcePath
                          else
                              sourcePath

                      let raw = File.ReadAllText(path)
                      let frontMatter = ContentProvider.parseFrontMatter raw
                      let body = frontMatter |> Option.map snd |> Option.defaultValue raw

                      let metadata =
                          frontMatter
                          |> Option.map fst
                          |> Option.defaultValue
                              { Title = ContentProvider.defaultTitle path
                                Type = None
                                Project = None
                                TargetFramework = None
                                Platform = None }

                      let selectedProject =
                          match frontMatter |> Option.bind (fun (metadata, _) -> metadata.Project) with
                          | None -> defaultProject
                          | Some configured ->
                              [ Path.GetFullPath(configured, sourceDir)
                                Path.GetFullPath(configured, docsDir) ]
                              |> List.tryFind File.Exists
                              |> Option.defaultWith (fun () ->
                                  invalidOp $"Documentation project in {relative} does not exist: {configured}")

                      if not (resolvedProjects |> List.contains selectedProject) then
                          let passed =
                              resolvedProjects
                              |> List.map (fun project -> "  " + describe project)
                              |> String.concat "\n"

                          invalidOp
                              $"Documentation page {relative} selects {describe selectedProject}, but that project was not passed to livedocs.\n\
                              Projects passed:\n{passed}\n\
                              Add the selected project to the command, or change the 'project:' front matter on that page."

                      if not setProjects.IsEmpty && not (setProjects |> List.contains selectedProject) then
                          invalidOp
                              $"Documentation page {relative} selects {describe selectedProject}, which is not listed in documentation set {set.Id}'s projects."

                      let expanded = ContentProvider.expandTransclusions body sourceDir package

                      let blocks =
                          DocumentationDiscovery.discoverMarkdown relative (Some selectedProject) expanded

                      DocumentationDiscovery.validateCoverage blocks

                      let platform =
                          frontMatter
                          |> Option.bind (fun (metadata, _) -> metadata.Platform)
                          |> Option.map _.ToLowerInvariant()

                      match platform with
                      | Some "fable" when
                          blocks
                          |> List.exists (fun block ->
                              match block.Mode with
                              | NoCheck _
                              | Transcript -> false
                              | _ -> true)
                          ->
                          invalidOp
                              $"Documentation page {relative} declares platform: fable, but FsLiveDocs cannot yet invoke the Fable compiler. Mark each F# block no-check with a reason or transclude code covered by a Fable build gate."
                      | Some value when value <> "dotnet" && value <> "fable" ->
                          invalidOp $"Documentation page {relative} declares unsupported platform '{value}'."
                      | _ -> ()

                      let targetFramework =
                          frontMatter |> Option.bind (fun (metadata, _) -> metadata.TargetFramework)

                      yield
                          { SetId = set.Id
                            Relative = relative
                            SourcePath = sourcePath
                            Prelude = set.FSharpPrelude |> Option.defaultValue ""
                            SelectedProject = selectedProject
                            Expanded = expanded
                            Blocks = blocks
                            Metadata = metadata
                            TargetFramework = targetFramework }
                  | _ -> () ]

    /// Legacy single-tree discovery. Its paths and cache identities remain unchanged when
    /// <c>docsSets</c> is absent.
    let pages (projectPaths: string list) (package: PackageModel) =
        pagesForResolvedSets false [ DocsSet.implicit None projectPaths None ] projectPaths package

    /// Discovers every configured set once, assigning overlapping source trees to their most
    /// specific owner and prefixing semantic identities with the set route.
    let pagesForDocsSets sets projectPaths package =
        pagesForResolvedSets true sets projectPaths package

    let private analyzePagesWithProgress
        reportProgress
        defaultPrelude
        (projectPaths: string list)
        (projectFingerprint: string)
        (package: PackageModel)
        (pages: Page list)
        =
        let resolvedProjects = projectPaths |> List.map Path.GetFullPath
        let blocks = pages |> List.collect _.Blocks
        let packageFingerprint = Newtonsoft.Json.JsonConvert.SerializeObject(package, FsLiveDocs.Core.Serialization.jsonSettings)
        let contextFingerprint =
            [ yield $"semantic-schema:{History.SemanticSchemaVersion}"
              yield $"compiler-mvid:{typeof<EvaluatedProject>.Assembly.ManifestModule.ModuleVersionId}"
              yield $"project-inputs:{projectFingerprint}"
              yield $"prelude:{defaultPrelude}"
              yield packageFingerprint
              for page in pages do
                  let framework = page.TargetFramework |> Option.defaultValue "<default>"
                  yield $"set:{page.SetId}|project:{page.SelectedProject}|framework:{framework}|prelude:{page.Prelude}"

                  for block in page.Blocks do
                      yield $"block:{block.Id}|{block.SourceHash}" ]
            |> String.concat "\n"
        let cacheDirectory = Path.Combine(".livedocs", "cache")
        let cachePath = Path.Combine(cacheDirectory, sha256Text contextFingerprint + ".semantic.json") |> Path.GetFullPath
        let cachedArtifact =
            if File.Exists cachePath then
                let artifact = Newtonsoft.Json.JsonConvert.DeserializeObject<SemanticDocumentationArtifact>(File.ReadAllText(cachePath), FsLiveDocs.Core.Serialization.jsonSettings)
                if isNull (box artifact) || artifact.SchemaVersion <> History.SemanticSchemaVersion then None else Some artifact
            else None
        let results =
            match cachedArtifact with
            | Some _ ->
                reportProgress "Checking documentation pages" pages.Length pages.Length
                []
            | None ->
                // Only page-selected projects need compiler evaluation. Evaluating every project
                // leaks solution composition into documentation checking and can make an unrelated
                // project-reference graph fail capture. Other documented projects contribute their
                // already-built assemblies to the aggregate reference context.
                let selectedProjects = pages |> List.map _.SelectedProject |> List.distinct
                let evaluated = selectedProjects |> List.map (fun path -> path, DocumentationCompiler.evaluateProject path)
                let builtAssemblies =
                    resolvedProjects
                    |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                    |> List.filter (String.IsNullOrWhiteSpace >> not)
                let aggregateReferences =
                    (evaluated |> List.collect (snd >> _.References)) @ builtAssemblies
                    |> List.distinct
                let evaluatedProjects = evaluated |> List.map (fun (path, project) -> path, { project with References = aggregateReferences }) |> Map.ofList
                let completed = ref 0
                pages
                |> List.map (fun page ->
                    async {
                        let selectedEvaluation =
                            match page.TargetFramework with
                            | None -> evaluatedProjects.[page.SelectedProject]
                            | Some _ ->
                                let selected =
                                    DocumentationCompiler.evaluateProjectFor page.TargetFramework page.SelectedProject

                                let references =
                                    selected.References @ aggregateReferences
                                    |> List.distinctBy (Path.GetFileName >> _.ToUpperInvariant())

                                { selected with
                                    References = references }

                        let! checkedUnits =
                            DocumentationCompiler.checkBlocksWithProject selectedEvaluation page.Prelude page.Blocks

                        let current = Threading.Interlocked.Increment(completed)
                        reportProgress "Checking documentation pages" current pages.Length
                        return checkedUnits
                    })
                |> fun checks -> Async.Parallel(checks, maxDegreeOfParallelism = max 1 Environment.ProcessorCount)
                |> Async.RunSynchronously
                |> Array.toList
                |> List.collect id
        DocumentationDiscovery.validateCoverage blocks

        { Blocks = blocks
          Results = results
          Prelude = defaultPrelude
          CachedArtifact = cachedArtifact
          CachePath = cachePath }

    let analyzeWithProgress
        reportProgress
        prelude
        (projectPaths: string list)
        (projectFingerprint: string)
        (package: PackageModel)
        =
        let legacyPages =
            pages projectPaths package
            |> List.map (fun page -> { page with Prelude = prelude })

        analyzePagesWithProgress reportProgress prelude projectPaths projectFingerprint package legacyPages

    let analyzeDocsSetsWithProgress reportProgress (sets: DocsSet list) projectPaths projectFingerprint package =
        let fallbackPrelude =
            sets |> List.tryHead |> Option.bind _.FSharpPrelude |> Option.defaultValue ""

        analyzePagesWithProgress
            reportProgress
            fallbackPrelude
            projectPaths
            projectFingerprint
            package
            (pagesForDocsSets sets projectPaths package)

    let analyze prelude projectPaths projectFingerprint package =
        analyzeWithProgress (fun _ _ _ -> ()) prelude projectPaths projectFingerprint package

    let analyzeDocsSets sets projectPaths projectFingerprint package =
        analyzeDocsSetsWithProgress (fun _ _ _ -> ()) sets projectPaths projectFingerprint package

    /// Materializes and caches the semantic artifact represented by an analysis result.
    let semanticArtifact (analysis: Analysis) =
        match analysis.CachedArtifact with
        | Some artifact -> artifact
        | None ->
            let artifact = SemanticExtractor.artifact analysis.Results
            let directory = Path.GetDirectoryName analysis.CachePath
            Directory.CreateDirectory(directory) |> ignore
            File.WriteAllText(
                analysis.CachePath,
                Newtonsoft.Json.JsonConvert.SerializeObject(
                    artifact,
                    Newtonsoft.Json.Formatting.Indented,
                    Serialization.jsonSettings))
            for stale in Directory.GetFiles(directory, "*.semantic.json") do
                if not (Path.GetFullPath(stale).Equals(Path.GetFullPath(analysis.CachePath), StringComparison.Ordinal)) then
                    File.Delete(stale)
            artifact

    /// Counts authored blocks with compiler errors, independent of how a caller presents them.
    let compilerFailureCount (analysis: Analysis) =
        let failedBlockIds =
            match analysis.CachedArtifact with
            | Some artifact ->
                artifact.Pages
                |> List.collect _.Blocks
                |> List.choose (fun block ->
                    if block.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
                    then Some block.Id
                    else None)
            | None ->
                analysis.Results
                |> List.collect _.Diagnostics
                |> List.choose (fun diagnostic ->
                    if diagnostic.Severity = SemanticDiagnosticSeverity.Error then diagnostic.BlockId else None)
        failedBlockIds |> List.distinct |> List.length
