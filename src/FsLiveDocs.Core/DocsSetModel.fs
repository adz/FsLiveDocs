namespace FsLiveDocs.Core

open System
open System.IO
open System.Text.RegularExpressions

/// <summary>Author-facing configuration for one documentation set, read from the
/// <c>docsSets</c> array in <c>.livedocs/config.json</c>.</summary>
type DocsSetConfig =
    {
        /// <summary>Stable slug identity for the set.</summary>
        Id: string
        /// <summary>Display name. Defaults to a title-cased <c>Id</c>.</summary>
        Title: string option
        /// <summary>Repository-relative Markdown source directory. Defaults to <c>docs</c>.</summary>
        Source: string option
        /// <summary>Route prefix. Defaults to <c>""</c> for the default set and to <c>Id</c> otherwise.</summary>
        Path: string option
        /// <summary>Project paths whose API surface this set exposes.</summary>
        Projects: string list
        /// <summary>Whether this set renders at the site root. Exactly one set must set this.</summary>
        Default: bool option
        /// <summary>Whether the set renders its own isolated sidebar. Defaults to true.</summary>
        Sidebar: bool option
        /// <summary>Whether the set renders an API reference for its projects. Defaults to true.</summary>
        Api: bool option
        /// <summary>Repository-owned F# setup compiled and shown for this set's checked F# blocks.</summary>
        FSharpPrelude: string option
    }

/// <summary>A fully resolved documentation set: an isolated content area that shares the site
/// shell, search index, theme, and version history with every other set.</summary>
type DocsSet =
    {
        /// <summary>Stable slug identity, used in block ids, search metadata, and release capsules.</summary>
        Id: string
        /// <summary>Display name shown in the set's sidebar heading and the set switcher.</summary>
        Title: string
        /// <summary>Repository-relative Markdown source directory.</summary>
        Source: string
        /// <summary>Route prefix. <c>""</c> for the site-root default set; otherwise a normalized slash path.</summary>
        Path: string
        /// <summary>Project paths whose API surface this set exposes.</summary>
        Projects: string list
        /// <summary>Whether this set renders at the site root and is the redirect target of <c>/</c>.</summary>
        IsDefault: bool
        /// <summary>Whether the set renders its own isolated sidebar.</summary>
        Sidebar: bool
        /// <summary>Whether the set renders an API reference for its projects.</summary>
        Api: bool
        /// <summary>Repository-owned F# setup compiled and shown for this set's checked F# blocks.</summary>
        FSharpPrelude: string option
    }

/// <summary>Resolves and interprets documentation sets shared by the CLI, renderer, and release capture.</summary>
module DocsSet =

    /// <summary>The identity given to the single implicit set of a repository without <c>docsSets</c>.</summary>
    [<Literal>]
    let DefaultId = "docs"

    let private slugPattern = Regex(@"^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled)

    let private titleCase (value: string) =
        value.Split([| '-'; '_'; ' ' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun part -> part.Substring(0, 1).ToUpperInvariant() + part.Substring(1))
        |> String.concat " "

    let private normalizeSegments (value: string) =
        value.Replace('\\', '/').Trim().Trim('/')

    let private validateRelativePath field setId (value: string) =
        let segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries)

        if
            Path.IsPathRooted value
            || segments |> Array.exists (fun segment -> segment = "." || segment = "..")
        then
            invalidOp
                $"Documentation set {setId} has an unsafe {field} path: '{value}'. Use a repository-relative path without '.' or '..' segments."

        value

    /// <summary>The route prefix ending in <c>/</c>, or <c>""</c> for the site-root default set.</summary>
    let routePrefix (set: DocsSet) =
        if String.IsNullOrEmpty set.Path then "" else set.Path + "/"

    /// <summary>The historical single-site layout, used when a repository has not configured <c>docsSets</c>.</summary>
    let implicit (siteName: string option) (projects: string list) (prelude: string option) : DocsSet =
        { Id = DefaultId
          Title =
            siteName
            |> Option.filter (String.IsNullOrWhiteSpace >> not)
            |> Option.defaultValue "Documentation"
          Source = "docs"
          Path = ""
          Projects = projects
          IsDefault = true
          Sidebar = true
          Api = true
          FSharpPrelude = prelude }

    /// <summary>Resolves configured sets, or the single implicit set when <paramref name="configured"/> is None.</summary>
    /// <remarks>Validates slug ids, unique ids, routes and sources, and exactly one default set.</remarks>
    let resolve
        (siteName: string option)
        (topLevelProjects: string list)
        (topLevelPrelude: string option)
        (configured: DocsSetConfig list option)
        : DocsSet list =
        match configured with
        | None -> [ implicit siteName topLevelProjects topLevelPrelude ]
        | Some [] -> invalidOp "\"docsSets\" is present but empty. Remove the key to use the default documentation set."
        | Some raw ->
            let sets =
                raw
                |> List.map (fun cfg ->
                    let id = (if isNull cfg.Id then "" else cfg.Id).Trim()

                    if not (slugPattern.IsMatch id) then
                        invalidOp
                            $"Documentation set id '{cfg.Id}' is not a slug: use lower-case letters, digits, and hyphens, starting with a letter or digit."

                    let isDefault = cfg.Default |> Option.defaultValue false

                    let path =
                        match cfg.Path |> Option.map normalizeSegments with
                        | None
                        | Some "" -> if isDefault then "" else id
                        | Some _ when isDefault ->
                            invalidOp
                                $"Default documentation set {id} must use the site root; remove its non-empty \"path\"."
                        | Some value -> validateRelativePath "route" id value

                    let projects =
                        (if isNull (box cfg.Projects) then [] else cfg.Projects)
                        |> List.filter (String.IsNullOrWhiteSpace >> not)
                        |> List.map (fun project -> project.Replace('\\', '/').Trim())
                        |> List.distinct

                    { Id = id
                      Title =
                        cfg.Title
                        |> Option.filter (String.IsNullOrWhiteSpace >> not)
                        |> Option.defaultValue (titleCase id)
                      Source =
                        cfg.Source
                        |> Option.map normalizeSegments
                        |> Option.filter (String.IsNullOrWhiteSpace >> not)
                        |> Option.defaultValue "docs"
                        |> validateRelativePath "source" id
                      Path = path
                      Projects = projects
                      IsDefault = isDefault
                      Sidebar = cfg.Sidebar |> Option.defaultValue true
                      // A set that lists no project has no API surface to render, whatever "api" says.
                      Api = (cfg.Api |> Option.defaultValue true) && not projects.IsEmpty
                      FSharpPrelude = cfg.FSharpPrelude |> Option.orElse topLevelPrelude })

            match sets |> List.filter _.IsDefault with
            | [] -> invalidOp "One documentation set must be marked \"default\": true."
            | [ _ ] -> ()
            | many ->
                invalidOp
                    $"""Exactly one documentation set may be "default": true; found {many.Length} ({many |> List.map _.Id |> String.concat ", "})."""

            match sets |> List.countBy _.Id |> List.tryFind (fun (_, count) -> count > 1) with
            | Some(id, _) -> invalidOp $"Duplicate documentation set id: {id}."
            | None -> ()

            match sets |> List.countBy _.Path |> List.tryFind (fun (_, count) -> count > 1) with
            | Some(path, _) ->
                let label = if path = "" then "the site root" else $"route '{path}'"
                invalidOp $"Two documentation sets resolve to {label}. Give each set a distinct \"path\"."
            | None -> ()

            match
                sets
                |> List.countBy (fun set -> set.Source.ToLowerInvariant())
                |> List.tryFind (fun (_, count) -> count > 1)
            with
            | Some(source, _) ->
                invalidOp
                    $"Two documentation sets use the same \"source\" directory ('{source}'). Give each set a distinct source, or nest one inside the other."
            | None -> ()

            sets

    /// <summary>The set that owns a repository-relative Markdown path, by most-specific (longest) source-root match.</summary>
    let ownerOf (sets: DocsSet list) (repositoryRelativePath: string) : DocsSet option =
        let normalized = repositoryRelativePath.Replace('\\', '/').TrimStart('/')

        sets
        |> List.filter (fun set ->
            let root = set.Source.Trim('/') + "/"
            normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        |> List.sortByDescending (fun set -> set.Source.Length)
        |> List.tryHead

    /// <summary>The default set: the one rendered at the site root.</summary>
    let defaultSet (sets: DocsSet list) =
        sets
        |> List.tryFind _.IsDefault
        |> Option.defaultWith (fun () -> invalidOp "The resolved documentation sets contain no default set.")
