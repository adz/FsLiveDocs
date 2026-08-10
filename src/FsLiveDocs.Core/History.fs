namespace FsLiveDocs.Core

open System
open System.IO
open System.Security.Cryptography
open Newtonsoft.Json

/// <summary>Loads and verifies immutable inputs for a multi-version documentation build.</summary>
module History =

    [<Literal>]
    let ApiModelSchemaVersion = 1

    [<Literal>]
    let SemanticSchemaVersion = 2

    [<Literal>]
    let ManifestSchemaVersion = 1

    let private deserialize<'value> path =
        JsonConvert.DeserializeObject<'value>(File.ReadAllText(path), Serialization.jsonSettings)

    /// <summary>Computes the lowercase SHA-256 digest of a file.</summary>
    let sha256 path =
        use stream = File.OpenRead(path)
        SHA256.HashData(stream) |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

    /// <summary>Loads an API artifact after checking its checksum, schema, and declared version.</summary>
    let loadArtifact expectedVersion expectedSha256 path =
        if not (File.Exists(path)) then invalidOp $"History API model is missing: {path}"
        let actualSha256 = sha256 path
        if not (actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) then
            invalidOp $"History API model checksum mismatch for {expectedVersion}: expected {expectedSha256}, got {actualSha256}."
        let artifact = deserialize<ApiModelArtifact> path
        if isNull (box artifact) then invalidOp $"History API model is empty: {path}"
        if artifact.SchemaVersion <> ApiModelSchemaVersion then
            invalidOp $"Unsupported API model schema {artifact.SchemaVersion} in {path}; expected {ApiModelSchemaVersion}."
        if artifact.Package.Version <> expectedVersion then
            invalidOp $"History API model version mismatch in {path}: expected {expectedVersion}, got {artifact.Package.Version}."
        artifact.Package

    /// <summary>Loads renderer-neutral semantic documentation after checksum and schema validation.</summary>
    let loadSemanticArtifact expectedSha256 path =
        if not (File.Exists(path)) then invalidOp $"History semantic artifact is missing: {path}"
        let actualSha256 = sha256 path
        if not (actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) then
            invalidOp $"History semantic artifact checksum mismatch: expected {expectedSha256}, got {actualSha256}."
        let artifact = deserialize<SemanticDocumentationArtifact> path
        if isNull (box artifact) then invalidOp $"History semantic artifact is empty: {path}"
        if artifact.SchemaVersion <> SemanticSchemaVersion then
            invalidOp $"Unsupported semantic documentation schema {artifact.SchemaVersion} in {path}; expected {SemanticSchemaVersion}."
        artifact.Pages
        |> List.collect _.Blocks
        |> List.iter (fun block ->
            block.Lines
            |> List.collect _.Tokens
            |> List.iter (fun token ->
                match token.Tooltip with
                | Some index when index < 0 || index >= block.Tooltips.Length ->
                    invalidOp $"Semantic block {block.Id} contains invalid tooltip index {index}."
                | _ -> ()))
        artifact

    /// <summary>Loads a history manifest and resolves entry paths relative to the manifest.</summary>
    let loadManifest path =
        if not (File.Exists(path)) then invalidOp $"History manifest is missing: {path}"
        let manifest = deserialize<HistoryManifest> path
        if isNull (box manifest) then invalidOp $"History manifest is empty: {path}"
        if manifest.SchemaVersion <> ManifestSchemaVersion then
            invalidOp $"Unsupported history manifest schema {manifest.SchemaVersion}; expected {ManifestSchemaVersion}."
        if manifest.Entries |> List.isEmpty then invalidOp "History manifest must contain at least one entry."
        if manifest.Entries |> List.countBy (fun entry -> entry.Version) |> List.exists (fun (_, count) -> count > 1) then
            invalidOp "History manifest contains duplicate versions."
        if manifest.Entries |> List.exists (fun entry -> entry.Version = manifest.CurrentVersion) |> not then
            invalidOp $"Current history version {manifest.CurrentVersion} has no manifest entry."
        manifest.Entries
        |> List.iter (fun entry ->
            match entry.SemanticPath, entry.SemanticSha256 with
            | None, None | Some _, Some _ -> ()
            | _ -> invalidOp $"History entry {entry.Version} must declare semanticPath and semanticSha256 together.")
        let root = Path.GetDirectoryName(Path.GetFullPath(path))
        manifest,
        manifest.Entries
        |> List.map (fun entry ->
            entry,
            Path.GetFullPath(Path.Combine(root, entry.ModelPath)),
            Path.GetFullPath(Path.Combine(root, entry.DocsPath)))
