namespace FsLiveDocs.Cli

open System
open System.IO
open FsLiveDocs.Core

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
          FSharpPrelude = None }

    /// Finds documentable projects when callers omit the project list.
    let discoverProjects () =
        let root = Directory.GetCurrentDirectory()
        let ignored =
            set [ ".git"; ".livedocs"; "artifacts"; "bin"; "node_modules"; "obj"; "output"; "packages"; "TestResults"; "tests" ]
        let isIgnored (path: string) =
            Path.GetRelativePath(root, path).Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |])
            |> Array.exists ignored.Contains
        Directory.GetFiles(root, "*.fsproj", SearchOption.AllDirectories)
        |> Array.filter (isIgnored >> not)
        |> Array.sort
        |> Array.map (fun path -> Path.GetRelativePath(root, path).Replace('\\', '/'))
        |> Array.toList

    let private configuredProjects () =
        let configPath = Path.Combine(".livedocs", "config.json")
        if not (File.Exists configPath) then []
        else
            let config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText configPath)

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
        let config =
            if File.Exists configPath then Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText configPath)
            else Newtonsoft.Json.Linq.JObject()
        config["projects"] <- Newtonsoft.Json.Linq.JArray(projects |> List.map Newtonsoft.Json.Linq.JValue)
        File.WriteAllText(configPath, config.ToString(Newtonsoft.Json.Formatting.Indented) + Environment.NewLine)
        projects.Length, configPath

    let loadHistoryConfig () =
        let configPath = Path.Combine(".livedocs", "config.json")
        if not (File.Exists configPath) then emptyHistoryConfig
        else
            try
                let config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText configPath)
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
        if File.Exists configPath then
            try
                let config =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<SiteConfig>(
                        File.ReadAllText configPath,
                        Serialization.jsonSettings)
                if isNull (box config) then defaultSiteConfig else config
            with _ -> defaultSiteConfig
        else defaultSiteConfig

    /// Reads the raw documentation-set array while preserving the distinction between an absent
    /// key (the byte-compatible legacy site) and an explicitly configured set list.
    let loadDocsSetConfigs () =
        let configPath = Path.Combine(".livedocs", "config.json")

        if not (File.Exists configPath) then
            None
        else
            let config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText configPath)

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
        let shouldWrite =
            if File.Exists path then File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd() + "\n" <> normalized
            else true
        if shouldWrite then
            let directory = Path.GetDirectoryName path
            if not (String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory(directory) |> ignore
            File.WriteAllText(path, normalized)

    /// Creates the repository-local files required by the default workflow.
    let initialize discover =
        Directory.CreateDirectory(".livedocs") |> ignore
        if not (File.Exists ".livedocs/config.json") then File.WriteAllText(".livedocs/config.json", "{}")
        let discovered = if discover then Some(recordDiscoveredProjects ()) else None
        if not (File.Exists ".livedocs/history.json") then File.WriteAllText(".livedocs/history.json", Templates.HistoryIndex)

        let ignorePath = ".gitignore"
        let ignored =
            if File.Exists ignorePath then File.ReadAllText(ignorePath).Replace("\r\n", "\n")
            else ""
        let requiredIgnores = [ ".livedocs/cache/"; ".livedocs/releases/" ]
        let missing = requiredIgnores |> List.filter (fun item -> ignored.Split('\n') |> Array.contains item |> not)
        if not missing.IsEmpty then
            let prefix = if String.IsNullOrEmpty ignored || ignored.EndsWith("\n") then ignored else ignored + "\n"
            File.WriteAllText(ignorePath, prefix + String.concat "\n" missing + "\n")

        Directory.CreateDirectory("docs") |> ignore
        if not (File.Exists "docs/index.md") then File.WriteAllText("docs/index.md", Templates.DocIndex)
        discovered
