namespace FsLiveDocs.Core

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Net.Http
open Newtonsoft.Json

/// Creates, validates, inspects, and extracts deterministic release capsules.
module ReleaseCapsule =

    [<Literal>]
    let ManifestSchemaVersion = 1

    [<Literal>]
    let ContentSchemaVersion = 1

    [<Literal>]
    let HistoryIndexSchemaVersion = 1

    let private archiveTimestamp = DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)
    let private maximumEntryCount = 10_000
    let private maximumEntrySize = 64L * 1024L * 1024L
    let private maximumTotalSize = 256L * 1024L * 1024L

    let private sha256Bytes (bytes: byte array) =
        bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

    let private serialize value =
        JsonConvert.SerializeObject(value, Formatting.Indented, Serialization.jsonSettings)
        |> Encoding.UTF8.GetBytes

    let private deserialize<'value> (bytes: byte array) =
        JsonConvert.DeserializeObject<'value>(Encoding.UTF8.GetString bytes, Serialization.jsonSettings)

    let private normalizedEntryPath (path: string) =
        let normalized = path.Replace('\\', '/').TrimStart('/')
        if String.IsNullOrWhiteSpace normalized
           || Path.IsPathRooted path
           || normalized.Split('/') |> Array.exists (fun segment -> segment = ".." || segment = ".") then
            invalidOp $"Unsafe release capsule path: {path}"
        normalized

    let private createComponent schemaVersion path (bytes: byte array) : ReleaseComponent =
        {
            SchemaVersion = schemaVersion
            Path = path
            Sha256 = sha256Bytes bytes
            Size = int64 bytes.LongLength
        }

    let private mediaType (path: string) =
        match Path.GetExtension(path).ToLowerInvariant() with
        | ".css" -> "text/css"
        | ".js" -> "text/javascript"
        | ".json" -> "application/json"
        | ".svg" -> "image/svg+xml"
        | ".png" -> "image/png"
        | ".jpg" | ".jpeg" -> "image/jpeg"
        | ".gif" -> "image/gif"
        | ".webp" -> "image/webp"
        | ".ico" -> "image/x-icon"
        | ".woff" -> "font/woff"
        | ".woff2" -> "font/woff2"
        | ".txt" -> "text/plain"
        | _ -> "application/octet-stream"

    let private writeEntry (archive: ZipArchive) path bytes =
        let entry = archive.CreateEntry(normalizedEntryPath path, CompressionLevel.Optimal)
        entry.LastWriteTime <- archiveTimestamp
        entry.ExternalAttributes <- 0
        use stream = entry.Open()
        stream.Write(bytes, 0, bytes.Length)

    let private captureCounts (api: ApiModelArtifact) (semantic: SemanticDocumentationArtifact) (content: ReleaseContentArtifact) =
        let rec countEntities (entities: EntityModel list) : int * int * int =
            entities
            |> List.fold (fun (entityCount, memberCount, exampleCount) (entity: EntityModel) ->
                let nestedEntities, nestedMembers, nestedExamples = countEntities entity.Entities
                entityCount + nestedEntities + 1,
                memberCount + nestedMembers + entity.Members.Length,
                exampleCount + nestedExamples + entity.Examples.Length + (entity.Members |> List.sumBy (fun member' -> member'.Examples.Length))) (0, 0, 0)
        let entities, members, examples = countEntities api.Package.Entities
        let rec countNodes nodes = nodes |> List.sumBy (fun node -> 1 + countNodes node.Children)
        let documentationNodes =
            let rec inEntities entities =
                entities
                |> List.sumBy (fun entity ->
                    countNodes entity.Summary
                    + (entity.Members |> List.sumBy (fun member' -> countNodes member'.Summary + countNodes member'.Remarks))
                    + inEntities entity.Entities)
            inEntities api.Package.Entities
        let blocks = semantic.Pages |> List.collect _.Blocks
        {
            Entities = entities
            Members = members
            Examples = examples
            DocumentationNodes = documentationNodes
            Pages = content.Pages.Length
            CodeBlocks = blocks.Length
            Tooltips = blocks |> List.sumBy _.Tooltips.Length
            Diagnostics = blocks |> List.sumBy _.Diagnostics.Length
            Assets = content.Assets.Length
        }

    let private validateApi (api: ApiModelArtifact) =
        if String.IsNullOrWhiteSpace api.Package.Version then invalidOp "Release API artifact has no product version."
        let rec collectIds (entities: EntityModel list) =
            entities
            |> List.collect (fun (entity: EntityModel) -> entity.Id :: ((entity.Members |> List.map _.Id) @ collectIds entity.Entities))
        let ids = collectIds api.Package.Entities
        match ids |> List.tryFind String.IsNullOrWhiteSpace with
        | Some _ -> invalidOp "Release API artifact contains an empty symbol ID."
        | None -> ()
        match ids |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) with
        | Some (id, _) -> invalidOp $"Release API artifact contains duplicate symbol ID {id}."
        | None -> ()

    let private validateSemantic (semantic: SemanticDocumentationArtifact) =
        match semantic.Pages |> List.countBy _.SourcePath |> List.tryFind (fun (_, count) -> count > 1) with
        | Some (path, _) -> invalidOp $"Release semantic artifact contains duplicate page {path}."
        | None -> ()
        let blocks = semantic.Pages |> List.collect _.Blocks
        match blocks |> List.countBy _.Id |> List.tryFind (fun (_, count) -> count > 1) with
        | Some (id, _) -> invalidOp $"Release semantic artifact contains duplicate block ID {id}."
        | None -> ()
        for block in blocks do
            if String.IsNullOrWhiteSpace block.Id || String.IsNullOrWhiteSpace block.SourceHash || String.IsNullOrWhiteSpace block.ContextHash then
                invalidOp "Release semantic artifact contains a block without an ID, source hash, or context hash."
            for token in block.Lines |> List.collect _.Tokens do
                match token.Tooltip with
                | Some index when index < 0 || index >= block.Tooltips.Length ->
                    invalidOp $"Release semantic block {block.Id} contains invalid tooltip index {index}."
                | _ -> ()

    let private validateContent (content: ReleaseContentArtifact) =
        match content.Pages |> List.countBy _.SourcePath |> List.tryFind (fun (_, count) -> count > 1) with
        | Some (path, _) -> invalidOp $"Release content artifact contains duplicate page {path}."
        | None -> ()
        content.Pages |> List.iter (fun page -> normalizedEntryPath page.SourcePath |> ignore)
        match content.Assets |> List.countBy _.Path |> List.tryFind (fun (_, count) -> count > 1) with
        | Some (path, _) -> invalidOp $"Release content artifact contains duplicate asset {path}."
        | None -> ()
        for asset in content.Assets do
            normalizedEntryPath asset.Path |> ignore
            if String.IsNullOrWhiteSpace asset.MediaType then invalidOp $"Release asset {asset.Path} has no media type."

    /// Creates a complete capsule without overwriting an existing release.
    let create path sourceRevision captureToolVersion (api: ApiModelArtifact) (semantic: SemanticDocumentationArtifact) site pages assets =
        let fullPath = Path.GetFullPath path
        if File.Exists fullPath then invalidOp $"Release capsule already exists: {fullPath}"
        let directory = Path.GetDirectoryName fullPath
        if not (String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory directory |> ignore

        let normalizedAssets =
            assets
            |> List.map (fun (path, bytes: byte array) -> normalizedEntryPath path, bytes)
            |> List.sortBy fst
        let duplicates = normalizedAssets |> List.countBy fst |> List.filter (fun (_, count) -> count > 1)
        if not duplicates.IsEmpty then invalidOp $"Release content contains duplicate asset path: {fst duplicates.Head}"

        let apiBytes = serialize api
        let semanticBytes = serialize semantic
        let content : ReleaseContentArtifact =
            {
                SchemaVersion = ContentSchemaVersion
                Pages = pages |> List.sortBy _.SourcePath
                Assets =
                    normalizedAssets
                    |> List.map (fun (path, bytes) -> { Path = path; MediaType = mediaType path; Sha256 = sha256Bytes bytes; Size = int64 bytes.LongLength })
                Site = site
            }
        validateApi api
        validateSemantic semantic
        validateContent content
        let contentBytes = serialize content
        let manifest =
            {
                SchemaVersion = ManifestSchemaVersion
                ProductVersion = api.Package.Version
                SourceRevision = sourceRevision
                CaptureToolVersion = captureToolVersion
                Api = createComponent api.SchemaVersion "api.json" apiBytes
                Semantic = createComponent semantic.SchemaVersion "semantic.json" semanticBytes
                Content = createComponent content.SchemaVersion "content.json" contentBytes
            }
        let manifestBytes = serialize manifest

        use file = File.Create fullPath
        use archive = new ZipArchive(file, ZipArchiveMode.Create)
        [ "api.json", apiBytes
          "content.json", contentBytes
          "manifest.json", manifestBytes
          "semantic.json", semanticBytes ]
        |> List.iter (fun (entryPath, bytes) -> writeEntry archive entryPath bytes)
        normalizedAssets
        |> List.iter (fun (assetPath, bytes) -> writeEntry archive ("assets/" + assetPath) bytes)
        archive.Dispose()
        file.Dispose()

        {
            Path = fullPath
            Sha256 = History.sha256 fullPath
            CompressedSize = FileInfo(fullPath).Length
            UncompressedSize = int64 manifestBytes.LongLength + manifest.Api.Size + manifest.Semantic.Size + manifest.Content.Size + (content.Assets |> List.sumBy _.Size)
            Manifest = manifest
            Counts = captureCounts api semantic content
        }

    let private readEntries path =
        use archive = ZipFile.OpenRead path
        let entries = archive.Entries |> Seq.toList
        if entries.Length > maximumEntryCount then
            invalidOp $"Release capsule has {entries.Length} entries; the limit is {maximumEntryCount}."
        let duplicates = entries |> List.countBy _.FullName |> List.filter (fun (_, count) -> count > 1)
        if not duplicates.IsEmpty then invalidOp $"Release capsule contains duplicate entry: {fst duplicates.Head}"
        let mutable totalSize = 0L
        entries
        |> List.map (fun entry ->
            let name = normalizedEntryPath entry.FullName
            if name.EndsWith("/", StringComparison.Ordinal) then invalidOp $"Release capsule contains a directory entry: {name}"
            let unixFileType = (entry.ExternalAttributes >>> 16) &&& 0xF000
            if unixFileType = 0xA000 then invalidOp $"Release capsule contains a symbolic link: {name}"
            if entry.Length < 0L || entry.Length > maximumEntrySize then
                invalidOp $"Release capsule entry {name} is larger than the {maximumEntrySize} byte limit."
            totalSize <- totalSize + entry.Length
            if totalSize > maximumTotalSize then
                invalidOp $"Release capsule expands beyond the {maximumTotalSize} byte limit."
            use stream = entry.Open()
            use memory = new MemoryStream(int entry.Length)
            stream.CopyTo memory
            if memory.Length <> entry.Length then invalidOp $"Release capsule entry size changed while reading: {name}"
            name, memory.ToArray())
        |> Map.ofList

    let private required (name: string) (entries: Map<string, byte array>) =
        entries |> Map.tryFind name |> Option.defaultWith (fun () -> invalidOp $"Release capsule is missing {name}.")

    let private verifyComponent (releaseComponent: ReleaseComponent) entries =
        let bytes = required releaseComponent.Path entries
        if int64 bytes.LongLength <> releaseComponent.Size then invalidOp $"Release component size mismatch: {releaseComponent.Path}"
        let actual = sha256Bytes bytes
        if not (actual.Equals(releaseComponent.Sha256, StringComparison.OrdinalIgnoreCase)) then
            invalidOp $"Release component checksum mismatch: {releaseComponent.Path}"
        bytes

    /// Verifies a capsule and returns its manifest and component models.
    let load path =
        let fullPath = Path.GetFullPath path
        if not (File.Exists fullPath) then invalidOp $"Release capsule is missing: {fullPath}"
        let entries = readEntries fullPath
        let manifest = required "manifest.json" entries |> deserialize<ReleaseCapsuleManifest>
        if isNull (box manifest) || manifest.SchemaVersion <> ManifestSchemaVersion then
            let actual = if isNull (box manifest) then 0 else manifest.SchemaVersion
            invalidOp $"Unsupported release capsule manifest schema {actual}; expected {ManifestSchemaVersion}."
        let api = verifyComponent manifest.Api entries |> deserialize<ApiModelArtifact>
        let semantic = verifyComponent manifest.Semantic entries |> deserialize<SemanticDocumentationArtifact>
        let content = verifyComponent manifest.Content entries |> deserialize<ReleaseContentArtifact>
        if api.SchemaVersion <> History.ApiModelSchemaVersion then invalidOp $"Unsupported API model schema {api.SchemaVersion}; expected {History.ApiModelSchemaVersion}."
        if semantic.SchemaVersion <> History.SemanticSchemaVersion then invalidOp $"Unsupported semantic schema {semantic.SchemaVersion}; expected {History.SemanticSchemaVersion}."
        if content.SchemaVersion <> ContentSchemaVersion then invalidOp $"Unsupported content schema {content.SchemaVersion}; expected {ContentSchemaVersion}."
        validateApi api
        validateSemantic semantic
        validateContent content
        if api.Package.Version <> manifest.ProductVersion then invalidOp "Release capsule product version does not match its API artifact."
        for asset in content.Assets do
            let bytes = required ("assets/" + normalizedEntryPath asset.Path) entries
            if int64 bytes.LongLength <> asset.Size || sha256Bytes bytes <> asset.Sha256 then
                invalidOp $"Release asset integrity mismatch: {asset.Path}"
        let expectedEntries =
            [ "manifest.json"; manifest.Api.Path; manifest.Semantic.Path; manifest.Content.Path ]
            @ (content.Assets |> List.map (fun asset -> "assets/" + normalizedEntryPath asset.Path))
            |> Set.ofList
        let unexpected = entries |> Map.toSeq |> Seq.map fst |> Seq.filter (expectedEntries.Contains >> not) |> Seq.tryHead
        match unexpected with
        | Some name -> invalidOp $"Release capsule contains undeclared entry: {name}"
        | None -> ()
        manifest, api, semantic, content, entries

    /// Inspects and fully verifies a capsule without extracting it.
    let inspect path =
        let manifest, api, semantic, content, entries = load path
        let fullPath = Path.GetFullPath path
        {
            Path = fullPath
            Sha256 = History.sha256 fullPath
            CompressedSize = FileInfo(fullPath).Length
            UncompressedSize = entries |> Map.toSeq |> Seq.sumBy (fun (_, bytes) -> int64 bytes.LongLength)
            Manifest = manifest
            Counts = captureCounts api semantic content
        }

    /// Materializes renderer-neutral content under a validated destination.
    let materializeContent path destination =
        let _, api, semantic, content, entries = load path
        let root = Path.GetFullPath destination
        Directory.CreateDirectory root |> ignore
        for page in content.Pages do
            let relative = normalizedEntryPath page.SourcePath
            if not (relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) then invalidOp $"Release page is not Markdown: {relative}"
            let output = Path.GetFullPath(Path.Combine(root, relative))
            if not (output.StartsWith(root + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then invalidOp $"Unsafe release page path: {relative}"
            Directory.CreateDirectory(Path.GetDirectoryName output) |> ignore
            let frontMatter = JsonConvert.SerializeObject(page.Metadata, Formatting.Indented, Serialization.jsonSettings)
            File.WriteAllText(output, "---\n" + frontMatter + "\n---\n" + page.Markdown)
        for asset in content.Assets do
            let relative = normalizedEntryPath asset.Path
            let output = Path.GetFullPath(Path.Combine(root, relative))
            if not (output.StartsWith(root + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then invalidOp $"Unsafe release asset path: {relative}"
            Directory.CreateDirectory(Path.GetDirectoryName output) |> ignore
            File.WriteAllBytes(output, required ("assets/" + relative) entries)
        api.Package, semantic, content.Site

    /// Loads a capsule history index and validates its structural invariants.
    let loadHistoryIndex path =
        if not (File.Exists path) then invalidOp $"Release history index is missing: {path}"
        let index = JsonConvert.DeserializeObject<ReleaseHistoryIndex>(File.ReadAllText path, Serialization.jsonSettings)
        if isNull (box index) || index.SchemaVersion <> HistoryIndexSchemaVersion then
            let actual = if isNull (box index) then 0 else index.SchemaVersion
            invalidOp $"Unsupported release history index schema {actual}; expected {HistoryIndexSchemaVersion}."
        if index.Entries.IsEmpty then invalidOp "Release history index must contain at least one entry."
        if index.Entries |> List.countBy _.Version |> List.exists (fun (_, count) -> count > 1) then
            invalidOp "Release history index contains duplicate versions."
        if index.Entries |> List.exists (fun entry -> entry.Version = index.CurrentVersion) |> not then
            invalidOp $"Current history version {index.CurrentVersion} has no capsule entry."
        for entry in index.Entries do
            match entry.CapsulePath, entry.CapsuleUrl with
            | Some path, None when not (String.IsNullOrWhiteSpace path) -> ()
            | None, Some url when not (String.IsNullOrWhiteSpace url) -> ()
            | _ -> invalidOp $"Release {entry.Version} must declare exactly one of CapsulePath or CapsuleUrl."
            if entry.CapsuleSha256.Length <> 64
               || entry.CapsuleSha256 |> Seq.exists (fun value -> not (Uri.IsHexDigit value)) then
                invalidOp $"Release {entry.Version} has an invalid SHA-256 checksum."
        index

    /// Resolves a local or remote capsule into the checksum-addressed download cache.
    let acquire indexRoot cacheRoot (entry: ReleaseHistoryEntry) =
        let verify path =
            let actual = History.sha256 path
            if not (actual.Equals(entry.CapsuleSha256, StringComparison.OrdinalIgnoreCase)) then
                invalidOp $"Release capsule checksum mismatch for {entry.Version}: expected {entry.CapsuleSha256}, got {actual}."
            path

        match entry.CapsulePath, entry.CapsuleUrl with
        | Some relative, None -> Path.GetFullPath(Path.Combine(indexRoot, relative)) |> verify
        | None, Some source ->
            let uri =
                match Uri.TryCreate(source, UriKind.Absolute) with
                | true, value when value.Scheme = Uri.UriSchemeHttps -> value
                | _ -> invalidOp $"Release {entry.Version} capsule URL must use HTTPS."
            Directory.CreateDirectory cacheRoot |> ignore
            let cached = Path.Combine(cacheRoot, entry.CapsuleSha256.ToLowerInvariant() + ".livedocs.zip")
            if File.Exists cached then verify cached
            else
                let temporary = cached + ".download-" + Guid.NewGuid().ToString("N")
                try
                    use client = new HttpClient()
                    use response = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                    response.EnsureSuccessStatusCode() |> ignore
                    use sourceStream = response.Content.ReadAsStream()
                    use destination = File.Create temporary
                    sourceStream.CopyTo destination
                    destination.Dispose()
                    verify temporary |> ignore
                    File.Move(temporary, cached)
                    cached
                finally
                    if File.Exists temporary then File.Delete temporary
        | _ -> invalidOp $"Release {entry.Version} must declare exactly one capsule source."
