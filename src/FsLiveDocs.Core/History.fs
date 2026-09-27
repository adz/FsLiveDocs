namespace FsLiveDocs.Core

open System
open System.IO
open System.Security.Cryptography
open Reified
open FsLiveDocs.Core.Schema

/// <summary>Loads and verifies immutable inputs for a multi-version documentation build.</summary>
module History =

    /// Schema 5 adds renderer-neutral API families, sections, placements, and facets.
    [<Literal>]
    let ApiModelSchemaVersion = 5

    /// API schema versions this renderer reads. Older versions are migrated; unknown versions are rejected.
    let supportedApiModelSchemaVersions = set [ 3; 4; 5 ]

    [<Literal>]
    let SemanticSchemaVersion = 2

    [<Literal>]
    let ManifestSchemaVersion = 1

    let private apiCodec = Json.compile ApiSchema.apiModelArtifact
    let private semanticCodec = Json.compile SemanticSchema.semanticDocumentationArtifact
    let private historyManifestCodec = Json.compile HistorySchema.historyManifest

    let private deserializeWith codec path = Json.deserialize codec (File.ReadAllText path)

    /// The API artifact exactly as schema 3 persisted it, kept for migration only.
    module private LegacyApi =

        /// Schema 3 packages predate `Description`. Write it explicitly as null so the strict
        /// schema-4 codec decodes the artifact without relying on absent-field defaults.
        let migrateV3 (root: System.Text.Json.Nodes.JsonObject) =
            match root["Package"] with
            | :? System.Text.Json.Nodes.JsonObject as package ->
                match package["Packages"] with
                | :? System.Text.Json.Nodes.JsonArray as packages ->
                    for info in packages do
                        match info with
                        | :? System.Text.Json.Nodes.JsonObject as fields ->
                            if not (fields.ContainsKey "Description") then fields["Description"] <- null
                        | _ -> invalidOp "API schema 3 package entry must be an object."
                | _ -> invalidOp "API schema 3 Package.Packages must be an array."
            | _ -> invalidOp "API schema 3 payload is missing Package."
            root["SchemaVersion"] <- System.Text.Json.Nodes.JsonValue.Create(4)

        /// Schema 4 predates organization. Add an explicit empty value for strict decoding;
        /// readApiArtifact then derives conservative one-release defaults from the stored graph.
        let migrateV4 (root: System.Text.Json.Nodes.JsonObject) =
            match root["Package"] with
            | :? System.Text.Json.Nodes.JsonObject as package ->
                if not (package.ContainsKey "Organization") then
                    let organization = System.Text.Json.Nodes.JsonObject()
                    organization["Families"] <- System.Text.Json.Nodes.JsonArray()
                    organization["PackageSections"] <- System.Text.Json.Nodes.JsonArray()
                    package["Organization"] <- organization
            | _ -> invalidOp "API schema 4 payload is missing Package."
            root["SchemaVersion"] <- System.Text.Json.Nodes.JsonValue.Create(5)

    /// <summary>Reads an API artifact of any supported schema version, migrating older versions to the current one.</summary>
    let readApiArtifact (json: string) : ApiModelArtifact =
        let root =
            match System.Text.Json.Nodes.JsonNode.Parse json with
            | :? System.Text.Json.Nodes.JsonObject as value -> value
            | _ -> invalidOp "API model payload must be an object."
        let declared = match root["SchemaVersion"] with | null -> 0 | value -> value.GetValue<int>()
        if not (supportedApiModelSchemaVersions.Contains declared) then
            let supported = supportedApiModelSchemaVersions |> Set.toList |> List.map string |> String.concat ", "
            invalidOp $"Unsupported API model schema {declared}; supported versions are {supported}."
        if declared = 3 then LegacyApi.migrateV3 root
        if declared <= 4 then LegacyApi.migrateV4 root
        let artifact =
            if declared < ApiModelSchemaVersion then Json.deserialize apiCodec (root.ToJsonString())
            else Json.deserialize apiCodec json
        if declared < ApiModelSchemaVersion then
            { artifact with Package = { artifact.Package with Organization = ApiOrganizationModel.derive artifact.Package.Entities } }
        else artifact

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
        let artifact = readApiArtifact (File.ReadAllText path)
        if artifact.Package.Version <> expectedVersion then
            invalidOp $"History API model version mismatch in {path}: expected {expectedVersion}, got {artifact.Package.Version}."
        artifact.Package

    /// <summary>Loads renderer-neutral semantic documentation after checksum and schema validation.</summary>
    let loadSemanticArtifact expectedSha256 path =
        if not (File.Exists(path)) then invalidOp $"History semantic artifact is missing: {path}"
        let actualSha256 = sha256 path
        if not (actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) then
            invalidOp $"History semantic artifact checksum mismatch: expected {expectedSha256}, got {actualSha256}."
        let artifact = deserializeWith semanticCodec path
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
        let manifest = deserializeWith historyManifestCodec path
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
