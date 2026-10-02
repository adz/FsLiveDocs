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

/// Discovers and compiler-checks canonical documentation pages.
module internal DocAnalysis =

    let private packageCodec = Json.compile ApiSchema.packageModel
    let private semanticArtifactCodec = Json.compile SemanticSchema.semanticDocumentationArtifact

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
          Errors: (string * (int * int * string)) list
          Prelude: string
          Artifact: SemanticDocumentationArtifact option
          CachePath: string
          /// <summary>The resolved pages the analysis walked, so callers do not repeat the scan.</summary>
          Pages: Page list }

    type private ApiNameCandidate = { FullName: string; OpenPath: string option }

    let private fSharpFullName (entity: EntityModel) =
        let withoutArity = System.Text.RegularExpressions.Regex.Replace(entity.Id, @"`\d+$", "")
        if entity.Kind = EntityKind.Module && withoutArity.EndsWith("Module", StringComparison.Ordinal) then
            withoutArity.Substring(0, withoutArity.Length - "Module".Length)
        else
            withoutArity

    let private apiNameCandidates (package: PackageModel) name =
        let rec collect parentOpenPath (entities: EntityModel list) =
            [ for entity in entities do
                  let openPath =
                      match entity.Kind with
                      | EntityKind.Namespace
                      | EntityKind.Module -> Some(fSharpFullName entity)
                      | _ -> parentOpenPath

                  let fullName =
                      match entity.Kind, parentOpenPath with
                      | (EntityKind.Namespace | EntityKind.Module), _ -> fSharpFullName entity
                      | _, Some parent -> $"{parent}.{entity.Name}"
                      | _, None -> fSharpFullName entity

                  if entity.Name.Equals(name, StringComparison.Ordinal) then
                      yield { FullName = fullName; OpenPath = openPath }

                  yield! collect openPath entity.Entities ]

        collect None package.Entities
        |> List.distinctBy (fun candidate -> candidate.FullName, candidate.OpenPath)
        |> List.sortBy _.FullName

    let internal addApiNameHint (package: PackageModel) (errorNumber: int) (message: string) =
        if errorNumber <> 39 then
            message
        else
            let missingName = System.Text.RegularExpressions.Regex.Match(message, "'(?<name>[^']+)' is not defined\\.")
            if not missingName.Success then
                message
            else
                let name = missingName.Groups["name"].Value
                match apiNameCandidates package name with
                | [ candidate ] ->
                    match candidate.OpenPath with
                    | Some openPath -> $"{message} Did you mean to open {openPath}?"
                    | None -> $"{message} The documented symbol is {candidate.FullName}; qualify it explicitly."
                | _ :: _ :: _ as candidates ->
                    let names = candidates |> List.map _.FullName |> String.concat ", "
                    $"{message} Matching public API symbols: {names}. Open or fully qualify the intended symbol to resolve the ambiguity."
                | _ -> message

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

        let sourceDir = Run.orFallback FileSystem.getCurrentDirectory (Directory.GetCurrentDirectory())
        let resolvedProjects = projectPaths |> List.map Path.GetFullPath

        let describe (path: string) =
            Path.GetRelativePath(sourceDir, path).Replace('\\', '/')

        // Every existence check, directory listing, and file read this scan needs is gathered
        // into one Flow and run exactly once; the loop below is pure, consuming the gathered
        // results and raising the same validation errors the original imperative walk raised.
        let gatherWork =
            sets
            |> Flow.traverse (fun set ->
                let docsDir = Path.GetFullPath(set.Source, sourceDir)

                flow {
                    let! docsDirExists = FileSystem.directoryExists docsDir

                    let! setProjectChecks =
                        set.Projects
                        |> Flow.traverse (fun project ->
                            let full = Path.GetFullPath(project, sourceDir)
                            FileSystem.fileExists full |> Flow.map (fun exists -> project, full, exists))

                    let! files =
                        if docsDirExists then
                            FileSystem.getFiles docsDir "*.md" SearchOption.AllDirectories
                            |> Flow.map (Array.sort >> Array.toList)
                        else
                            Flow.succeed []

                    let! fileData =
                        files
                        |> Flow.traverse (fun path ->
                            flow {
                                let! raw = FileSystem.readAllText path
                                let frontMatter = ContentProvider.parseFrontMatter raw

                                let configuredProject =
                                    frontMatter |> Option.bind (fun (metadata, _) -> metadata.Project)

                                let! candidates =
                                    match configuredProject with
                                    | None -> Flow.succeed None
                                    | Some configured ->
                                        [ Path.GetFullPath(configured, sourceDir)
                                          Path.GetFullPath(configured, docsDir) ]
                                        |> Flow.traverse (fun p ->
                                            FileSystem.fileExists p |> Flow.map (fun exists -> p, exists))
                                        |> Flow.map Some

                                return path, raw, candidates
                            })

                    return set, docsDir, docsDirExists, setProjectChecks, fileData
                })

        let gathered =
            Run.orRaise FileSystemError.describe "Could not scan documentation sets" gatherWork

        let resolveSetProject (set: DocsSet) (setProjectChecks: (string * string * bool) list) project =
            let _, full, exists =
                setProjectChecks |> List.find (fun (candidate, _, _) -> candidate = project)

            if not exists then
                invalidOp $"Documentation set {set.Id} project does not exist: {project}"

            if not (resolvedProjects |> List.contains full) then
                invalidOp
                    $"Documentation set {set.Id} selects {describe full}, but that project was not passed to livedocs. Build, test, watch, and capture operate on the union of every set's projects."

            full

        [ for set, docsDir, docsDirExists, setProjectChecks, fileData in gathered do
              if not docsDirExists then
                  invalidOp $"Documentation directory for set {set.Id} is missing: {docsDir}"

              let setProjects = set.Projects |> List.map (resolveSetProject set setProjectChecks)

              let defaultProject =
                  setProjects |> List.tryHead |> Option.defaultValue (List.head resolvedProjects)

              for path, raw, candidates in fileData do
                  let repositoryRelative = Path.GetRelativePath(sourceDir, path).Replace('\\', '/')

                  match DocsSet.ownerOf sets repositoryRelative with
                  | Some owner when owner.Id = set.Id ->
                      let sourcePath = Path.GetRelativePath(docsDir, path).Replace('\\', '/')

                      let relative =
                          if useSetIdentity then
                              set.Id + "/" + sourcePath
                          else
                              sourcePath

                      let frontMatter = ContentProvider.parseFrontMatter raw
                      let body = frontMatter |> Option.map snd |> Option.defaultValue raw

                      let metadata =
                          frontMatter
                          |> Option.map fst
                          |> Option.defaultValue
                              (ContentMetadata.empty (ContentProvider.defaultTitle path))

                      let selectedProject =
                          match frontMatter |> Option.bind (fun (metadata, _) -> metadata.Project) with
                          | None -> defaultProject
                          | Some configured ->
                              candidates.Value
                              |> List.tryFind snd
                              |> Option.map fst
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
        let packageFingerprint = Json.serialize packageCodec package
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
            let work =
                flow {
                    let! exists = FileSystem.fileExists cachePath
                    if exists then
                        let! text = FileSystem.readAllText cachePath
                        let artifact = Json.deserialize semanticArtifactCodec text
                        return if artifact.SchemaVersion <> History.SemanticSchemaVersion then None else Some artifact
                    else
                        return None
                }
            Run.orRaise FileSystemError.describe $"Could not read semantic cache {cachePath}" work
        let errors, artifact =
            match cachedArtifact with
            | Some artifact ->
                reportProgress "Checking documentation pages" pages.Length pages.Length
                [], Some artifact
            | None ->
                // The complete artifact above is the fastest no-change path. On an incremental
                // miss, retain locality by loading each page independently: an edit to one guide
                // must not make every other guide cross the compiler seam again.
                let pageCommonContext =
                    [ $"semantic-schema:{History.SemanticSchemaVersion}"
                      $"compiler-mvid:{typeof<EvaluatedProject>.Assembly.ManifestModule.ModuleVersionId}"
                      $"project-inputs:{projectFingerprint}"
                      packageFingerprint ]
                    |> String.concat "\n"

                let pageCachePath page =
                    let framework = page.TargetFramework |> Option.defaultValue "<default>"
                    let blockIdentities =
                        page.Blocks |> List.map (fun block -> $"{block.Id}|{block.SourceHash}")
                    let key =
                        AnalysisCache.pageKey
                            pageCommonContext
                            page.Relative
                            framework
                            page.Prelude
                            blockIdentities
                    Path.Combine(cacheDirectory, key + ".semantic-page.json") |> Path.GetFullPath

                let tryReadPage page =
                    let path = pageCachePath page
                    let work =
                        flow {
                            let! exists = FileSystem.fileExists path
                            if exists then
                                let! text = FileSystem.readAllText path
                                let artifact = Json.deserialize semanticArtifactCodec text
                                return if artifact.SchemaVersion = History.SemanticSchemaVersion then Some artifact else None
                            else
                                return None
                        }
                    Run.orRaise FileSystemError.describe $"Could not read semantic page cache {path}" work

                let pageStates = pages |> List.map (fun page -> page, tryReadPage page)
                let cachedPages = pageStates |> List.choose snd
                let missingPages = pageStates |> List.choose (fun (page, cached) -> if cached.IsNone then Some page else None)
                let mutable completed = cachedPages.Length
                reportProgress "Checking documentation pages" completed pages.Length

                // Only projects selected by cache misses need compiler evaluation. This is the
                // key incremental property: unchanged pages do not even require an FCS project.
                let selectedProjects = missingPages |> List.map _.SelectedProject |> List.distinct
                let evaluated = selectedProjects |> List.map (fun path -> path, DocumentationCompiler.evaluateProject path)
                let builtAssemblies =
                    resolvedProjects
                    |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
                    |> List.filter (String.IsNullOrWhiteSpace >> not)
                let aggregateReferences =
                    (evaluated |> List.collect (snd >> _.References)) @ builtAssemblies
                    |> List.distinct
                let evaluatedProjects =
                    evaluated
                    |> List.map (fun (path, project) -> path, { project with References = aggregateReferences })
                    |> Map.ofList

                // A page that pins a target framework must compile against that framework's
                // reference assemblies alone. Concatenating the default framework's references
                // (netstandard2.1 beside net8.0, say) makes inline SRTP overloads resolve
                // differently, so the framework's own built project assemblies are gathered here.
                let frameworkBuiltAssemblies = Collections.Generic.Dictionary<string, string list>()

                let builtAssembliesFor framework =
                    match frameworkBuiltAssemblies.TryGetValue framework with
                    | true, assemblies -> assemblies
                    | _ ->
                        let assemblies =
                            resolvedProjects
                            |> List.map (fun projectPath ->
                                // A project may not declare the page's framework; its default build is
                                // still compatible with it, so fall back rather than reject the page.
                                let target =
                                    try
                                        (ProjectResolver.documentationBuildFor (Some framework) projectPath).TargetPath
                                    with :? InvalidOperationException ->
                                        None

                                match target with
                                | Some path -> path
                                | None -> ProjectResolver.resolveAssemblyPath projectPath)
                            |> List.filter (String.IsNullOrWhiteSpace >> not)
                        frameworkBuiltAssemblies.[framework] <- assemblies
                        assemblies

                let writePageCache page artifact =
                    let path = pageCachePath page
                    let work =
                        flow {
                            do! FileSystem.createDirectory (Path.GetDirectoryName path)
                            do! FileSystem.writeAllText path (Json.serialize semanticArtifactCodec artifact)
                        }
                    Run.orRaise FileSystemError.describe $"Could not write semantic page cache {path}" work

                let evaluationFor (selectedProject, targetFramework) =
                    match targetFramework with
                    | None -> evaluatedProjects.[selectedProject]
                    | Some framework ->
                        let selected = DocumentationCompiler.evaluateProjectFor (Some framework) selectedProject
                        let references =
                            selected.References @ builtAssembliesFor framework
                            |> List.distinctBy (Path.GetFileName >> _.ToUpperInvariant())
                        { selected with References = references }

                let projectGroups =
                    missingPages
                    |> List.groupBy (fun page -> page.SelectedProject, page.TargetFramework)

                // One generated F# project amortizes reference imports and lets FCS reuse its
                // project graph. Shards bound the check-result graph retained before semantic
                // records are projected and persisted page by page.
                let checkedPages =
                    [ for projectKey, projectPages in projectGroups do
                          let selectedEvaluation = evaluationFor projectKey
                          for shard in projectPages |> List.chunkBySize 32 do
                              let requests =
                                  shard |> List.map (fun page -> page.Relative, page.Prelude, page.Blocks)
                              let checkedByPage =
                                  DocumentationCompiler.checkPagesWithProject selectedEvaluation requests
                                  |> Async.RunSynchronously

                              for page in shard do
                                  let checkedUnits = checkedByPage |> Map.tryFind page.Relative |> Option.defaultValue []
                                  let errors =
                                      checkedUnits
                                      |> List.collect _.Diagnostics
                                      |> List.filter (fun item -> item.Severity = SemanticDiagnosticSeverity.Error)
                                      |> List.choose (fun item ->
                                          item.BlockId
                                          |> Option.map (fun id ->
                                              id,
                                              (item.StartLine, item.StartColumn, addApiNameHint package item.ErrorNumber item.Message)))
                                  let semantic =
                                      if errors.IsEmpty then
                                          let artifact = SemanticExtractor.artifact checkedUnits
                                          writePageCache page artifact
                                          Some artifact
                                      else None
                                  completed <- completed + 1
                                  reportProgress "Checking documentation pages" completed pages.Length
                                  yield errors, semantic ]

                let errors = checkedPages |> List.collect fst
                let artifacts = cachedPages @ (checkedPages |> List.choose snd)
                let artifact =
                    if not errors.IsEmpty then None
                    else
                        Some {
                            SchemaVersion = History.SemanticSchemaVersion
                            Prelude = defaultPrelude
                            Pages = artifacts |> List.collect _.Pages |> List.sortBy _.SourcePath
                        }
                errors, artifact
        DocumentationDiscovery.validateCoverage blocks

        { Blocks = blocks
          Errors = errors
          Prelude = defaultPrelude
          Artifact = artifact
          CachePath = cachePath
          Pages = pages }

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
        let artifact =
            analysis.Artifact
            |> Option.defaultWith (fun () -> invalidOp "Cannot create semantic data while documentation contains compiler errors.")
        let directory = Path.GetDirectoryName analysis.CachePath
        let serialized = Json.serialize semanticArtifactCodec artifact

        let work =
            flow {
                do! FileSystem.createDirectory directory
                do! FileSystem.writeAllText analysis.CachePath serialized
                let! staleFiles = FileSystem.getFiles directory "*.semantic.json" SearchOption.TopDirectoryOnly

                for stale in staleFiles do
                    if not (Path.GetFullPath(stale).Equals(Path.GetFullPath(analysis.CachePath), StringComparison.Ordinal)) then
                        do! FileSystem.deleteFile stale
            }

        Run.orRaise FileSystemError.describe $"Could not write semantic cache {analysis.CachePath}" work
        artifact

    /// Counts authored blocks with compiler errors, independent of how a caller presents them.
    let compilerFailureCount (analysis: Analysis) =
        analysis.Errors |> List.map fst |> List.distinct |> List.length
