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

    let resolveSnippets (body: string) (sourceDir: string) =
        // Regex for {{< snippet id="UserAuth" showOutput="true" >}}
        let pattern = @"{{<\s*snippet\s+id=""(?<id>[^""]+)""\s*(?:showOutput=""(?<showOutput>[^""]+)"")?\s*>}}"
        System.Text.RegularExpressions.Regex.Replace(body, pattern, fun (m: System.Text.RegularExpressions.Match) ->
            let id = m.Groups.["id"].Value
            // Search for <snippet:id> in sourceDir
            let files = Directory.GetFiles(sourceDir, "*.fs", SearchOption.AllDirectories)
            let snippet = 
                files 
                |> Seq.tryPick (fun f ->
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

    let loadPage (filePath: string) (sourceDir: string) =
        let raw = File.ReadAllText(filePath)
        match parseFrontMatter raw with
        | Some (metadata, body) ->
            let bodyWithSnippets = resolveSnippets body sourceDir
            let html = Markdown.ToHtml(bodyWithSnippets, pipeline)
            { Metadata = metadata; ContentHtml = html; FilePath = filePath }
        | None ->
            let html = Markdown.ToHtml(raw, pipeline)
            { Metadata = { Title = Path.GetFileNameWithoutExtension(filePath); Weight = 0; Type = None }; ContentHtml = html; FilePath = filePath }

    let scanDocs (docsDir: string) (sourceDir: string) =
        if Directory.Exists(docsDir) then
            Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            |> Array.map (fun f -> loadPage f sourceDir)
            |> Array.toList
        else
            []

