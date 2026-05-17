namespace FsLiveDocs.Core

open System.IO
open Markdig
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

/// <summary>Provides capabilities to load, parse, and resolve Markdown documentation pages.</summary>
module ContentProvider =

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
        let rec searchEntities (entities: EntityModel list) =
            entities |> Seq.tryPick (fun e ->
                let entityExamples = if isNull (box e.Examples) then [] else e.Examples
                match entityExamples |> List.tryFind (fun ex -> ex.Name = id) with
                | Some ex -> Some ex
                | None ->
                    match e.Members |> Seq.collect (fun m -> m.Examples) |> Seq.tryFind (fun ex -> ex.Name = id) with
                    | Some ex -> Some ex
                    | None -> searchEntities e.Entities
            )
        searchEntities package.Entities

    /// <summary>Resolves shortcodes (snippets, examples) and semantic links (xrefs) in Markdown content.</summary>
    let resolveSnippets (body: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        // 1. Resolve {{< snippet id="X" >}}
        let snippetPattern = @"{{<\s*snippet\s+id=""(?<id>[^""]+)""\s*(?:showOutput=""(?<showOutput>[^""]+)"")?\s*>}}"
        let body1 = System.Text.RegularExpressions.Regex.Replace(body, snippetPattern, fun (m: System.Text.RegularExpressions.Match) ->
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
        body3

    /// <summary>Loads and processes a single Markdown page.</summary>
    let loadPage (filePath: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        let raw = File.ReadAllText(filePath)
        match parseFrontMatter raw with
        | Some (metadata, body) ->
            let resolved = resolveSnippets body sourceDir package rootPath
            let html = Markdown.ToHtml(resolved, pipeline)
            { Metadata = metadata; ContentHtml = html; FilePath = filePath }
        | None ->
            let html = Markdown.ToHtml(raw, pipeline)
            { Metadata = { Title = Path.GetFileNameWithoutExtension(filePath); Weight = 0; Type = None }; ContentHtml = html; FilePath = filePath }

    /// <summary>Scans the docs directory and loads all guide pages.</summary>
    let scanDocs (docsDir: string) (sourceDir: string) (package: PackageModel) (rootPath: string) =
        if Directory.Exists(docsDir) then
            Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            |> Array.filter (fun f -> not (f.Contains("/api/")))
            |> Array.map (fun f -> loadPage f sourceDir package rootPath)
            |> Array.toList
        else
            []
