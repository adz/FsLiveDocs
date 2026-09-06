namespace FsLiveDocs.Cli

open System
open System.IO
open FsLiveDocs.Core
open FsLiveDocs.Renderer

/// Resolves documentation-set ownership and produces the common renderer/capture inputs.
module internal DocumentationSets =

    type Prepared =
        { Sites: SiteBuilder.DocsSetSite list
          Sets: ReleaseDocsSet list
          StaticFiles: (string * string * string list) list }

    let private routePrefix path =
        if String.IsNullOrEmpty path then
            ""
        else
            path.Trim('/') + "/"

    let releaseSets (sets: DocsSet list) (package: PackageModel) =
        sets
        |> List.map (fun set ->
            let packageNames = set.Projects |> List.map SymbolLister.packageName |> Set.ofList

            let ids =
                if not set.Api then
                    []
                elif packageNames.IsEmpty then
                    []
                else
                    (if isNull (box package.Packages) then
                         []
                     else
                         package.Packages)
                    |> List.filter (fun info -> packageNames.Contains info.Name)
                    |> List.collect _.EntityIds
                    |> List.distinct

            ({ Id = set.Id
               Title = set.Title
               Source = set.Source
               Path = set.Path
               Projects = set.Projects
               IsDefault = set.IsDefault
               Sidebar = set.Sidebar
               Api = set.Api
               ApiEntityIds = ids
               FSharpPrelude = set.FSharpPrelude }
            : ReleaseDocsSet))

    let private validateAndCollectOutputs (sets: ReleaseDocsSet list) guideOutputs =
        let outputs =
            [ yield! guideOutputs
              for set in sets do
                  let setIndex = routePrefix set.Path + "index.html"
                  // An authored index owns the set root. SiteBuilder only generates this
                  // fallback when no authored page produced it.
                  if not (guideOutputs |> List.contains setIndex) then
                      yield setIndex

                  if set.Api then
                      yield routePrefix set.Path + "api/index.html"

                      for id in set.ApiEntityIds do
                          yield routePrefix set.Path + "api/" + id + ".html" ]

        match outputs |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) with
        | Some(path, _) -> invalidOp $"Documentation sets generate the same output path: {path}"
        | None -> Set.ofList outputs

    let private apiRoutesFor (sets: ReleaseDocsSet list) (currentSet: ReleaseDocsSet) =
        let ordered =
            sets
            |> List.sortBy (fun set ->
                if set.Id = currentSet.Id then 0
                elif set.IsDefault then 1
                else 2)

        ordered
        |> List.collect (fun set -> set.ApiEntityIds |> List.map (fun id -> id, routePrefix set.Path))
        |> List.rev
        |> Map.ofList

    let private guideFiles sourceDir files =
        files
        |> List.filter (fun path ->
            let relative = Path.GetRelativePath(sourceDir, path).Replace('\\', '/')
            not (relative.StartsWith("api/", StringComparison.OrdinalIgnoreCase)))

    let prepareCurrent
        (usesDocumentationSets: bool)
        (sets: DocsSet list)
        (package: PackageModel)
        (artifact: SemanticDocumentationArtifact)
        siteRootPath
        =
        let root = Directory.GetCurrentDirectory()
        let capturedSets = releaseSets sets package

        let ownedFiles =
            sets
            |> List.map (fun set ->
                let sourceDir = Path.GetFullPath(set.Source, root)

                let files =
                    if Directory.Exists sourceDir then
                        Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
                        |> Array.filter (fun path ->
                            let relative = Path.GetRelativePath(root, path).Replace('\\', '/')
                            DocsSet.ownerOf sets relative |> Option.exists (fun owner -> owner.Id = set.Id))
                        |> Array.toList
                    else
                        []

                set, sourceDir, files)

        let guideOutputs =
            ownedFiles
            |> List.collect (fun (set, sourceDir, files) ->
                ContentProvider.setGuideOutputs sourceDir (DocsSet.routePrefix set) (guideFiles sourceDir files))

        let allowed = validateAndCollectOutputs capturedSets guideOutputs

        let sites =
            (ownedFiles, capturedSets)
            ||> List.map2 (fun (set, sourceDir, files) captured ->
                let options =
                    { SemanticCode.defaults with
                        Artifact = Some artifact
                        Prelude = set.FSharpPrelude |> Option.defaultValue "" }

                let apiRoutes = apiRoutesFor capturedSets captured

                let apiPackage =
                    if captured.Api then
                        ContentProvider.applyApiDocsForSet
                            sourceDir
                            root
                            package
                            (DocsSet.routePrefix set)
                            allowed
                            apiRoutes
                            options
                    else
                        package

                let pages =
                    ContentProvider.scanDocsSet
                        { SourceDir = sourceDir
                          SnippetSourceDir = root
                          Package = apiPackage
                          RoutePrefix = DocsSet.routePrefix set
                          SemanticPrefix = if usesDocumentationSets then set.Id + "/" else ""
                          SiteRootPath = siteRootPath
                          AllowedOutputs = allowed
                          SemanticCode = options
                          ApiRoutes = apiRoutes
                          Files = guideFiles sourceDir files }

                ({ Set = captured
                   Package = apiPackage
                   Pages = pages }
                : SiteBuilder.DocsSetSite))

        let staticFiles =
            ownedFiles
            |> List.map (fun (set, sourceDir, files) ->
                sourceDir,
                DocsSet.routePrefix set,
                files
                |> List.filter (fun path ->
                    not (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))))

        let staticTargets =
            staticFiles
            |> List.collect (fun (sourceDir, prefix, files) ->
                files
                |> List.map (fun path -> prefix + Path.GetRelativePath(sourceDir, path).Replace('\\', '/')))

        match staticTargets |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) with
        | Some(path, _) -> invalidOp $"Documentation sets contain duplicate static output path: {path}"
        | None -> ()

        match staticTargets |> List.tryFind allowed.Contains with
        | Some path -> invalidOp $"A documentation static file collides with a generated page: {path}"
        | None -> ()

        { Sites = sites
          Sets = capturedSets
          StaticFiles = staticFiles }

    let prepareCaptured (materializedRoot: string) (content: ReleaseContentArtifact) package artifact siteRootPath =
        let guideOutputs =
            [ for set in content.DocsSets do
                  let prefix = routePrefix set.Path

                  let sourceDir =
                      Path.Combine(materializedRoot, prefix.Replace('/', Path.DirectorySeparatorChar))

                  let files =
                      content.Pages
                      |> List.filter (fun page ->
                          page.SetId = set.Id
                          && not (page.SourcePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase)))
                      |> List.map (fun page ->
                          Path.Combine(sourceDir, page.SourcePath.Replace('/', Path.DirectorySeparatorChar)))

                  yield! ContentProvider.setGuideOutputs sourceDir prefix files ]

        let allowed = validateAndCollectOutputs content.DocsSets guideOutputs

        let sites =
            content.DocsSets
            |> List.map (fun set ->
                let prefix = routePrefix set.Path

                let sourceDir =
                    Path.Combine(materializedRoot, prefix.Replace('/', Path.DirectorySeparatorChar))

                let options =
                    { SemanticCode.defaults with
                        Artifact = Some artifact
                        Prelude = set.FSharpPrelude |> Option.defaultValue "" }

                let apiRoutes = apiRoutesFor content.DocsSets set

                let apiPackage =
                    if set.Api then
                        ContentProvider.applyApiDocsForSet
                            sourceDir
                            materializedRoot
                            package
                            prefix
                            allowed
                            apiRoutes
                            options
                    else
                        package

                let files =
                    content.Pages
                    |> List.filter (fun page ->
                        page.SetId = set.Id
                        && not (page.SourcePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase)))
                    |> List.map (fun page ->
                        Path.Combine(sourceDir, page.SourcePath.Replace('/', Path.DirectorySeparatorChar)))

                let pages =
                    ContentProvider.scanDocsSet
                        { SourceDir = sourceDir
                          SnippetSourceDir = materializedRoot
                          Package = apiPackage
                          RoutePrefix = prefix
                          SemanticPrefix = set.Id + "/"
                          SiteRootPath = siteRootPath
                          AllowedOutputs = allowed
                          SemanticCode = options
                          ApiRoutes = apiRoutes
                          Files = files }

                ({ Set = set
                   Package = apiPackage
                   Pages = pages }
                : SiteBuilder.DocsSetSite))

        { Sites = sites
          Sets = content.DocsSets
          StaticFiles = [] }

    let captureAssets (prepared: Prepared) =
        prepared.StaticFiles
        |> List.collect (fun (sourceDir, prefix, files) ->
            files
            |> List.map (fun path ->
                prefix + Path.GetRelativePath(sourceDir, path).Replace('\\', '/'), File.ReadAllBytes path))
