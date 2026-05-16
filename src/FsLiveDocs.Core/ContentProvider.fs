namespace FsLiveDocs.Core

open System.IO
open Markdig
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

module ContentProvider =

    let pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()
    let deserializer = DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()

    let findIndexIteration start f (arr: 'T[]) =
        let mutable found = None
        let mutable i = start
        while i < arr.Length && found.IsNone do
            if f arr.[i] then found <- Some i
            i <- i + 1
        found

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

    let findMember (id: string) (package: PackageModel) =
        let rec searchEntities (entities: EntityModel list) =
            entities |> Seq.tryPick (fun e ->
                match e.Members |> List.tryFind (fun m -> m.Id = id || m.Name = id) with
                | Some m -> Some m
                | None -> searchEntities e.Entities
            )
        searchEntities package.Entities

    let resolveSnippets (body: string) (sourceDir: string) (package: PackageModel) =
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
            | None -> $"**Snippet {id} not found**"
        )

        // 2. Resolve {{< example id="X" >}}
        let examplePattern = @"{{<\s*example\s+id=""(?<id>[^""]+)""\s*>}}"
        let body2 = System.Text.RegularExpressions.Regex.Replace(body1, examplePattern, fun (m: System.Text.RegularExpressions.Match) ->
            let id = m.Groups.["id"].Value
            // Search in all members of all entities
            let rec findExample (entities: EntityModel list) =
                entities |> Seq.tryPick (fun e ->
                    let ex = e.Members |> Seq.collect (fun m -> m.Examples) |> Seq.tryFind (fun ex -> ex.Name = id)
                    match ex with
                    | Some x -> Some x
                    | None -> findExample e.Entities
                )
            match findExample package.Entities with
            | Some ex -> $"```fsharp\n{ex.Content}\n```"
            | None -> $"**Example {id} not found**"
        )

        // 3. Resolve xref:M:Namespace.Type.Method
        let xrefPattern = @"xref:(?<type>[A-Z]):(?<id>[^\s\)]+)"
        let body3 = System.Text.RegularExpressions.Regex.Replace(body2, xrefPattern, fun (m: System.Text.RegularExpressions.Match) ->
            let id = m.Groups.["id"].Value
            match findMember id package with
            | Some mem -> $"[{mem.Name}](/api.html#{mem.Id})"
            | None -> id
        )
        body3

    let loadPage (filePath: string) (sourceDir: string) (package: PackageModel) =
        let raw = File.ReadAllText(filePath)
        match parseFrontMatter raw with
        | Some (metadata, body) ->
            let resolved = resolveSnippets body sourceDir package
            let html = Markdown.ToHtml(resolved, pipeline)
            { Metadata = metadata; ContentHtml = html; FilePath = filePath }
        | None ->
            let html = Markdown.ToHtml(raw, pipeline)
            { Metadata = { Title = Path.GetFileNameWithoutExtension(filePath); Weight = 0; Type = None }; ContentHtml = html; FilePath = filePath }

    let scanDocs (docsDir: string) (sourceDir: string) (package: PackageModel) =
        if Directory.Exists(docsDir) then
            Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            |> Array.map (fun f -> loadPage f sourceDir package)
            |> Array.toList
        else
            []
