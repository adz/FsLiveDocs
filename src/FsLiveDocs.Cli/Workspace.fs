namespace FsLiveDocs.Cli

open System
open System.IO
open Axial
open Axial.FileSystem
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects

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
            let config = Newtonsoft.Json.Linq.JObject.Parse(text)

            let topLevel =
                match config.GetValue("projects", StringComparison.OrdinalIgnoreCase) with
                | :? Newtonsoft.Json.Linq.JArray as projects -> projects.Values<string>()
                | _ -> Seq.empty

            let setProjects =
                match config.GetValue("docsSets", StringComparison.OrdinalIgnoreCase) with
                | :? Newtonsoft.Json.Linq.JArray as sets ->
                    sets.Children<Newtonsoft.Json.Linq.JObject>()
                    |> Seq.collect (fun set ->
                        match set.GetValue("projects", StringComparison.OrdinalIgnoreCase) with
                        | :? Newtonsoft.Json.Linq.JArray as projects -> projects.Values<string>()
                        | _ -> Seq.empty)
                | _ -> Seq.empty

            Seq.append topLevel setProjects
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
            | Some text -> Newtonsoft.Json.Linq.JObject.Parse(text)
            | None -> Newtonsoft.Json.Linq.JObject()

        config["projects"] <- Newtonsoft.Json.Linq.JArray(projects |> List.map Newtonsoft.Json.Linq.JValue)
        let serialized = config.ToString(Newtonsoft.Json.Formatting.Indented) + Environment.NewLine

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
                let config = Newtonsoft.Json.Linq.JObject.Parse(text)
                match config.GetValue("history", StringComparison.OrdinalIgnoreCase) with
                | :? Newtonsoft.Json.Linq.JObject as history ->
                    let read name =
                        match history.GetValue(name, StringComparison.OrdinalIgnoreCase) with
                        | null -> None
                        | token ->
                            let value = token.ToString()
                            if String.IsNullOrWhiteSpace value then None else Some value
                    { UrlPattern = read "urlPattern"; Discover = read "discover" }
                | _ -> emptyHistoryConfig
            with _ -> emptyHistoryConfig

    let loadSiteConfig () =
        let configPath = Path.Combine(".livedocs", "config.json")
        match Run.orFallback (readIfExists configPath) None with
        | Some text ->
            try
                let config =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<SiteConfig>(text, Serialization.jsonSettings)
                if isNull (box config) then defaultSiteConfig else config
            with _ -> defaultSiteConfig
        | None -> defaultSiteConfig

    /// Reads the raw documentation-set array while preserving the distinction between an absent
    /// key (the byte-compatible legacy site) and an explicitly configured set list.
    let loadDocsSetConfigs () =
        let configPath = Path.Combine(".livedocs", "config.json")

        match Run.orRaise FileSystemError.describe $"Could not read {configPath}" (readIfExists configPath) with
        | None -> None
        | Some text ->
            let config = Newtonsoft.Json.Linq.JObject.Parse(text)

            match config.GetValue("docsSets", StringComparison.OrdinalIgnoreCase) with
            | null -> None
            | :? Newtonsoft.Json.Linq.JArray as sets ->
                let serializer = Newtonsoft.Json.JsonSerializer.Create(Serialization.jsonSettings)
                let values = sets.ToObject<DocsSetConfig list>(serializer)
                Some(if isNull (box values) then [] else values)
            | _ -> invalidOp "\"docsSets\" in .livedocs/config.json must be an array."

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
