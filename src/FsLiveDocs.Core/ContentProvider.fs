namespace FsLiveDocs.Core

open System.IO
open Markdig
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

/// <summary>Provides capabilities to load, parse, and resolve Markdown documentation pages.</summary>
/// <example name="ResolveSnippetExample">
/// > let package = { Version = "1.0"; Entities = []; Scenarios = [] };;
/// > ContentProvider.resolveSnippets "Hello" "." package "";;
/// val it: string = "Hello"
/// </example>
module ContentProvider =

    let private siteOutputRoot = Path.GetFullPath("output")

    /// <summary>The shared Markdig pipeline with advanced extensions enabled.</summary>
    let pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()
    
    /// <summary>The YAML deserializer for frontmatter processing.</summary>
    let deserializer = DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()

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
                Some (metadata, body)
            | None -> None
        else
            None

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
             || cleaned.StartsWith("tel:", System.StringComparison.OrdinalIgnoreCase) then None
        else
            let currentDir = Path.GetDirectoryName(currentOutputPath)
            let candidate =
                if cleaned.StartsWith("/") then cleaned.TrimStart('/')
                elif System.String.IsNullOrWhiteSpace currentDir then cleaned
                else Path.Combine(currentDir, cleaned)

            let full = Path.GetFullPath(Path.Combine(siteOutputRoot, candidate))
            let relative = Path.GetRelativePath(siteOutputRoot, full).Replace('\\', '/')
            Some relative

    let private validateLinks (currentOutputPath: string) (allowedOutputs: Set<string>) (body: string) =
        let protectedSegments = ResizeArray<string>()
        let protectCodeSegments (text: string) =
            let codePattern = @"(?s)```.*?```|`[^`\r\n]+`"
            System.Text.RegularExpressions.Regex.Replace(text, codePattern, fun (m: System.Text.RegularExpressions.Match) ->
                let token = $"@@FSLIVEDOCS_VALIDATE_CODE_{protectedSegments.Count}@@"
                protectedSegments.Add(m.Value)
                token)

        let restoreCodeSegments (text: string) =
            protectedSegments
            |> Seq.mapi (fun i (segment: string) -> $"@@FSLIVEDOCS_VALIDATE_CODE_{i}@@", segment)
            |> Seq.fold (fun (acc: string) (token, segment) -> acc.Replace(token, segment)) text

        let body = protectCodeSegments body
        let linkPattern = @"(?<!\!)\[[^\]]+\]\((?<href>[^)]+)\)"
        for m in System.Text.RegularExpressions.Regex.Matches(body, linkPattern) do
            let href = m.Groups.["href"].Value.Trim().Trim('"')
            match normalizeOutputPath currentOutputPath href with
            | None -> ()
            | Some target ->
                if target.EndsWith(".html", System.StringComparison.OrdinalIgnoreCase)
                   || target.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase) then
                    let normalizedTarget =
                        if target.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase) then
                            Path.ChangeExtension(target, ".html").Replace('\\', '/')
                        else target
                    if not (allowedOutputs.Contains normalizedTarget) then
                        invalidOp $"Broken documentation link in {currentOutputPath}: [{href}] resolves to {normalizedTarget}, which does not exist."
        ignore (restoreCodeSegments body)

    let private collectEntityIds (entities: EntityModel list) =
        let rec walk acc (items: EntityModel list) =
            match items with
            | [] -> acc
            | e :: rest ->
                walk (e.Id :: acc) (e.Entities @ rest)
        walk [] entities

    let private collectGuideOutputs (docsDir: string) =
        if Directory.Exists(docsDir) then
            Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            |> Array.filter (fun f -> not (f.Contains($"{Path.DirectorySeparatorChar}api{Path.DirectorySeparatorChar}")))
            |> Array.map (fun f -> Path.GetFileNameWithoutExtension(f).ToLowerInvariant() + ".html")
            |> Array.toList
        else
            []

    let private collectAllowedOutputs (docsDir: string) (package: PackageModel) =
        let guideOutputs = collectGuideOutputs docsDir
        let apiOutputs = collectEntityIds package.Entities |> List.map (fun id -> $"api/{id}.html")
        Set.ofList (guideOutputs @ apiOutputs @ [ "index.html"; "api.html" ])

    /// <summary>Resolves shortcodes (snippets, examples) and semantic links (xrefs) in Markdown content.</summary>
    let resolveSnippets (body: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        let protectedSegments = ResizeArray<string>()
        let protectCodeSegments (text: string) =
            let codePattern = @"(?s)```.*?```|`[^`\r\n]+`"
            System.Text.RegularExpressions.Regex.Replace(text, codePattern, fun (m: System.Text.RegularExpressions.Match) ->
                let token = $"@@FSLIVEDOCS_CODE_{protectedSegments.Count}@@"
                protectedSegments.Add(m.Value)
                token)

        let restoreCodeSegments (text: string) =
            protectedSegments
            |> Seq.mapi (fun i (segment: string) -> $"@@FSLIVEDOCS_CODE_{i}@@", segment)
            |> Seq.fold (fun (acc: string) (token, segment) -> acc.Replace(token, segment)) text

        let protectedBody = protectCodeSegments body

        // 1. Resolve {{< snippet id="X" >}}
        let snippetPattern = @"{{<\s*snippet\s+id=""(?<id>[^""]+)""\s*(?:showOutput=""(?<showOutput>[^""]+)"")?\s*>}}"
        let body1 = System.Text.RegularExpressions.Regex.Replace(protectedBody, snippetPattern, fun (m: System.Text.RegularExpressions.Match) ->
            let id = m.Groups.["id"].Value
            let files = Directory.GetFiles(sourceDir, "*.fs", SearchOption.AllDirectories)
            let snippet = 
                files |> Seq.tryPick (fun f ->
                    let lines = File.ReadAllLines(f)
                    let start = Array.tryFindIndex (fun (l: string) -> l.Contains($"<snippet:{id}>")) lines
                    let stop = Array.tryFindIndex (fun (l: string) -> l.Contains($"</snippet:{id}>")) lines
                    match start, stop with
                    | Some s, Some e -> Some (String.concat "\n" lines.[s+1..e-1])
                    | _ -> None
                )
            match snippet with
            | Some s -> $"```fsharp\n{s}\n```"
            | None -> invalidOp $"Snippet '{id}' was not found."
        )

        // 2. Resolve {{< example id="X" >}}
        let examplePattern = @"{{<\s*example\s+id=""(?<id>[^""]+)""\s*>}}"
        let body2 = System.Text.RegularExpressions.Regex.Replace(body1, examplePattern, fun (m: System.Text.RegularExpressions.Match) ->
            let id = m.Groups.["id"].Value
            match findExample id package with
            | Some ex -> $"```fsharp\n{ex.Content}\n```"
            | None -> invalidOp $"Example '{id}' was not found."
        )

        // <snippet:XrefResolution>
        // 3. Resolve xref: with relative rootPath
        let xrefPattern = @"xref:(?<type>[A-Z]):(?<id>[^\s\)]+)"
        let body3 = System.Text.RegularExpressions.Regex.Replace(body2, xrefPattern, fun (m: System.Text.RegularExpressions.Match) ->
            let id = m.Groups.["id"].Value
            match findMember id package, findEntity id package with
            | Some mem, _ -> 
                // We point to the module/type page, not individual member pages yet
                // Need to find the parent entity ID
                let rec findParentId (entities: EntityModel list) parentId =
                    entities |> Seq.tryPick (fun e ->
                        if e.Members |> List.exists (fun m -> m.Id = id || m.Name = id) then
                            Some e.Id
                        else
                            findParentId e.Entities (Some e.Id)
                    )
                let targetPage = defaultArg (findParentId package.Entities None) "api"
                $"[{mem.Name}]({rootPath}api/{targetPage}.html#{mem.Id})"
            | None, Some ent -> $"[{ent.Name}]({rootPath}api/{ent.Id}.html)"
            | None, None -> invalidOp $"Cross-reference '{id}' was not found."
        )
        // </snippet:XrefResolution>
        restoreCodeSegments body3

    /// <summary>Loads and processes a single Markdown page.</summary>
    /// <param name="filePath">The markdown file to read.</param>
    /// <param name="sourceDir">The root directory used to resolve snippet shortcodes.</param>
    /// <param name="package">The extracted package model used for examples and xrefs.</param>
    /// <param name="rootPath">The relative root path used when generating links.</param>
    /// <param name="currentOutputPath">The output HTML path used for link validation.</param>
    /// <param name="allowedOutputs">The set of known output pages used to validate local links.</param>
    /// <returns>A processed content page ready for rendering.</returns>
    let loadPage (filePath: string) (sourceDir: string) (package: PackageModel) (rootPath: string) (currentOutputPath: string) (allowedOutputs: Set<string>) =
        let raw = File.ReadAllText(filePath)
        match parseFrontMatter raw with
        | Some (metadata, body) ->
            let resolved = resolveSnippets body sourceDir package rootPath
            validateLinks currentOutputPath allowedOutputs resolved
            let html = Markdown.ToHtml(resolved, pipeline)
            { Metadata = metadata; ContentHtml = html; FilePath = filePath }
        | None ->
            let resolved = resolveSnippets raw sourceDir package rootPath
            validateLinks currentOutputPath allowedOutputs resolved
            let html = Markdown.ToHtml(resolved, pipeline)
            { Metadata = { Title = Path.GetFileNameWithoutExtension(filePath); Weight = 0; Type = None }; ContentHtml = html; FilePath = filePath }

    /// <summary>Scans the docs directory and loads all guide pages.</summary>
    /// <param name="docsDir">The docs root containing markdown pages.</param>
    /// <param name="sourceDir">The source root used for snippet resolution.</param>
    /// <param name="package">The extracted package model used for xrefs and examples.</param>
    /// <param name="rootPath">The relative root path used when rendering links.</param>
    /// <returns>All guide pages found under the docs directory.</returns>
    let scanDocs (docsDir: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        if Directory.Exists(docsDir) then
            let allowedOutputs = collectAllowedOutputs docsDir package
            Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            |> Array.filter (fun f -> not (f.Contains("/api/")))
            |> Array.map (fun f ->
                let outputPath = Path.GetFileNameWithoutExtension(f).ToLowerInvariant() + ".html"
                loadPage f sourceDir package rootPath outputPath allowedOutputs)
            |> Array.toList
        else
            []

    /// <summary>Applies long-form documentation from docs/api/*.md to the package model.</summary>
    /// <param name="docsDir">The docs root that contains the api subdirectory.</param>
    /// <param name="sourceDir">The source root used for snippet resolution.</param>
    /// <param name="package">The current package model to enrich.</param>
    /// <returns>A package model with API summaries replaced by markdown content where present.</returns>
    let applyApiDocs (docsDir: string) (sourceDir: string) (package: PackageModel) =
        let apiDocsDir = Path.Combine(docsDir, "api")
        if not (Directory.Exists(apiDocsDir)) then package
        else
            let allowedOutputs = collectAllowedOutputs docsDir package
            let rec updateEntity (e: EntityModel) (docs: Map<string, string>) =
                let summary = docs |> Map.tryFind e.Id |> Option.defaultValue e.SummaryHtml
                { e with 
                    SummaryHtml = summary
                    Entities = e.Entities |> List.map (fun child -> updateEntity child docs) }

            let docFiles = Directory.GetFiles(apiDocsDir, "*.md")
            let docsMap = 
                docFiles 
                |> Array.map (fun f -> 
                    let id = Path.GetFileNameWithoutExtension(f)
                    let page = loadPage f sourceDir package "" $"api/{id}.html" allowedOutputs
                    id, page.ContentHtml)
                |> Map.ofArray
            
            { package with Entities = package.Entities |> List.map (fun e -> updateEntity e docsMap) }
