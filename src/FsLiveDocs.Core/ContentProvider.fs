namespace FsLiveDocs.Core

open System.IO
open System.Text.RegularExpressions
open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines
open Markdig.Renderers.Html
open Markdig.Extensions.CustomContainers
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions
open YamlDotNet.RepresentationModel
open Axial
open Axial.FileSystem
open FsLiveDocs.Core.Effects

/// <summary>Provides capabilities to load, parse, and resolve Markdown documentation pages.</summary>
/// <example name="ResolveSnippetExample" data-livedocs="snapshot">
/// > let package = { Version = "1.0"; Entities = []; Scenarios = []; Packages = []; Organization = ApiOrganizationModel.empty };;
/// val package: PackageModel = { Version = "1.0"
///   Entities = []
///   Scenarios = []
///   Packages = []
///   Organization = { Families = []
///                    PackageSections = [] } }
///
/// > ContentProvider.resolveSnippets "Hello" "." package "";;
/// val it: string = "Hello"
/// </example>
module ContentProvider =

    type private SourceOutputPath =
        { SourcePath: string
          OutputPath: string }

    let private siteOutputRoot = Path.GetFullPath("output")

    let private addLinkError (linkErrors: ResizeArray<string>) (message: string) =
        linkErrors.Add message

    /// <summary>Raises one error containing all broken local documentation links collected during a build.</summary>
    let throwLinkErrors (linkErrors: ResizeArray<string>) =
        if linkErrors.Count > 0 then
            invalidOp (String.concat System.Environment.NewLine linkErrors)

    /// <summary>Matches the shortcode that transcludes an XML example into a page.</summary>
    /// <remarks>
    /// Shared so that callers can discover which examples a page pulls in without expanding it,
    /// which is how an example that no page transcludes is identified as unverified.
    /// </remarks>
    let exampleShortcodePattern = @"{{<\s*example\s+id=""(?<id>[^""]+)""\s*>}}"

    /// <summary>Names of the XML examples a page body transcludes.</summary>
    let transcludedExampleNames (body: string) =
        Regex.Matches(body, exampleShortcodePattern)
        |> Seq.map (fun m -> m.Groups.["id"].Value)
        |> Set.ofSeq

    let private stripOrderingPrefix (value: string) =
        Regex.Replace(value, @"^\d+[\s._-]*", "")

    let private slug (value: string) =
        stripOrderingPrefix value |> fun part -> part.ToLowerInvariant()

    let private sectionOrderFor (docsDir: string) (filePath: string) =
        let relative = Path.GetRelativePath(docsDir, filePath).Replace('\\', '/')
        let firstPart = relative.Split('/').[0]
        let orderingPrefix = Regex.Match(firstPart, @"^(?<order>\d+)")
        if orderingPrefix.Success then int orderingPrefix.Groups.["order"].Value
        else System.Int32.MaxValue

    /// <summary>Maps a Markdown file to its stable output path relative to the docs root.</summary>
    let outputPathFor (docsDir: string) (filePath: string) =
        let relative = Path.GetRelativePath(docsDir, filePath).Replace('\\', '/')
        let parts = relative.Split('/') |> Array.toList
        let directories = parts |> List.take (parts.Length - 1) |> List.map slug
        let stem = Path.GetFileNameWithoutExtension(List.last parts)
        let fileName =
            if stem.Equals("index", System.StringComparison.OrdinalIgnoreCase)
               || stem.Equals("_index", System.StringComparison.OrdinalIgnoreCase) then
                "index.html"
            else
                slug stem + ".html"
        String.concat "/" (directories @ [ fileName ])

    /// <summary>Copies one file to its destination, creating the destination directory first if needed.</summary>
    let private copyFileEnsuringDestination (source: string) (destination: string) =
        flow {
            let destinationDirectory = Path.GetDirectoryName(destination)
            let! destinationDirectoryExists = FileSystem.directoryExists destinationDirectory
            if not destinationDirectoryExists then
                do! FileSystem.createDirectory destinationDirectory
            do! FileSystem.copyFile source destination true
        }

    /// <summary>Copies consumer-owned non-Markdown files from the docs tree into the generated site.</summary>
    let copyStaticFiles (docsDir: string) (outputDir: string) =
        let work =
            flow {
                let! docsDirExists = FileSystem.directoryExists docsDir
                if docsDirExists then
                    let! files = FileSystem.getFiles docsDir "*" SearchOption.AllDirectories
                    let staticFiles =
                        files
                        |> Array.filter (fun file -> not (Path.GetExtension(file).Equals(".md", System.StringComparison.OrdinalIgnoreCase)))
                    for source in staticFiles do
                        let relative = Path.GetRelativePath(docsDir, source)
                        let destination = Path.Combine(outputDir, relative)
                        do! copyFileEnsuringDestination source destination
            }
        Run.orRaise FileSystemError.describe $"Could not copy static files from {docsDir}" work

    /// <summary>Copies the explicitly owned static files of one documentation set beneath its route.</summary>
    let copyStaticFilesForSet (sourceDir: string) (routePrefix: string) (files: string list) (outputDir: string) =
        let work =
            flow {
                for source in files do
                    let relative = Path.GetRelativePath(sourceDir, source)

                    let destination =
                        Path.Combine(outputDir, routePrefix.Replace('/', Path.DirectorySeparatorChar), relative)

                    do! copyFileEnsuringDestination source destination
            }
        Run.orRaise FileSystemError.describe $"Could not copy static files for route '{routePrefix}'" work

    let defaultTitle (filePath: string) =
        let stem = Path.GetFileNameWithoutExtension(filePath) |> stripOrderingPrefix
        if stem.Equals("index", System.StringComparison.OrdinalIgnoreCase)
           || stem.Equals("_index", System.StringComparison.OrdinalIgnoreCase) then
            Path.GetDirectoryName(filePath)
            |> Path.GetFileName
            |> stripOrderingPrefix
        else stem
        |> fun value ->
            value.Split([| '-'; '_'; ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun part ->
                if part.Length = 0 then part
                else part.Substring(0, 1).ToUpperInvariant() + part.Substring(1))
            |> String.concat " "

    /// <summary>The shared Markdig pipeline with advanced extensions enabled.</summary>
    let pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()
    
    /// <summary>The YAML deserializer for frontmatter processing.</summary>
    let deserializer =
        DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()

    /// <summary>Helper to find the index of an element starting from a given position.</summary>
    let findIndexIteration start f (arr: 'T[]) =
        let mutable found = None
        let mutable i = start
        while i < arr.Length && found.IsNone do
            if f arr.[i] then found <- Some i
            i <- i + 1
        found

    /// <summary>Parses the YAML frontmatter from a raw Markdown string.</summary>
    let parseFrontMatter (content: string) =
        let lines = content.Split([| "\n"; "\r\n" |], System.StringSplitOptions.None)
        if lines.Length > 0 && lines.[0] = "---" then
            let endIndex = findIndexIteration 1 (fun l -> l = "---") lines
            match endIndex with
            | Some i ->
                let yaml = String.concat "\n" lines.[1..i-1]
                let body = String.concat "\n" lines.[i+1..]
                let metadata = deserializer.Deserialize<ContentMetadata>(yaml)
                // YamlDotNet leaves omitted list fields null; the model promises empty lists.
                let metadata =
                    { metadata with
                        Tags = if isNull (box metadata.Tags) then [] else metadata.Tags
                        BlogList =
                            metadata.BlogList
                            |> Option.map (fun options -> if isNull (box options.Show) then { options with Show = [] } else options) }
                Some (metadata, body)
            | None -> None
        else
            None

    /// <summary>Maps a page with blog metadata to its render-time permalink. Dated pages use a
    /// flat, stable slug beneath <c>blog/</c>; ordinary documentation keeps its legacy route.</summary>
    let outputPathForMetadata (docsDir: string) (filePath: string) (metadata: ContentMetadata option) =
        match metadata |> Option.bind _.Date with
        | None -> outputPathFor docsDir filePath
        | Some _ ->
            let configuredSlug = metadata |> Option.bind _.Slug
            let sourceSlug = Path.GetFileNameWithoutExtension(filePath) |> stripOrderingPrefix
            let value: string = configuredSlug |> Option.defaultValue sourceSlug |> slug
            if System.String.IsNullOrWhiteSpace value then invalidOp $"Dated post {filePath} needs a non-empty slug."
            "blog/" + value + "/index.html"

    let private outputPathForFile docsDir filePath =
        let metadata = File.ReadAllText(filePath) |> parseFrontMatter |> Option.map fst
        outputPathForMetadata docsDir filePath metadata

    let private sourceRelativePath (docsDir: string) (filePath: string) =
        Path.GetRelativePath(Path.GetFullPath docsDir, Path.GetFullPath filePath).Replace('\\', '/')

    let private sourcePathKey (relativePath: string) =
        relativePath.Replace('\\', '/').ToUpperInvariant()

    let private sourceOutputPathsForFiles (docsDir: string) (routePrefix: string) (files: string list) =
        files
        |> List.map (fun filePath ->
            let sourcePath = sourceRelativePath docsDir filePath
            let key = sourcePath |> sourcePathKey
            key,
            { SourcePath = sourcePath
              OutputPath = routePrefix + outputPathForFile docsDir filePath })
        |> Map.ofList

    /// <summary>Searches for a member by ID or Name within a PackageModel.</summary>
    let findMember (id: string) (package: PackageModel) =
        let rec searchEntities (entities: EntityModel list) =
            entities |> Seq.tryPick (fun e ->
                match e.Members |> List.tryFind (fun m -> m.Id = id || m.Name = id) with
                | Some m -> Some m
                | None -> searchEntities e.Entities
            )
        searchEntities package.Entities

    let findEntity (id: string) (package: PackageModel) =
        let rec searchEntities (entities: EntityModel list) =
            entities |> Seq.tryPick (fun e ->
                if e.Id = id || e.Name = id then Some e
                else searchEntities e.Entities
            )
        searchEntities package.Entities

    let findExample (id: string) (package: PackageModel) =
        let rec collect (entities: EntityModel list) =
            seq {
                for e in entities do
                    if not (isNull (box e.Examples)) then
                        yield! e.Examples
                    for m in e.Members do
                        yield! m.Examples
                    yield! collect e.Entities
            }

        collect package.Entities |> Seq.tryFind (fun ex -> ex.Name = id)

    let private normalizeOutputPath (currentOutputPath: string) (href: string) =
        let cleaned = href.Split([| '#'; '?' |], 2).[0].Trim()
        if System.String.IsNullOrWhiteSpace(cleaned) then None
        elif cleaned.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
             || cleaned.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
             || cleaned.StartsWith("mailto:", System.StringComparison.OrdinalIgnoreCase)
             || cleaned.StartsWith("tel:", System.StringComparison.OrdinalIgnoreCase)
             || cleaned.StartsWith("xref:", System.StringComparison.Ordinal) then None
        else
            let currentDir = Path.GetDirectoryName(currentOutputPath)
            let candidate =
                if cleaned = "/" then "index.html"
                elif cleaned.StartsWith("/") then cleaned.TrimStart('/')
                elif System.String.IsNullOrWhiteSpace currentDir then cleaned
                else Path.Combine(currentDir, cleaned)

            let full = Path.GetFullPath(Path.Combine(siteOutputRoot, candidate))
            let relative = Path.GetRelativePath(siteOutputRoot, full).Replace('\\', '/')
            Some relative

    let private splitHrefPath (href: string) =
        let suffixStart = href.IndexOfAny([| '#'; '?' |])
        if suffixStart < 0 then href, ""
        else href.Substring(0, suffixStart), href.Substring(suffixStart)

    let private sourceTargetPath (docsDir: string) (currentSourcePath: string) (hrefPath: string) =
        let docsRoot = Path.GetFullPath docsDir
        let targetPath =
            if hrefPath = "/" then docsRoot
            elif hrefPath.StartsWith("/", System.StringComparison.Ordinal) then
                Path.Combine(docsRoot, hrefPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
            else
                Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath currentSourcePath),
                    hrefPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))

        let relative = Path.GetRelativePath(docsRoot, Path.GetFullPath targetPath).Replace('\\', '/')
        let isOutsideRoot =
            Path.IsPathRooted(relative)
            || relative = ".."
            || relative.StartsWith("../", System.StringComparison.Ordinal)
        Path.GetFullPath targetPath, relative, isOutsideRoot

    let private sourcePathCandidates (docsDir: string) (currentSourcePath: string) (hrefPath: string) =
        let _, relative, isOutsideRoot = sourceTargetPath docsDir currentSourcePath hrefPath
        if isOutsideRoot then
            []
        else
            let extension = Path.GetExtension(relative)
            if extension.Equals(".md", System.StringComparison.OrdinalIgnoreCase) then
                [ relative ]
            elif relative = "." || System.String.IsNullOrEmpty extension then
                let trimmed = relative.TrimEnd('/')
                if trimmed = "." || System.String.IsNullOrEmpty trimmed then
                    [ "index.md"; "_index.md" ]
                else
                    [ trimmed + ".md"; trimmed + "/index.md"; trimmed + "/_index.md" ]
            else
                []

    let private tryResolveSourceOutput
        (docsDir: string)
        (currentSourcePath: string)
        (currentOutputPath: string)
        (sourceOutputPaths: Map<string, SourceOutputPath>)
        (hrefPath: string)
        =
        let bySourcePath =
            sourcePathCandidates docsDir currentSourcePath hrefPath
            |> List.tryPick (fun candidate -> sourceOutputPaths |> Map.tryFind (sourcePathKey candidate))

        match bySourcePath with
        | Some _ -> bySourcePath
        | None when System.String.IsNullOrEmpty(Path.GetExtension(hrefPath)) ->
            let outputTarget = normalizeOutputPath currentOutputPath hrefPath
            let candidates =
                outputTarget
                |> Option.map (fun target ->
                    let trimmed = target.TrimEnd('/')
                    [ trimmed + ".html"; trimmed + "/index.html" ])
                |> Option.defaultValue []
            sourceOutputPaths
            |> Map.toSeq
            |> Seq.tryPick (fun (_, outputPath) ->
                if candidates |> List.exists (fun candidate -> System.String.Equals(candidate, outputPath.OutputPath, System.StringComparison.OrdinalIgnoreCase)) then
                    Some outputPath
                else
                    None)
        | None -> None

    let private outputFolderAliases (allowedOutputs: Set<string>) =
        allowedOutputs
        |> Set.toList
        |> List.collect (fun outputPath ->
            if outputPath.Equals("index.html", System.StringComparison.OrdinalIgnoreCase) then
                [ "", outputPath; "/", outputPath ]
            elif outputPath.EndsWith("/index.html", System.StringComparison.OrdinalIgnoreCase) then
                let folderPath = outputPath.Substring(0, outputPath.Length - "index.html".Length)
                [ folderPath, outputPath; folderPath.TrimEnd('/'), outputPath ]
            else
                [])
        |> List.map (fun (alias, outputPath) -> sourcePathKey alias, outputPath)
        |> Map.ofList

    let private tryResolveOutputFolderAlias
        (currentOutputPath: string)
        (outputFolderAliases: Map<string, string>)
        (hrefPath: string)
        =
        normalizeOutputPath currentOutputPath hrefPath
        |> Option.map (fun target ->
            let trimmed = target.TrimEnd('/')
            [ target; trimmed; trimmed + "/" ])
        |> Option.defaultValue []
        |> List.tryPick (fun candidate -> outputFolderAliases |> Map.tryFind (sourcePathKey candidate))

    let private resolveLocalOutput
        (docsDir: string)
        (currentSourcePath: string)
        (currentOutputPath: string)
        (allowedOutputs: Set<string>)
        (sourceOutputPaths: Map<string, SourceOutputPath>)
        (outputFolderAliases: Map<string, string>)
        (href: string)
        =
        let hrefPath, _ = splitHrefPath href
        if hrefPath.EndsWith(".html", System.StringComparison.OrdinalIgnoreCase) then
            normalizeOutputPath currentOutputPath href
            |> Option.filter allowedOutputs.Contains
        else
            let sourcePathOutput =
                tryResolveSourceOutput docsDir currentSourcePath currentOutputPath sourceOutputPaths hrefPath
            match sourcePathOutput with
            | Some page when allowedOutputs.Contains page.OutputPath -> Some page.OutputPath
            | Some _ -> None
            | None when System.String.IsNullOrEmpty(Path.GetExtension(hrefPath)) ->
                tryResolveOutputFolderAlias currentOutputPath outputFolderAliases hrefPath
                |> Option.filter allowedOutputs.Contains
            | None -> None

    let private looksLikePageHref (hrefPath: string) =
        let extension = Path.GetExtension(hrefPath)
        hrefPath.EndsWith("/", System.StringComparison.Ordinal)
        || System.String.IsNullOrEmpty(extension)
        || extension.Equals(".md", System.StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".html", System.StringComparison.OrdinalIgnoreCase)

    let private brokenLinkMessage
        (docsDir: string)
        (currentSourcePath: string)
        (currentOutputPath: string)
        (sourceOutputPaths: Map<string, SourceOutputPath>)
        (href: string)
        =
        let hrefPath, _ = splitHrefPath href
        let targetFullPath, targetRelativePath, isOutsideRoot = sourceTargetPath docsDir currentSourcePath hrefPath
        let attemptedOutputPath = normalizeOutputPath currentOutputPath href |> Option.defaultValue targetRelativePath
        let mappedPage =
            let outputCandidates =
                if hrefPath.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase) then
                    [ Path.ChangeExtension(attemptedOutputPath, ".html").Replace('\\', '/') ]
                elif hrefPath.EndsWith(".html", System.StringComparison.OrdinalIgnoreCase) then
                    [ attemptedOutputPath ]
                elif System.String.IsNullOrEmpty(Path.GetExtension(hrefPath)) then
                    let trimmed = attemptedOutputPath.TrimEnd('/')
                    [ trimmed + ".html"; trimmed + "/index.html" ]
                else
                    []

            sourceOutputPaths
            |> Map.toSeq
            |> Seq.tryPick (fun (_, page) ->
                if outputCandidates |> List.exists (fun candidate -> System.String.Equals(candidate, page.OutputPath, System.StringComparison.OrdinalIgnoreCase)) then
                    Some page
                else
                    None)

        let detail =
            if isOutsideRoot then
                $"resolves to source path `{targetRelativePath}` outside the docs root. Use a plain path in backticks or a full URL."
            elif Directory.Exists targetFullPath then
                $"resolves to folder `{targetRelativePath}`; folder links are not pages."
            else
                match mappedPage with
                | Some page ->
                    $"resolves to `{attemptedOutputPath}`, but the source page is `{page.SourcePath}` and generates `{page.OutputPath}`."
                | None ->
                    $"resolves to source path `{targetRelativePath}` and generated path `{attemptedOutputPath}`, which does not resolve to a generated page."

        $"Broken documentation link in {currentOutputPath}: [{href}] {detail}"

    let private withProtectedCodeSegments (text: string) (tokenPrefix: string) (action: string -> string) =
        let protectedSegments = ResizeArray<string>()
        let protectCodeSegments (input: string) =
            let codePattern = @"(?s)```.*?```|`[^`\r\n]+`"
            System.Text.RegularExpressions.Regex.Replace(input, codePattern, fun (m: System.Text.RegularExpressions.Match) ->
                let token = $"@@{tokenPrefix}_{protectedSegments.Count}@@"
                protectedSegments.Add(m.Value)
                token)

        let restoreCodeSegments (input: string) =
            protectedSegments
            |> Seq.mapi (fun i (segment: string) -> $"@@{tokenPrefix}_{i}@@", segment)
            |> Seq.fold (fun (acc: string) (token, segment) -> acc.Replace(token, segment)) input

        text |> protectCodeSegments |> action |> restoreCodeSegments

    let private validateLinks
        (docsDir: string)
        (currentSourcePath: string)
        (currentOutputPath: string)
        (allowedOutputs: Set<string>)
        (sourceOutputPaths: Map<string, SourceOutputPath>)
        (outputFolderAliases: Map<string, string>)
        (linkErrors: ResizeArray<string>)
        (body: string)
        =
        let linkPattern = @"(?<!\!)\[[^\]]+\]\((?<href>[^)]+)\)"
        withProtectedCodeSegments body "FSLIVEDOCS_VALIDATE_CODE" (fun protectedBody ->
            for m in System.Text.RegularExpressions.Regex.Matches(protectedBody, linkPattern) do
                let href = m.Groups.["href"].Value.Trim().Trim('"')
                match normalizeOutputPath currentOutputPath href with
                | None -> ()
                | Some _ ->
                    let hrefPath, _ = splitHrefPath href
                    if looksLikePageHref hrefPath
                       && resolveLocalOutput docsDir currentSourcePath currentOutputPath allowedOutputs sourceOutputPaths outputFolderAliases href |> Option.isNone then
                        addLinkError linkErrors (brokenLinkMessage docsDir currentSourcePath currentOutputPath sourceOutputPaths href)
            protectedBody)
        |> ignore

    let private rewriteLocalLinks
        (docsDir: string)
        (currentSourcePath: string)
        (currentOutputPath: string)
        (allowedOutputs: Set<string>)
        (sourceOutputPaths: Map<string, SourceOutputPath>)
        (outputFolderAliases: Map<string, string>)
        (body: string)
        =
        let linkPattern = @"(?<!\!)(?<prefix>\[[^\]]+\]\()(?<href>[^\s\)]+)(?<suffix>[^\)]*\))"
        withProtectedCodeSegments body "FSLIVEDOCS_REWRITE_LINKS" (fun protectedBody ->
            Regex.Replace(protectedBody, linkPattern, fun (m: Match) ->
                let href = m.Groups.["href"].Value.Trim().Trim('"')
                match resolveLocalOutput docsDir currentSourcePath currentOutputPath allowedOutputs sourceOutputPaths outputFolderAliases href with
                | Some resolved ->
                    let _, hrefSuffix = splitHrefPath href
                    let currentDirectory = Path.GetDirectoryName(currentOutputPath)
                    let relative =
                        if System.String.IsNullOrWhiteSpace currentDirectory then resolved
                        else Path.GetRelativePath(currentDirectory, resolved).Replace('\\', '/')
                    m.Groups.["prefix"].Value + relative + hrefSuffix + m.Groups.["suffix"].Value
                | None -> m.Value))

    let private collectEntityIds (entities: EntityModel list) =
        let rec walk acc (items: EntityModel list) =
            match items with
            | [] -> acc
            | e :: rest ->
                walk (e.Id :: acc) (e.Entities @ rest)
        walk [] entities

    /// <summary>Entity ids contributed by a package, used to scope a set's API surface.</summary>
    let entityIdsOf (entities: EntityModel list) = collectEntityIds entities

    let private markdownFilesIn (docsDir: string) =
        let work =
            flow {
                let! exists = FileSystem.directoryExists docsDir
                if exists then
                    let! files = FileSystem.getFiles docsDir "*.md" SearchOption.AllDirectories
                    return
                        files
                        |> Array.filter (fun f ->
                            not (f.Contains($"{Path.DirectorySeparatorChar}api{Path.DirectorySeparatorChar}")))
                        |> Array.toList
                else
                    return []
            }
        Run.orRaise FileSystemError.describe $"Could not list Markdown files under {docsDir}" work

    let private collectGuideOutputs (docsDir: string) =
        markdownFilesIn docsDir |> List.map (outputPathForFile docsDir)

    let private collectAllowedOutputs (docsDir: string) (package: PackageModel) =
        let guideOutputs = collectGuideOutputs docsDir
        let apiOutputs = collectEntityIds package.Entities |> List.map (fun id -> $"api/{id}.html")
        Set.ofList (guideOutputs @ apiOutputs @ [ "index.html"; "api.html" ])

    /// <summary>
    /// The content of every <c>&lt;snippet:*&gt;</c>-delimited region across a source tree's F# files,
    /// scanned once so a page with several <c>{{&lt; snippet &gt;}}</c> shortcodes reads each file only once.
    /// </summary>
    let private loadSourceSnippetFiles (sourceDir: string) =
        let work =
            flow {
                let! files = FileSystem.getFiles sourceDir "*.fs" SearchOption.AllDirectories
                return!
                    files
                    |> Array.toList
                    |> Flow.traverse (fun f -> FileSystem.readAllLines f |> Flow.map (fun lines -> f, lines))
            }
        Run.orRaise FileSystemError.describe $"Could not scan {sourceDir} for source snippets" work

    let private findSourceSnippet (id: string) (files: (string * string array) list) =
        files
        |> List.tryPick (fun (_, lines) ->
            let start = Array.tryFindIndex (fun (l: string) -> l.Contains($"<snippet:{id}>")) lines
            let stop = Array.tryFindIndex (fun (l: string) -> l.Contains($"</snippet:{id}>")) lines
            match start, stop with
            | Some s, Some e -> Some(String.concat "\n" lines.[s + 1 .. e - 1])
            | _ -> None)

    /// <summary>Expands source and XML-example transclusions while preserving semantic cross-references.</summary>
    let expandTransclusions (body: string) (sourceDir: string) (package: PackageModel) =
        let snippetPattern = @"{{<\s*snippet\s+(?<args>[^>]+)>}}"
        let examplePattern = exampleShortcodePattern

        // Scanned once up front (only when the page actually transcludes a snippet) so the
        // file-system work composes into a single Flow run rather than one run per shortcode match.
        let sourceSnippetFiles =
            if Regex.IsMatch(body, snippetPattern) then loadSourceSnippetFiles sourceDir else []

        withProtectedCodeSegments body "FSLIVEDOCS_CODE" (fun protectedBody ->
            // 1. Resolve {{< snippet id="X" >}}
            let body1 =
                System.Text.RegularExpressions.Regex.Replace(protectedBody, snippetPattern, fun (m: System.Text.RegularExpressions.Match) ->
                    let args = m.Groups.["args"].Value
                    let attribute name =
                        let pattern = "(?:^|\\s)" + Regex.Escape(name) + "=\"(?<value>[^\"]*)\""
                        let found = Regex.Match(args, pattern)
                        if found.Success then Some found.Groups.["value"].Value else None
                    let id = attribute "id" |> Option.defaultWith (fun () -> invalidOp "A snippet shortcode requires id=\"...\".")
                    let snippet = findSourceSnippet id sourceSnippetFiles
                    match snippet with
                    | Some s ->
                        let mode = attribute "mode" |> Option.defaultValue ""
                        let reason = attribute "reason"
                        let info =
                            match mode.ToLowerInvariant(), reason with
                            | "", None -> "fsharp"
                            | "no-check", Some explanation when not (System.String.IsNullOrWhiteSpace explanation) -> $"fsharp no-check reason=\"{explanation}\""
                            | "no-check", _ -> invalidOp $"Snippet '{id}' uses no-check without a reason."
                            | selected, None when set [ "prepare"; "isolated"; "run" ] |> Set.contains selected -> $"fsharp {selected}"
                            | selected, _ -> invalidOp $"Snippet '{id}' has unsupported mode '{selected}'."
                        $"```{info} origin=source-snippet\n{s}\n```"
                    | None -> invalidOp $"Snippet '{id}' was not found."
                )

            // 2. Resolve {{< example id="X" >}}
            let body2 =
                System.Text.RegularExpressions.Regex.Replace(body1, examplePattern, fun (m: System.Text.RegularExpressions.Match) ->
                    let id = m.Groups.["id"].Value
                    match findExample id package with
                    | Some ex ->
                        let info =
                            // An example excluded at its declaration stays excluded once transcluded,
                            // carrying the author's reason onto the fence.
                            match ex.NoCheckReason with
                            | Some reason -> $"fsharp no-check reason=\"{reason}\""
                            | None ->
                            if ex.Content.Replace("\r\n", "\n").Split('\n') |> Array.exists (fun line -> line.TrimStart().StartsWith("> ")) then
                                "fsharp transcript"
                            elif ex.IsSnapshotTest then "fsharp run"
                            else "fsharp"
                        $"```{info} origin=xml-example\n{ex.Content}\n```"
                    | None -> invalidOp $"Example '{id}' was not found."
                )

            body2)

    type private ApiLink = { Label: string; Url: string }

    let private apiLinksWithRoutes (package: PackageModel) (rootPath: string) (apiRoutes: Map<string, string>) =
        let links = ResizeArray<string * ApiLink>()
        let add alias label url =
            if not (System.String.IsNullOrWhiteSpace alias) then
                links.Add(alias, { Label = label; Url = url })

        let rec collect (entities: EntityModel list) =
            for entity in entities do
                match apiRoutes |> Map.tryFind entity.Id with
                | None when not apiRoutes.IsEmpty -> ()
                | route ->
                    let routeValue = route |> Option.defaultValue ""
                    let entityUrl = $"{rootPath}{routeValue}api/{entity.Id}.html"
                    add entity.Id entity.Name entityUrl
                    add entity.Name entity.Name entityUrl
                    let ownerName = entity.Name.Split('<').[0]

                    for member' in entity.Members do
                        let memberUrl = $"{entityUrl}#{member'.Id}"
                        add member'.Id member'.Name memberUrl
                        add $"{ownerName}.{member'.Name}" member'.Name memberUrl

                collect entity.Entities
        collect package.Entities

        links
        |> Seq.groupBy fst
        |> Seq.choose (fun (alias, candidates) ->
            let distinct = candidates |> Seq.map snd |> Seq.distinctBy _.Url |> Seq.toList
            match distinct with
            | [ link ] -> Some(alias, link)
            | _ -> None)
        |> Map.ofSeq

    let private apiLinks (package: PackageModel) (rootPath: string) =
        apiLinksWithRoutes package rootPath Map.empty

    let private resolveApiTarget (links: Map<string, ApiLink>) (target: string) =
        let separator = target.LastIndexOf(':')
        let id = if separator >= 0 then target.Substring(separator + 1) else target
        links |> Map.tryFind id

    /// <summary>Resolves bare semantic cross-references during rendering.</summary>
    let private resolveCrossReferences (body: string) (package: PackageModel) (rootPath: string) =
        let xrefPattern = @"(?<!\]\()xref:(?<type>[A-Z]):(?<id>[^\s\)]+)"
        let links = apiLinks package rootPath
        withProtectedCodeSegments body "FSLIVEDOCS_XREF" (fun protectedBody ->
            Regex.Replace(protectedBody, xrefPattern, fun (matched: Match) ->
                let symbolType = matched.Groups.["type"].Value
                let symbolId = matched.Groups.["id"].Value
                let target = $"{symbolType}:{symbolId}"
                match resolveApiTarget links target with
                | Some link -> $"[{link.Label}]({link.Url})"
                | None -> invalidOp $"Cross-reference '{target}' was not found."))

    let private resolveCrossReferencesWithRoutes (body: string) (package: PackageModel) (rootPath: string) apiRoutes =
        let xrefPattern = @"(?<!\]\()xref:(?<type>[A-Z]):(?<id>[^\s\)]+)"
        let links = apiLinksWithRoutes package rootPath apiRoutes

        withProtectedCodeSegments body "FSLIVEDOCS_XREF" (fun protectedBody ->
            Regex.Replace(
                protectedBody,
                xrefPattern,
                fun (matched: Match) ->
                    let symbolType = matched.Groups.["type"].Value
                    let symbolId = matched.Groups.["id"].Value
                    let target = $"{symbolType}:{symbolId}"

                    match resolveApiTarget links target with
                    | Some link -> $"[{link.Label}]({link.Url})"
                    | None -> invalidOp $"Cross-reference '{target}' was not found."
            ))

    /// Resolves explicit xref links and unambiguous inline-code API names on the parsed Markdown tree.
    let private renderMarkdownWithApiLinksAndRoutes
        (body: string)
        (package: PackageModel)
        (rootPath: string)
        apiRoutes
        =
        let document = Markdown.Parse(body, pipeline)
        let links = apiLinksWithRoutes package rootPath apiRoutes

        // A `::: rendered` custom container frames sample output so a reader can tell a
        // demonstration of what Markdown renders to from the page's own content. Its headings
        // are deliberately excluded from the on-this-page navigation (see the renderer).
        for container in document.Descendants<CustomContainer>() |> Seq.toList do
            if container.Info = "rendered" then
                container.GetAttributes().AddClass("livedocs-rendered")

        let inlineRoots =
            document.Descendants<LeafBlock>()
            |> Seq.choose (fun block -> if isNull block.Inline then None else Some block.Inline)
            |> Seq.toList
        let linkNodes = inlineRoots |> Seq.collect _.FindDescendants<LinkInline>() |> Seq.toArray
        let codeNodes = inlineRoots |> Seq.collect _.FindDescendants<CodeInline>() |> Seq.toArray

        for link in linkNodes do
            if not (isNull link.Url) && link.Url.StartsWith("xref:", System.StringComparison.Ordinal) then
                match resolveApiTarget links link.Url with
                | Some target -> link.Url <- target.Url
                | None -> invalidOp $"Cross-reference '{link.Url}' was not found."

        for code in codeNodes do
            if not (code.Parent :? LinkInline) then
                match links |> Map.tryFind code.Content with
                | Some target ->
                    let link = LinkInline(target.Url, null)
                    code.ReplaceBy(link) |> ignore
                    link.AppendChild(code) |> ignore
                | None -> ()

        Markdown.ToHtml(document, pipeline)

    let private renderMarkdownWithApiLinks (body: string) (package: PackageModel) (rootPath: string) =
        renderMarkdownWithApiLinksAndRoutes body package rootPath Map.empty

    /// <summary>Resolves transclusions and semantic links for a current render.</summary>
    let resolveSnippets (body: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        expandTransclusions body sourceDir package
        |> fun expanded -> resolveCrossReferences expanded package rootPath

    type private MarkdownContext =
        {
            DocsDir: string
            SourceDir: string
            Package: PackageModel
            RootPath: string
            CurrentOutputPath: string
            AllowedOutputs: Set<string>
            SourceOutputPaths: Map<string, SourceOutputPath>
            OutputFolderAliases: Map<string, string>
            LinkErrors: ResizeArray<string>
            /// Route prefix ("" or e.g. "internal/") prepended to this page's semantic source path so a
            /// documentation set's persisted blocks stay uniquely keyed across the shared site.
            RoutePrefix: string
            /// Per-entity route prefixes for globally resolved cross-set API links.
            ApiRoutes: Map<string, string>
            SemanticCode: SemanticCode.Options
        }

    let private resolveMarkdown (context: MarkdownContext) (sourcePath: string) (body: string) =
        let resolved =
            expandTransclusions body context.SourceDir context.Package
            |> fun expanded ->
                if context.ApiRoutes.IsEmpty then
                    resolveCrossReferences expanded context.Package context.RootPath
                else
                    resolveCrossReferencesWithRoutes expanded context.Package context.RootPath context.ApiRoutes

        let rewritten =
            rewriteLocalLinks
                context.DocsDir
                sourcePath
                context.CurrentOutputPath
                context.AllowedOutputs
                context.SourceOutputPaths
                context.OutputFolderAliases
                resolved

        validateLinks
            context.DocsDir
            sourcePath
            context.CurrentOutputPath
            context.AllowedOutputs
            context.SourceOutputPaths
            context.OutputFolderAliases
            context.LinkErrors
            rewritten

        let semanticSourcePath =
            context.RoutePrefix
            + Path.GetRelativePath(context.DocsDir, sourcePath).Replace('\\', '/')

        let formatted =
            SemanticCode.formatFences context.SemanticCode semanticSourcePath rewritten

        let semanticSegments = ResizeArray<string>()
        let semanticPattern =
            Regex(
                Regex.Escape(SemanticCode.htmlStartMarker) + "(?<html>.*?)" + Regex.Escape(SemanticCode.htmlEndMarker),
                RegexOptions.Singleline)
        let protectedMarkdown =
            semanticPattern.Replace(
                formatted,
                fun matched ->
                    let index = semanticSegments.Count
                    semanticSegments.Add(matched.Groups.["html"].Value)
                    // A comment placeholder does not start an HTML block, so Markdown following a
                    // semantically rendered fence remains available to Markdig for normal parsing.
                    $"<!--fslivedocs-semantic-placeholder:{index}-->"
            )

        let rendered =
            if context.ApiRoutes.IsEmpty then
                renderMarkdownWithApiLinks protectedMarkdown context.Package context.RootPath
            else
                renderMarkdownWithApiLinksAndRoutes protectedMarkdown context.Package context.RootPath context.ApiRoutes

        semanticSegments
        |> Seq.mapi (fun index html -> $"<!--fslivedocs-semantic-placeholder:{index}-->", html)
        |> Seq.fold (fun (current: string) (placeholder, html) -> current.Replace(placeholder, html)) rendered

    let private loadMarkdownPage (context: MarkdownContext) (filePath: string) (outputPath: string) (raw: string) =
        match parseFrontMatter raw with
        | Some (metadata, body) ->
            let contentHtml = resolveMarkdown context filePath body
            let labels =
                [ metadata.Platform |> Option.map (fun value -> $"Platform: {System.Net.WebUtility.HtmlEncode value}")
                  metadata.TargetFramework |> Option.map (fun value -> $"Target: {System.Net.WebUtility.HtmlEncode value}") ]
                |> List.choose id
            let contentHtml =
                if labels.IsEmpty then contentHtml
                else
                    let labelText = String.concat " · " labels
                    $"<aside class=\"livedocs-checking-context not-prose\" aria-label=\"Example checking context\">{labelText}</aside>" + contentHtml
            { Metadata = metadata; ContentHtml = contentHtml; Markdown = body; FilePath = filePath; OutputPath = outputPath; SectionOrder = System.Int32.MaxValue }
        | None ->
            let contentHtml = resolveMarkdown context filePath raw
            { Metadata = ContentMetadata.empty (defaultTitle filePath)
              ContentHtml = contentHtml
              Markdown = raw
              FilePath = filePath
              OutputPath = outputPath
              SectionOrder = System.Int32.MaxValue }

    /// <summary>Loads and processes a single Markdown page.</summary>
    /// <param name="filePath">The markdown file to read.</param>
    /// <param name="sourceDir">The root directory used to resolve snippet shortcodes.</param>
    /// <param name="package">The extracted package model used for examples and xrefs.</param>
    /// <param name="rootPath">The relative root path used when generating links.</param>
    /// <param name="currentOutputPath">The output HTML path used for link validation.</param>
    /// <param name="allowedOutputs">The set of known output pages used to validate local links.</param>
    /// <returns>A processed content page ready for rendering.</returns>
    let loadPage (filePath: string) (sourceDir: string) (package: PackageModel) (rootPath: string) (currentOutputPath: string) (allowedOutputs: Set<string>) =
        let raw =
            Run.orRaise FileSystemError.describe $"Could not read Markdown page {filePath}" (FileSystem.readAllText filePath)
        let docsDir = Path.GetDirectoryName(Path.GetFullPath filePath)
        let sourceOutputPaths = markdownFilesIn docsDir |> sourceOutputPathsForFiles docsDir ""
        let linkErrors = ResizeArray<string>()
        let page =
            loadMarkdownPage
                { DocsDir = docsDir
                  SourceDir = sourceDir
                  Package = package
                  RootPath = rootPath
                  CurrentOutputPath = currentOutputPath
                  AllowedOutputs = allowedOutputs
                  SourceOutputPaths = sourceOutputPaths
                  OutputFolderAliases = outputFolderAliases allowedOutputs
                  LinkErrors = linkErrors
                  RoutePrefix = ""
                  ApiRoutes = Map.empty
                  SemanticCode = SemanticCode.defaults }
                filePath
                currentOutputPath
                raw
        throwLinkErrors linkErrors
        page

    /// <summary>Inputs for scanning one documentation set's Markdown into rendered pages.</summary>
    type DocsSetScan =
        {
            /// <summary>The set's Markdown root directory.</summary>
            SourceDir: string
            /// <summary>Root used to resolve <c>{{&lt; snippet &gt;}}</c> shortcodes (usually the repository root).</summary>
            SnippetSourceDir: string
            /// <summary>The global package model, shared for cross-references and semantic tooltips.</summary>
            Package: PackageModel
            /// <summary>Route prefix ("" for the site-root default set, otherwise e.g. "internal/").</summary>
            RoutePrefix: string
            /// <summary>Stable set-id prefix used for semantic page/block identity.</summary>
            SemanticPrefix: string
            /// <summary>Site-relative root path ("" for the current build, "../../" for a history render).</summary>
            SiteRootPath: string
            /// <summary>Every generated output path across all sets, so cross-set links validate.</summary>
            AllowedOutputs: Set<string>
            /// <summary>Semantic formatting options carrying this set's prelude and release artifact.</summary>
            SemanticCode: SemanticCode.Options
            /// Entity id to documentation-set route prefix, used to resolve cross-set xrefs.
            ApiRoutes: Map<string, string>
            /// <summary>Absolute Markdown file paths owned by this set (API enrichment files excluded).</summary>
            Files: string list
        }

    let private scanFileList
        (docsDir: string)
        (sourceDir: string)
        (package: PackageModel)
        (siteRootPath: string)
        (routePrefix: string)
        (semanticPrefix: string)
        (allowedOutputs: Set<string>)
        (apiRoutes: Map<string, string>)
        (semanticCode: SemanticCode.Options)
        (files: string list)
        (linkErrors: ResizeArray<string>)
        =
        // Every file's raw text is read up front as one composed Flow, run once, rather than once
        // per file inside the map below.
        let rawByFile =
            let work =
                files |> Flow.traverse (fun f -> FileSystem.readAllText f |> Flow.map (fun raw -> f, raw))
            Run.orRaise FileSystemError.describe $"Could not read Markdown pages under {docsDir}" work
            |> Map.ofList

        let outputPathsByFile =
            files |> List.map (fun filePath -> filePath, routePrefix + outputPathForFile docsDir filePath) |> Map.ofList

        let sourceOutputPaths =
            outputPathsByFile
            |> Map.toList
            |> List.map (fun (filePath, outputPath) ->
                let sourcePath = sourceRelativePath docsDir filePath
                sourcePath |> sourcePathKey,
                { SourcePath = sourcePath
                  OutputPath = outputPath })
            |> Map.ofList
        let outputFolderAliases = outputFolderAliases allowedOutputs

        files
        |> List.toArray
        |> Array.map (fun f ->
            let outputPath = outputPathsByFile.[f]
            let depth = outputPath.Split('/').Length - 1
            let pageRootPath = siteRootPath + String.replicate depth "../"

            let page =
                loadMarkdownPage
                    { DocsDir = docsDir
                      SourceDir = sourceDir
                      Package = package
                      RootPath = pageRootPath
                      CurrentOutputPath = outputPath
                      AllowedOutputs = allowedOutputs
                      SourceOutputPaths = sourceOutputPaths
                      OutputFolderAliases = outputFolderAliases
                      LinkErrors = linkErrors
                      RoutePrefix = semanticPrefix
                      ApiRoutes = apiRoutes
                      SemanticCode = semanticCode }
                    f
                    outputPath
                    rawByFile.[f]

            { page with
                SectionOrder = sectionOrderFor docsDir f })
        |> Array.groupBy (fun page -> page.OutputPath)
        |> Array.map (fun (outputPath, pages) ->
            if pages.Length > 1 then
                let sources = pages |> Array.map (fun page -> page.FilePath) |> String.concat ", "
                invalidOp $"Documentation output path collision at {outputPath}: {sources}"

            pages.[0])
        |> Array.toList

    /// <summary>Scans guides and semantically formats F# fences, appending local-link errors to a shared collection.</summary>
    let scanDocsWithOptionsWithLinkErrors
        (docsDir: string)
        (sourceDir: string)
        (package: PackageModel)
        (rootPath: string)
        (semanticCode: SemanticCode.Options)
        (linkErrors: ResizeArray<string>)
        =
        let work =
            flow {
                let! exists = FileSystem.directoryExists docsDir
                if exists then
                    let! files = FileSystem.getFiles docsDir "*.md" SearchOption.AllDirectories
                    return
                        files
                        |> Array.filter (fun f -> not (f.Contains("/api/")))
                        |> Array.toList
                        |> Some
                else
                    return None
            }
        match Run.orRaise FileSystemError.describe $"Could not scan {docsDir} for guides" work with
        | None -> []
        | Some files ->
            let allowedOutputs = collectAllowedOutputs docsDir package
            scanFileList docsDir sourceDir package rootPath "" "" allowedOutputs Map.empty semanticCode files linkErrors

    /// <summary>Scans guides and semantically formats F# fences using the supplied assembly references.</summary>
    let scanDocsWithOptions (docsDir: string) (sourceDir: string) (package: PackageModel) (rootPath: string) (semanticCode: SemanticCode.Options) =
        let linkErrors = ResizeArray<string>()
        let pages = scanDocsWithOptionsWithLinkErrors docsDir sourceDir package rootPath semanticCode linkErrors
        throwLinkErrors linkErrors
        pages

    /// <summary>Scans one documentation set's Markdown, honoring its route prefix and shared allowed outputs.</summary>
    let scanDocsSetWithLinkErrors (linkErrors: ResizeArray<string>) (scan: DocsSetScan) =
        scanFileList
            scan.SourceDir
            scan.SnippetSourceDir
            scan.Package
            scan.SiteRootPath
            scan.RoutePrefix
            scan.SemanticPrefix
            scan.AllowedOutputs
            scan.ApiRoutes
            scan.SemanticCode
            scan.Files
            linkErrors

    let scanDocsSet (scan: DocsSetScan) =
        let linkErrors = ResizeArray<string>()
        let pages = scanDocsSetWithLinkErrors linkErrors scan
        throwLinkErrors linkErrors
        pages

    /// <summary>Guide output paths for a set's files, prefixed by its route, for building the shared allowed-output set.</summary>
    let setGuideOutputs (sourceDir: string) (routePrefix: string) (files: string list) =
        files |> List.map (fun f -> routePrefix + outputPathFor sourceDir f)

    /// <summary>Scans the docs directory and loads all guide pages.</summary>
    /// <param name="docsDir">The docs root containing markdown pages.</param>
    /// <param name="sourceDir">The source root used for snippet resolution.</param>
    /// <param name="package">The extracted package model used for xrefs and examples.</param>
    /// <param name="rootPath">The relative root path used when rendering links.</param>
    /// <returns>All guide pages found under the docs directory.</returns>
    let scanDocs (docsDir: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        scanDocsWithOptions docsDir sourceDir package rootPath SemanticCode.defaults

    let private applyApiDocsCore
        (apiDocsDir: string)
        (sourceDir: string)
        (package: PackageModel)
        (routePrefix: string)
        (allowedOutputs: Set<string>)
        (apiRoutes: Map<string, string>)
        (_semanticCode: SemanticCode.Options)
        (linkErrors: ResizeArray<string>)
        =
        let apiDocsWork =
            flow {
                let! exists = FileSystem.directoryExists apiDocsDir
                if exists then
                    let! files = FileSystem.getFiles apiDocsDir "*.md" SearchOption.TopDirectoryOnly
                    let! withRaw =
                        files
                        |> Array.toList
                        |> Flow.traverse (fun f -> FileSystem.readAllText f |> Flow.map (fun raw -> f, raw))
                    return Some withRaw
                else
                    return None
            }

        match Run.orRaise FileSystemError.describe $"Could not read API documentation under {apiDocsDir}" apiDocsWork with
        | None -> package
        | Some docFiles ->
            let docsRoot = Path.GetDirectoryName(Path.GetFullPath apiDocsDir)
            let guideSourceOutputPaths =
                markdownFilesIn docsRoot |> sourceOutputPathsForFiles docsRoot routePrefix
            let apiSourceOutputPaths =
                docFiles
                |> List.map (fun (filePath, _) ->
                    let id = Path.GetFileNameWithoutExtension filePath
                    let sourcePath = sourceRelativePath docsRoot filePath
                    sourcePath |> sourcePathKey,
                    { SourcePath = sourcePath
                      OutputPath = routePrefix + $"api/{id}.html" })
                |> Map.ofList
            let sourceOutputPaths =
                Map.fold (fun paths key value -> Map.add key value paths) guideSourceOutputPaths apiSourceOutputPaths

            let yamlScalar (node: YamlNode) : string =
                match node with
                | :? YamlScalarNode as scalar -> scalar.Value
                | _ -> ""
            let tryChild (name: string) (mapping: YamlMappingNode) : YamlNode option =
                mapping.Children
                |> Seq.tryPick (fun pair -> if yamlScalar pair.Key = name then Some pair.Value else None)
            let stringList (node: YamlNode) : string list =
                match node with
                | :? YamlSequenceNode as values -> values.Children |> Seq.map yamlScalar |> Seq.filter (System.String.IsNullOrWhiteSpace >> not) |> Seq.toList
                | :? YamlScalarNode as value when not (System.String.IsNullOrWhiteSpace value.Value) -> [ value.Value ]
                | _ -> []
            let parseDirective (defaultId: string) (raw: string) : ApiOrganizationModel.FamilyDirective option =
                match parseFrontMatter raw with
                | None -> None
                | Some _ ->
                    let ending = raw.IndexOf("\n---", 3, System.StringComparison.Ordinal)
                    if ending < 0 then None else
                    let yaml = raw.Substring(4, ending - 4)
                    let stream = YamlStream()
                    use reader = new StringReader(yaml)
                    stream.Load(reader)
                    match stream.Documents.[0].RootNode with
                    | :? YamlMappingNode as root ->
                        match tryChild "api" root with
                        | Some (:? YamlMappingNode as api) ->
                            let familyId = tryChild "family" api |> Option.map yamlScalar |> Option.filter (System.String.IsNullOrWhiteSpace >> not) |> Option.defaultValue defaultId
                            let name = tryChild "name" api |> Option.map yamlScalar |> Option.filter (System.String.IsNullOrWhiteSpace >> not)
                            let sections =
                                match tryChild "sections" api with
                                | Some (:? YamlSequenceNode as items) ->
                                    items.Children
                                    |> Seq.choose (function
                                        | :? YamlMappingNode as sectionInfo ->
                                            let id = tryChild "id" sectionInfo |> Option.map yamlScalar |> Option.defaultValue ""
                                            if System.String.IsNullOrWhiteSpace id then None else
                                            let facets =
                                                match tryChild "facets" sectionInfo with
                                                | Some (:? YamlMappingNode as values) -> values.Children |> Seq.map (fun pair -> yamlScalar pair.Key, stringList pair.Value) |> Seq.toList
                                                | _ -> []
                                            Some ({
                                                Id = id
                                                Title = tryChild "title" sectionInfo |> Option.map yamlScalar |> Option.filter (System.String.IsNullOrWhiteSpace >> not) |> Option.defaultValue id
                                                Summary = tryChild "summary" sectionInfo |> Option.map yamlScalar |> Option.filter (System.String.IsNullOrWhiteSpace >> not) |> Option.map (Documentation.markdown >> List.singleton) |> Option.defaultValue []
                                                Order = tryChild "order" sectionInfo |> Option.map yamlScalar |> Option.bind (fun value -> match System.Int32.TryParse value with | true, number -> Some number | _ -> None) |> Option.defaultValue 100
                                                Symbols = tryChild "symbols" sectionInfo |> Option.map stringList |> Option.defaultValue []
                                                Members = tryChild "members" sectionInfo |> Option.map stringList |> Option.defaultValue []
                                                Facets = facets } : ApiOrganizationModel.SectionDirective)
                                        | _ -> None)
                                    |> Seq.toList
                                | _ -> []
                            let packageSections =
                                match tryChild "packageSections" api with
                                | Some (:? YamlSequenceNode as items) ->
                                    items.Children
                                    |> Seq.choose (function
                                        | :? YamlMappingNode as sectionInfo ->
                                            let id = tryChild "id" sectionInfo |> Option.map yamlScalar |> Option.defaultValue ""
                                            let packageName = tryChild "package" sectionInfo |> Option.map yamlScalar |> Option.defaultValue ""
                                            if System.String.IsNullOrWhiteSpace id || System.String.IsNullOrWhiteSpace packageName then None
                                            else
                                                Some ({
                                                    PackageName = packageName
                                                    Id = id
                                                    Title = tryChild "title" sectionInfo |> Option.map yamlScalar |> Option.filter (System.String.IsNullOrWhiteSpace >> not) |> Option.defaultValue id
                                                    Summary = tryChild "summary" sectionInfo |> Option.map yamlScalar |> Option.filter (System.String.IsNullOrWhiteSpace >> not) |> Option.map (Documentation.markdown >> List.singleton) |> Option.defaultValue []
                                                    Order = tryChild "order" sectionInfo |> Option.map yamlScalar |> Option.bind (fun value -> match System.Int32.TryParse value with | true, number -> Some number | _ -> None) |> Option.defaultValue 100
                                                    Entities = tryChild "entities" sectionInfo |> Option.map stringList |> Option.defaultValue []
                                                } : ApiOrganizationModel.PackageSectionDirective)
                                        | _ -> None)
                                    |> Seq.toList
                                | _ -> []
                            Some ({ FamilyId = familyId; Name = name; Sections = sections; PackageSections = packageSections } : ApiOrganizationModel.FamilyDirective)
                        | _ -> None
                    | _ -> None

            let rec updateEntity (e: EntityModel) (docs: Map<string, DocumentationNode list>) =
                let summary = docs |> Map.tryFind e.Id |> Option.defaultValue e.Summary
                { e with
                    Summary = summary
                    Entities = e.Entities |> List.map (fun child -> updateEntity child docs) }

            let docsMap =
                docFiles
                |> List.map (fun (f, raw) ->
                    let id = Path.GetFileNameWithoutExtension(f)
                    let apiOutputPath = routePrefix + $"api/{id}.html"
                    let body = parseFrontMatter raw |> Option.map snd |> Option.defaultValue raw

                    let apiDepth =
                        routePrefix.Split('/', System.StringSplitOptions.RemoveEmptyEntries).Length + 1

                    let rootPath = String.replicate apiDepth "../"

                    let expanded =
                        expandTransclusions body sourceDir package
                        |> fun value ->
                            if apiRoutes.IsEmpty then
                                resolveCrossReferences value package ""
                            else
                                resolveCrossReferencesWithRoutes value package rootPath apiRoutes

                    let rewritten =
                        rewriteLocalLinks docsRoot f apiOutputPath allowedOutputs sourceOutputPaths (outputFolderAliases allowedOutputs) expanded
                    validateLinks docsRoot f apiOutputPath allowedOutputs sourceOutputPaths (outputFolderAliases allowedOutputs) linkErrors rewritten
                    id, [ Documentation.markdown rewritten ])
                |> Map.ofList
            
            let organization =
                docFiles
                |> List.choose (fun (file, raw) -> parseDirective (Path.GetFileNameWithoutExtension file) raw)
                |> List.fold (fun current directive -> ApiOrganizationModel.applyDirective directive current) package.Organization
            for packageInfo in package.Packages do
                let sections = organization.PackageSections |> List.filter (fun sectionInfo -> sectionInfo.PackageName = packageInfo.Name)
                if not sections.IsEmpty then
                    let assigned = sections |> List.collect _.EntityIds |> Set.ofList
                    let missing = packageInfo.EntityIds |> List.filter (assigned.Contains >> not)
                    if not missing.IsEmpty then
                        let listed = String.concat ", " missing
                        invalidOp $"API package sections for {packageInfo.Name} leave entities unassigned: {listed}. Every entity must belong to an explicit section."
            { package with
                Entities = package.Entities |> List.map (fun e -> updateEntity e docsMap)
                Organization = organization }

    /// <summary>Applies long-form API documentation and appends local-link errors to a shared collection.</summary>
    let applyApiDocsWithOptionsAndLinkErrors
        (docsDir: string)
        (sourceDir: string)
        (package: PackageModel)
        (semanticCode: SemanticCode.Options)
        (linkErrors: ResizeArray<string>)
        =
        applyApiDocsCore
            (Path.Combine(docsDir, "api"))
            sourceDir
            package
            ""
            (collectAllowedOutputs docsDir package)
            Map.empty
            semanticCode
            linkErrors

    /// <summary>Applies long-form API documentation with semantic F# formatting.</summary>
    let applyApiDocsWithOptions
        (docsDir: string)
        (sourceDir: string)
        (package: PackageModel)
        (semanticCode: SemanticCode.Options)
        =
        let linkErrors = ResizeArray<string>()
        let enriched = applyApiDocsWithOptionsAndLinkErrors docsDir sourceDir package semanticCode linkErrors
        throwLinkErrors linkErrors
        enriched

    /// <summary>Applies one documentation set's long-form API pages and records local-link errors.</summary>
    let applyApiDocsForSetWithLinkErrors
        (setSourceDir: string)
        (snippetSourceDir: string)
        (package: PackageModel)
        (routePrefix: string)
        (allowedOutputs: Set<string>)
        (apiRoutes: Map<string, string>)
        (semanticCode: SemanticCode.Options)
        (linkErrors: ResizeArray<string>)
        =
        applyApiDocsCore
            (Path.Combine(setSourceDir, "api"))
            snippetSourceDir
            package
            routePrefix
            allowedOutputs
            apiRoutes
            semanticCode
            linkErrors

    /// <summary>Applies one documentation set's long-form API pages, honoring its route prefix and shared allowed outputs.</summary>
    let applyApiDocsForSet
        (setSourceDir: string)
        (snippetSourceDir: string)
        (package: PackageModel)
        (routePrefix: string)
        (allowedOutputs: Set<string>)
        (apiRoutes: Map<string, string>)
        (semanticCode: SemanticCode.Options)
        =
        let linkErrors = ResizeArray<string>()
        let enriched =
            applyApiDocsForSetWithLinkErrors setSourceDir snippetSourceDir package routePrefix allowedOutputs apiRoutes semanticCode linkErrors
        throwLinkErrors linkErrors
        enriched

    /// <summary>Applies long-form documentation from docs/api/*.md to the package model.</summary>
    /// <param name="docsDir">The docs root that contains the api subdirectory.</param>
    /// <param name="sourceDir">The source root used for snippet resolution.</param>
    /// <param name="package">The current package model to enrich.</param>
    /// <returns>A package model with API summaries replaced by markdown content where present.</returns>
    let applyApiDocs (docsDir: string) (sourceDir: string) (package: PackageModel) =
        applyApiDocsWithOptions docsDir sourceDir package SemanticCode.defaults
