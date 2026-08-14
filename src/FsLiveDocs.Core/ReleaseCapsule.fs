namespace FsLiveDocs.Core

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
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

    let private writeEntry (archive: ZipArchive) path bytes =
        let entry = archive.CreateEntry(normalizedEntryPath path, CompressionLevel.Optimal)
        entry.LastWriteTime <- archiveTimestamp
        entry.ExternalAttributes <- 0
        use stream = entry.Open()
        stream.Write(bytes, 0, bytes.Length)

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
                    |> List.map (fun (path, bytes) -> { Path = path; Sha256 = sha256Bytes bytes; Size = int64 bytes.LongLength })
                Site = site
            }
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
            Manifest = manifest
        }

    let private readEntries path =
        use archive = ZipFile.OpenRead path
        let entries = archive.Entries |> Seq.toList
        let duplicates = entries |> List.countBy _.FullName |> List.filter (fun (_, count) -> count > 1)
        if not duplicates.IsEmpty then invalidOp $"Release capsule contains duplicate entry: {fst duplicates.Head}"
        entries
        |> List.map (fun entry ->
            let name = normalizedEntryPath entry.FullName
            use stream = entry.Open()
            use memory = new MemoryStream()
            stream.CopyTo memory
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
        if api.Package.Version <> manifest.ProductVersion then invalidOp "Release capsule product version does not match its API artifact."
        for asset in content.Assets do
            let bytes = required ("assets/" + normalizedEntryPath asset.Path) entries
            if int64 bytes.LongLength <> asset.Size || sha256Bytes bytes <> asset.Sha256 then
                invalidOp $"Release asset integrity mismatch: {asset.Path}"
        manifest, api, semantic, content, entries

    /// Inspects and fully verifies a capsule without extracting it.
    let inspect path =
        let manifest, _, _, _, _ = load path
        let fullPath = Path.GetFullPath path
        {
            Path = fullPath
            Sha256 = History.sha256 fullPath
            CompressedSize = FileInfo(fullPath).Length
            Manifest = manifest
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
        index
