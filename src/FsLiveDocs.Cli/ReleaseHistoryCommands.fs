namespace FsLiveDocs.Cli

open System
open System.IO
open System.Text.RegularExpressions
open Axial
open Axial.FileSystem
open Axial.HttpClient
open Axial.PlatformService
open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core
open FsLiveDocs.Core.Effects

/// The parts of a GitHub API release/asset this tool reads. Field names are pinned to GitHub's
/// own snake_case JSON, not FsLiveDocs' PascalCase convention -- this is an external API
/// contract, not a persisted FsLiveDocs model.
type internal GitHubReleaseAsset =
    { Name: string
      BrowserDownloadUrl: string
      /// Absent for assets uploaded before GitHub started computing digests; GitHub's response
      /// simply omits the field rather than sending it null.
      Digest: string option }

type internal GitHubRelease =
    { TagName: string
      Draft: bool
      Assets: GitHubReleaseAsset array }

module internal GitHubReleaseSchema =
    let private asset : Schema<GitHubReleaseAsset> =
        schema<GitHubReleaseAsset> {
            fieldAs "name" (fun (a: GitHubReleaseAsset) -> a.Name) { withSchema Schema.text }
            fieldAs "browser_download_url" (fun (a: GitHubReleaseAsset) -> a.BrowserDownloadUrl) { withSchema Schema.text }
            fieldAs "digest" (fun (a: GitHubReleaseAsset) -> a.Digest) { withSchema (Schema.option Schema.text) }
            construct (fun name browserDownloadUrl digest ->
                { Name = name; BrowserDownloadUrl = browserDownloadUrl; Digest = digest })
        }

    let releases : Schema<GitHubRelease list> =
        Schema.listWith (
            schema<GitHubRelease> {
                fieldAs "tag_name" (fun (r: GitHubRelease) -> r.TagName) { withSchema Schema.text }
                fieldAs "draft" (fun (r: GitHubRelease) -> r.Draft) { withSchema Schema.bool }
                fieldAs "assets" (fun (r: GitHubRelease) -> r.Assets |> Array.toList) { withSchema (Schema.listWith asset) }
                construct (fun tagName draft assets -> { TagName = tagName; Draft = draft; Assets = List.toArray assets })
            }
        )

module ReleaseHistoryCommands =

    /// Where `history-sync` discovers published capsules. GitHub is built in; any other host
    /// is reached through a shell command, keeping the tool free of provider integrations.
    type DiscoverySource =
        | GithubRepo of string
        | Command of string

    let private fileExists path = Run.orFallback (FileSystem.fileExists path) false
    let private runOrRaise description flow = Run.orRaise FileSystemError.describe description flow

    let private normalizedSha (context: string) (value: string) =
        let sha = value.Trim().ToLowerInvariant()
        if sha.Length <> 64 || sha |> Seq.exists (Uri.IsHexDigit >> not) then
            invalidOp $"{context}: '{value}' is not a SHA-256 hex digest."
        sha

    /// Runs a discovery command and parses each non-empty line as `version url sha256`.
    let private commandEntries (command: string) =
        let startInfo = Diagnostics.ProcessStartInfo()
        if Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.Windows then
            startInfo.FileName <- "cmd"
            startInfo.ArgumentList.Add "/c"
        else
            startInfo.FileName <- "/bin/sh"
            startInfo.ArgumentList.Add "-c"
        startInfo.ArgumentList.Add command
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        use proc = Diagnostics.Process.Start startInfo
        let output = proc.StandardOutput.ReadToEnd()
        let errors = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            invalidOp $"Discovery command failed with exit code {proc.ExitCode}: {errors.Trim()}"
        output.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun line -> line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries))
        |> Array.choose (fun parts ->
            match parts with
            | [| version; url; sha |] ->
                let version = if version.StartsWith 'v' then version.Substring 1 else version
                if not (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) then
                    invalidOp $"Discovery command returned a non-HTTPS capsule URL for {version}: {url}"
                Some ({ Version = version
                        CapsulePath = None
                        CapsuleUrl = Some url
                        CapsuleSha256 = normalizedSha $"Discovered capsule {version}" sha }: ReleaseHistoryEntry)
            | _ -> invalidOp $"""Discovery command lines must be "version url sha256"; got: {String.Join(" ", parts)}"""
        )
        |> Array.toList

    let private githubReleasesCodec = Json.compile GitHubReleaseSchema.releases

    let private requiredSha (digest: string option) =
        let digest = digest |> Option.defaultValue ""
        if String.IsNullOrWhiteSpace digest || not (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) then
            invalidOp "A LiveDocs release asset is missing its GitHub SHA-256 digest."
        let value = digest.Substring("sha256:".Length).ToLowerInvariant()
        if value.Length <> 64 || value |> Seq.exists (Uri.IsHexDigit >> not) then
            invalidOp $"GitHub reported an invalid release asset digest: {digest}"
        value

    let private releasedEntries repository =
        if String.IsNullOrWhiteSpace repository || repository.Split('/').Length <> 2 then
            invalidArg "repository" "GitHub repository must have the form owner/name."
        let repositoryName = repository.Split('/')[1]

        // The token is read once and reused across every page, matching the original's one
        // client-configured-once-then-paginated shape.
        let work =
            flow {
                let! token =
                    EnvironmentVariables.tryGet "GH_TOKEN"
                    |> Flow.map (Option.filter (String.IsNullOrWhiteSpace >> not))

                let rec load page accumulated =
                    flow {
                        let uri = $"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}"
                        let request =
                            Http.get uri
                            |> Request.userAgent "FsLiveDocs"
                            |> Request.accept "application/vnd.github+json"
                        let request = match token with Some value -> request |> Request.bearer value | None -> request
                        let! text = Http.text request
                        let releases = Json.deserialize githubReleasesCodec text
                        let combined = accumulated @ releases
                        if releases.Length = 100 then return! load (page + 1) combined else return combined
                    }

                return! load 1 []
            }

        let releases =
            match work |> Flow.run LiveEnvironment.instance with
            | Exit.Success releases -> releases
            | Exit.Failure(Cause.Fail error) -> invalidOp $"Could not list GitHub releases for {repository}: {HttpError.describe error}"
            | Exit.Failure cause -> invalidOp $"Could not list GitHub releases for {repository}: {cause}"

        releases
        |> List.filter (fun release -> not release.Draft && not (String.IsNullOrWhiteSpace release.TagName))
        |> List.choose (fun release ->
            let version = if release.TagName.StartsWith 'v' then release.TagName.Substring 1 else release.TagName
            try
                ReleaseCapsule.compareVersions version version |> ignore
                let expectedName = $"{repositoryName}-{version}-livedocs.zip"
                release.Assets
                |> Array.tryFind (fun asset -> asset.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                |> Option.map (fun asset ->
                    ({ Version = version
                       CapsulePath = None
                       CapsuleUrl = Some asset.BrowserDownloadUrl
                       CapsuleSha256 = requiredSha asset.Digest }: ReleaseHistoryEntry))
            with :? InvalidOperationException -> None)

    let private discoveredEntries source =
        match source with
        | GithubRepo repository -> releasedEntries repository
        | Command command -> commandEntries command

    let private sourceLabel source =
        match source with
        | GithubRepo repository -> repository
        | Command command -> $"'{command}'"

    let sync (source: DiscoverySource) (indexPath: string) (expectedVersion: string option) (expectedUrl: string option) (expectedSha: string option) =
        let existing =
            if fileExists indexPath then (ReleaseCapsule.loadHistoryIndex indexPath).Entries
            else []
        // The oldest committed entry is the repository's explicit compatibility floor. Capsules
        // predating that floor may use artifact contracts the current renderer intentionally does
        // not support; synchronization extends history and never silently widens that promise.
        let compatibilityFloor = existing |> List.tryLast |> Option.map _.Version
        let discovered =
            discoveredEntries source
            |> List.filter (fun entry -> compatibilityFloor |> Option.forall (fun floor -> ReleaseCapsule.compareVersions entry.Version floor >= 0))
        if discovered.IsEmpty then invalidOp $"No compatible immutable LiveDocs release capsules were found for {sourceLabel source}."
        let merged =
            discovered
            |> List.fold (fun (entries: ReleaseHistoryEntry list) (discoveredEntry: ReleaseHistoryEntry) ->
                match entries |> List.tryFind (fun (entry: ReleaseHistoryEntry) -> entry.Version = discoveredEntry.Version) with
                | None -> discoveredEntry :: entries
                | Some existingEntry when existingEntry = discoveredEntry -> entries
                | Some _ -> invalidOp $"History contains different capsule metadata for {discoveredEntry.Version}.") existing
        let updated =
            ReleaseCapsule.normalizeHistoryIndex {
                SchemaVersion = ReleaseCapsule.HistoryIndexSchemaVersion
                CurrentVersion = merged.Head.Version
                Entries = merged
            }
        match expectedVersion, expectedUrl, expectedSha with
        | None, None, None -> ()
        | Some version, Some url, Some sha ->
            let expectedSha = sha.ToLowerInvariant()
            match updated.Entries |> List.tryFind (fun entry -> entry.Version = version) with
            | Some entry when entry.CapsuleUrl = Some url && entry.CapsuleSha256 = expectedSha && updated.CurrentVersion = version -> ()
            | _ -> invalidOp $"Released capsule {version} was not found as the current version with the expected URL and SHA-256."
        | _ -> invalidOp "Expected version, URL, and SHA-256 must be supplied together."
        ReleaseCapsule.saveHistoryIndex indexPath updated
        ReleaseCapsule.loadHistoryIndex indexPath |> ignore
        updated

    /// Resolves a page-relative href to the local file it would materialize as, or None for an
    /// external/non-local target. An unsafe path (one that would escape `output`) resolves to a
    /// sentinel file instead, matching the guard `verify` relies on to report it as broken.
    let private localTarget (output: string) (page: string) (target: string) : Flow<LiveEnvironment, FileSystemError, string option> =
        let target = target.Split([| '#'; '?' |], 2)[0]
        if String.IsNullOrWhiteSpace target
           || target.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase) then
            Flow.succeed None
        else
            let asFile (relative: string) : Flow<LiveEnvironment, FileSystemError, string> =
                let path = Path.GetFullPath(Path.Combine(output, relative))
                if path <> output && not (path.StartsWith(output + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then
                    Flow.succeed (Path.Combine(output, ".livedocs-unsafe-link"))
                else
                    FileSystem.directoryExists path
                    |> Flow.map (fun isDirectory -> if isDirectory then Path.Combine(path, "index.html") else path)
            if target.StartsWith '/' then
                flow {
                    let relative = Uri.UnescapeDataString(target.TrimStart '/')
                    let! direct = asFile relative
                    let! directExists = FileSystem.fileExists direct
                    if directExists then
                        return Some direct
                    else
                        let slash = relative.IndexOf '/'
                        let! fallback = asFile (if slash >= 0 then relative.Substring(slash + 1) else relative)
                        return Some fallback
                }
            else
                asFile (Path.Combine(Path.GetDirectoryName page, Uri.UnescapeDataString target))
                |> Flow.map Some

    let private linkPattern = Regex("(?:href|src)=['\"]([^'\"]+)['\"]", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
    let private setLinkPattern = Regex("<a[^>]*href=['\"]([^'\"]+)['\"][^>]*data-docs-set-link=['\"]([^'\"]+)['\"]", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)

    /// Every file-system fact `verify` needs about one rendered site: which pages exist, their
    /// full text, and where every local link and documentation-set entry point on those pages
    /// resolves to. Gathered as one Flow so `verify` touches the file system exactly once, then
    /// validates a pure, already-materialized snapshot -- no file-system call after this point.
    type private VerificationFacts =
        { Pages: string list
          EntryPoints: (ReleaseHistoryEntry * string * bool * string) list
          ResolvedLinks: (string * string * bool option) list
          SetLinkTargets: (ReleaseHistoryEntry * string * string * (string * bool * string option) option) list }

    let private gatherVerificationFacts (index: ReleaseHistoryIndex) (root: string) (entryPoint: string -> string) =
        flow {
            let! pageArray = FileSystem.enumerateFiles root "*.html" SearchOption.AllDirectories
            let pages = pageArray |> Seq.toList
            let! pageTexts = pages |> Flow.traverse (fun page -> FileSystem.readAllText page |> Flow.map (fun text -> page, text))
            let pageTextByPath = pageTexts |> Map.ofList

            let! entryPoints =
                index.Entries
                |> Flow.traverse (fun entry ->
                    let path = entryPoint entry.Version
                    FileSystem.fileExists path
                    |> Flow.map (fun exists -> entry, path, exists, (if exists then pageTextByPath |> Map.tryFind path |> Option.defaultValue "" else "")))

            // Pagefind owns its own `pagefind/` directory and runs as a separate index step; its
            // assets are not FsLiveDocs-generated links for this check to resolve.
            let linkReferences =
                [ for page, text in pageTexts do
                      let relativePage = Path.GetRelativePath(root, page)
                      for found in linkPattern.Matches(text) do
                          let href = found.Groups[1].Value
                          if not (href.Contains "pagefind/") then yield relativePage, href ]
            let! resolvedLinks =
                linkReferences
                |> Flow.traverse (fun (relativePage, href) ->
                    flow {
                        let! target = localTarget root relativePage href
                        match target with
                        | Some path ->
                            let! exists = FileSystem.fileExists path
                            return relativePage, href, Some exists
                        | None -> return relativePage, href, None
                    })

            let setLinkReferences =
                [ for entry, path, exists, text in entryPoints do
                      if exists then
                          let landingRelative = Path.GetRelativePath(root, path)
                          for found in setLinkPattern.Matches(text) do
                              yield entry, found.Groups[1].Value, found.Groups[2].Value, landingRelative ]
            let! setLinkTargets =
                setLinkReferences
                |> Flow.traverse (fun (entry, href, setId, landingRelative) ->
                    flow {
                        let! target = localTarget root landingRelative href
                        match target with
                        | Some path ->
                            let! exists = FileSystem.fileExists path
                            let! text =
                                match exists, pageTextByPath |> Map.tryFind path with
                                | true, Some cached -> Flow.succeed (Some cached)
                                | true, None -> FileSystem.readAllText path |> Flow.map Some
                                | false, _ -> Flow.succeed None
                            return entry, href, setId, Some(path, exists, text)
                        | None -> return entry, href, setId, None
                    })

            return
                { Pages = pages
                  EntryPoints = entryPoints
                  ResolvedLinks = resolvedLinks
                  SetLinkTargets = setLinkTargets }
        }

    let verify (indexPath: string) (output: string) =
        let index = ReleaseCapsule.loadHistoryIndex indexPath
        let root = Path.GetFullPath output
        let entryPoint version =
            if version = index.CurrentVersion then Path.Combine(root, "index.html")
            else Path.Combine(root, "history", version, "index.html")

        let facts = runOrRaise $"Could not verify generated pages under {root}" (gatherVerificationFacts index root entryPoint)

        for _, path, exists, _ in facts.EntryPoints do
            if not exists then invalidOp $"Missing version entry point: {path}"

        let failures =
            facts.ResolvedLinks
            |> List.choose (fun (relativePage, href, exists) -> if exists = Some false then Some $"{relativePage} -> {href}" else None)
        if not failures.IsEmpty then
            let detail = failures |> List.truncate 50 |> String.concat Environment.NewLine
            invalidOp $"Generated links do not resolve:{Environment.NewLine}{detail}"

        for entry, href, setId, resolution in facts.SetLinkTargets do
            match resolution with
            | Some(target, true, Some text) ->
                let identity = $"data-docs-set-id=\"{setId}\""
                if not (text.Contains(identity, StringComparison.Ordinal)) then
                    invalidOp $"Documentation-set entry point for {setId} in {entry.Version} has the wrong set identity: {target}"
            | _ -> invalidOp $"Documentation-set entry point for {setId} in {entry.Version} is missing: {href}"

        let landing =
            facts.EntryPoints
            |> List.tryFind (fun (entry, _, _, _) -> entry.Version = index.CurrentVersion)
            |> Option.map (fun (_, _, _, text) -> text)
            |> Option.defaultValue ""
        let positions = index.Entries |> List.map (fun entry -> landing.IndexOf($">{entry.Version}<", StringComparison.Ordinal))
        if positions |> List.exists (fun position -> position < 0) || positions <> List.sort positions then
            invalidOp "Version switcher is missing versions or is not newest-first."

        List.length facts.Pages
