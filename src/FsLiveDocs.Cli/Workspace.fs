namespace FsLiveDocs.Cli

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Axial
open Axial.FileSystem
open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects
open FsLiveDocs.Core.Schema

/// Repository-local release publication settings, read from the top-level `history`
/// object in `.livedocs/config.json`. These configure how CI locates and names
/// release capsules; they are never persisted in a capsule or the history index.
type internal HistoryConfig = {
    /// Format string for a capsule download URL. Placeholders: {version}, {name}, {tag}.
    UrlPattern: string option
    /// Shell command that lists published capsules as `version url sha256` lines.
    Discover: string option
}

/// Owns repository-local FsLiveDocs configuration, project selection, and initial scaffolding.
module internal Workspace =

    let emptyHistoryConfig = { UrlPattern = None; Discover = None }

    /// `.livedocs/config.json` is a free-form document: `projects`, `docsSets`, and `history` are
    /// read here, but every other top-level field belongs to `SiteConfig` (read via
    /// `loadSiteConfig`, which tolerates and ignores these three keys), and the file may carry
    /// other keys again in the future. That is why config reading uses `System.Text.Json.Nodes`
    /// -- a dynamic document -- rather than a Reified schema for the whole file, while individual
    /// well-typed sections (`SiteConfig`, `DocsSetConfig`) still decode through their own schema.
    let private tryProperty (node: JsonNode) (name: string) : JsonNode option =
        match node with
        | null -> None
        | :? JsonObject as obj ->
            let mutable value = Unchecked.defaultof<JsonNode>
            if obj.TryGetPropertyValue(name, &value) then Option.ofObj value else None
        | _ -> None

    let private tryString (node: JsonNode) (name: string) : string option =
        tryProperty node name
        |> Option.bind (fun value -> try Some(value.GetValue<string>()) with _ -> None)
        |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let private stringArray (node: JsonNode) (name: string) : string list =
        match tryProperty node name with
        | Some(:? JsonArray as items) ->
            items |> Seq.choose (fun item -> try Some(item.GetValue<string>()) with _ -> None) |> Seq.toList
        | _ -> []

    let internal docsSetConfigCodec =
        Json.compile (
            schema<DocsSetConfig> {
                fieldAs "id" (fun (c: DocsSetConfig) -> c.Id) { withSchema Schema.text }
                fieldAs "title" (fun (c: DocsSetConfig) -> c.Title) { withSchema (Schema.option Schema.text) }
                fieldAs "source" (fun (c: DocsSetConfig) -> c.Source) { withSchema (Schema.option Schema.text) }
                fieldAs "path" (fun (c: DocsSetConfig) -> c.Path) { withSchema (Schema.option Schema.text) }
                fieldAs "projects" (fun (c: DocsSetConfig) -> c.Projects) { withSchema (Schema.listWith Schema.text |> Schema.withDefault []) }
                fieldAs "default" (fun (c: DocsSetConfig) -> c.Default) { withSchema (Schema.option Schema.bool) }
                fieldAs "sidebar" (fun (c: DocsSetConfig) -> c.Sidebar) { withSchema (Schema.option Schema.bool) }
                fieldAs "api" (fun (c: DocsSetConfig) -> c.Api) { withSchema (Schema.option Schema.bool) }
                fieldAs "fSharpPrelude" (fun (c: DocsSetConfig) -> c.FSharpPrelude) { withSchema (Schema.option Schema.text) }
                construct (fun id title source path projects default_ sidebar api fsharpPrelude ->
                    { Id = id
                      Title = title
                      Source = source
                      Path = path
                      Projects = projects
                      Default = default_
                      Sidebar = sidebar
                      Api = api
                      FSharpPrelude = fsharpPrelude })
            }
        )

    let internal siteConfigCodec = Json.compile SiteSchema.siteConfigFile

    let private defaultSiteConfig =
        { RepoUrl = None
          SiteName = None
          LogoText = None
          LogoPath = None
          LogoDarkPath = None
          ShowSiteName = None
          Stylesheet = None
          Themes = None
          Navigation = None
          FSharpPrelude = None
          CommentsProvider = None }

    /// Reads a config-shaped file's content if it exists, as one Flow. Every function below that
    /// starts from ".livedocs/config.json" (or another config-shaped file) composes this once
    /// instead of issuing separate exists/read calls.
    let private readIfExists path =
        flow {
            let! exists = FileSystem.fileExists path
            if exists then
                let! text = FileSystem.readAllText path
                return Some text
            else
                return None
        }

    /// Finds documentable projects when callers omit the project list.
    let discoverProjects () =
        let work =
            flow {
                let! root = FileSystem.getCurrentDirectory
                let ignored =
                    set [ ".git"; ".livedocs"; "artifacts"; "bin"; "node_modules"; "obj"; "output"; "packages"; "TestResults"; "tests" ]
                let isIgnored (path: string) =
                    Path.GetRelativePath(root, path).Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
                    |> Array.exists ignored.Contains

                let! files = FileSystem.getFiles root "*.fsproj" SearchOption.AllDirectories

                return
                    files
                    |> Array.filter (isIgnored >> not)
                    |> Array.sort
                    |> Array.map (fun path -> Path.GetRelativePath(root, path).Replace('\\', '/'))
                    |> Array.toList
            }
        Run.orRaise FileSystemError.describe "Could not discover projects" work

    let private configuredProjects () =
        let configPath = Path.Combine(".livedocs", "config.json")
        match Run.orRaise FileSystemError.describe $"Could not read {configPath}" (readIfExists configPath) with
        | None -> []
        | Some text ->
            let config = JsonNode.Parse text

            let setProjects =
                match tryProperty config "docsSets" with
                | Some(:? JsonArray as sets) -> sets |> Seq.collect (fun set -> stringArray set "projects")
                | _ -> Seq.empty

            Seq.append (stringArray config "projects") setProjects
            |> Seq.filter (String.IsNullOrWhiteSpace >> not)
            |> Seq.distinct
            |> Seq.toList

    /// Resolves explicit, configured, or discovered projects in that order.
    let resolveProjects reportDiscovery command projectPaths =
        match projectPaths with
        | _ :: _ -> projectPaths
        | [] ->
            match configuredProjects () with
            | _ :: _ as projects -> projects
            | [] ->
                match discoverProjects () with
                | [] -> invalidOp $"{command} requires at least one project, and no .fsproj files were discovered. Pass project paths explicitly."
                | projects ->
                    reportDiscovery projects.Length
                    projects

    /// Discovers projects and persists the selection. Returns the count and config path for reporting.
    let recordDiscoveredProjects () =
        let projects = discoverProjects ()
        if projects.IsEmpty then invalidOp "No documentable .fsproj files were discovered."
        let configPath = Path.Combine(".livedocs", "config.json")

        let existingText =
            Run.orRaise FileSystemError.describe $"Could not read {configPath}" (readIfExists configPath)

        let config =
            match existingText with
            | Some text -> JsonNode.Parse(text).AsObject()
            | None -> JsonObject()

        config["projects"] <- JsonArray(projects |> List.map (fun project -> JsonValue.Create project :> JsonNode) |> List.toArray)
        let serialized = config.ToJsonString(JsonSerializerOptions(WriteIndented = true)) + Environment.NewLine

        Run.orRaise
            FileSystemError.describe
            $"Could not write {configPath}"
            (FileSystem.writeAllText configPath serialized)

        projects.Length, configPath

    let loadHistoryConfig () =
        let configPath = Path.Combine(".livedocs", "config.json")
        match Run.orFallback (readIfExists configPath) None with
        | None -> emptyHistoryConfig
        | Some text ->
            try
                let config = JsonNode.Parse text
                match tryProperty config "history" with
                | Some history -> { UrlPattern = tryString history "urlPattern"; Discover = tryString history "discover" }
                | None -> emptyHistoryConfig
            with _ -> emptyHistoryConfig

    let loadSiteConfig () =
        let configPath = Path.Combine(".livedocs", "config.json")
        match Run.orFallback (readIfExists configPath) None with
        | Some text -> (try Json.deserialize siteConfigCodec text with _ -> defaultSiteConfig)
        | None -> defaultSiteConfig

    /// Reads the raw documentation-set array while preserving the distinction between an absent
    /// key (the byte-compatible legacy site) and an explicitly configured set list.
    let loadDocsSetConfigs () =
        let configPath = Path.Combine(".livedocs", "config.json")

        match Run.orRaise FileSystemError.describe $"Could not read {configPath}" (readIfExists configPath) with
        | None -> None
        | Some text ->
            let config = JsonNode.Parse text

            match tryProperty config "docsSets" with
            | None -> None
            | Some(:? JsonArray as sets) ->
                sets |> Seq.map (fun set -> Json.deserialize docsSetConfigCodec (set.ToJsonString())) |> Seq.toList |> Some
            | Some _ -> invalidOp "\"docsSets\" in .livedocs/config.json must be an array."

    let hasConfiguredDocsSets () = loadDocsSetConfigs().IsSome

    /// Resolves effective sets for a command's site-wide project selection.
    let loadDocsSets projectPaths =
        let site = loadSiteConfig ()
        DocsSet.resolve site.SiteName projectPaths site.FSharpPrelude (loadDocsSetConfigs ())

    let writeIfChanged (path: string) (content: string) =
        let normalized = content.Replace("\r\n", "\n").TrimEnd() + "\n"

        let work =
            flow {
                let! existing = readIfExists path

                let shouldWrite =
                    match existing with
                    | Some text -> text.Replace("\r\n", "\n").TrimEnd() + "\n" <> normalized
                    | None -> true

                if shouldWrite then
                    let directory = Path.GetDirectoryName path
                    if not (String.IsNullOrWhiteSpace directory) then
                        do! FileSystem.createDirectory directory
                    do! FileSystem.writeAllText path normalized
            }

        Run.orRaise FileSystemError.describe $"Could not write {path}" work

    /// Creates the repository-local files required by the default workflow.
    let initialize discover =
        let setupFlow =
            flow {
                do! FileSystem.createDirectory ".livedocs"
                let! configExists = FileSystem.fileExists ".livedocs/config.json"
                if not configExists then
                    do! FileSystem.writeAllText ".livedocs/config.json" "{}"
            }

        Run.orRaise FileSystemError.describe "Could not initialize .livedocs directory" setupFlow

        let discovered = if discover then Some(recordDiscoveredProjects ()) else None

        let restFlow =
            flow {
                let! historyExists = FileSystem.fileExists ".livedocs/history.json"
                if not historyExists then
                    do! FileSystem.writeAllText ".livedocs/history.json" Templates.HistoryIndex

                let ignorePath = ".gitignore"
                let! ignoreContent = readIfExists ignorePath
                let ignored =
                    ignoreContent |> Option.map (fun s -> s.Replace("\r\n", "\n")) |> Option.defaultValue ""

                let requiredIgnores = [ ".livedocs/cache/"; ".livedocs/releases/" ]
                let missing = requiredIgnores |> List.filter (fun item -> ignored.Split('\n') |> Array.contains item |> not)
                if not missing.IsEmpty then
                    let prefix = if String.IsNullOrEmpty ignored || ignored.EndsWith("\n") then ignored else ignored + "\n"
                    do! FileSystem.writeAllText ignorePath (prefix + String.concat "\n" missing + "\n")

                do! FileSystem.createDirectory "docs"
                let! indexExists = FileSystem.fileExists "docs/index.md"
                if not indexExists then
                    do! FileSystem.writeAllText "docs/index.md" Templates.DocIndex
            }

        Run.orRaise FileSystemError.describe "Could not initialize workspace files" restFlow

        discovered
