namespace FsLiveDocs.Cli

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text.RegularExpressions
open FsLiveDocs.Core
open Newtonsoft.Json

[<CLIMutable>]
type internal GitHubReleaseAsset =
    { [<JsonProperty("name")>]
      Name: string
      [<JsonProperty("browser_download_url")>]
      BrowserDownloadUrl: string
      [<JsonProperty("digest")>]
      Digest: string }

[<CLIMutable>]
type internal GitHubRelease =
    { [<JsonProperty("tag_name")>]
      TagName: string
      [<JsonProperty("draft")>]
      Draft: bool
      [<JsonProperty("assets")>]
      Assets: GitHubReleaseAsset array }

module ReleaseHistoryCommands =

    let private requiredSha (digest: string) =
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
        use client = new HttpClient()
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FsLiveDocs")
        client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue("application/vnd.github+json"))
        match Environment.GetEnvironmentVariable "GH_TOKEN" |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not) with
        | Some token -> client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        | None -> ()
        let rec load page accumulated =
            let uri = $"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}"
            use response = client.GetAsync(uri).GetAwaiter().GetResult()
            response.EnsureSuccessStatusCode() |> ignore
            let releases = JsonConvert.DeserializeObject<GitHubRelease array>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            let releases = if isNull releases then [||] else releases
            let combined = Array.append accumulated releases
            if releases.Length = 100 then load (page + 1) combined else combined
        load 1 [||]
        |> Array.filter (fun release -> not release.Draft && not (String.IsNullOrWhiteSpace release.TagName))
        |> Array.choose (fun release ->
            let version = if release.TagName.StartsWith 'v' then release.TagName.Substring 1 else release.TagName
            try
                ReleaseCapsule.compareVersions version version |> ignore
                let expectedName = $"{repositoryName}-{version}-livedocs.zip"
                release.Assets
                |> Option.ofObj
                |> Option.defaultValue [||]
                |> Array.tryFind (fun asset -> asset.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                |> Option.map (fun asset ->
                    ({ Version = version
                       CapsulePath = None
                       CapsuleUrl = Some asset.BrowserDownloadUrl
                       CapsuleSha256 = requiredSha asset.Digest }: ReleaseHistoryEntry))
            with :? InvalidOperationException -> None)
        |> Array.toList

    let sync (repository: string) (indexPath: string) (expectedVersion: string option) (expectedUrl: string option) (expectedSha: string option) =
        let existing =
            if File.Exists indexPath then (ReleaseCapsule.loadHistoryIndex indexPath).Entries
            else []
        // The oldest committed entry is the repository's explicit compatibility floor. Capsules
        // predating that floor may use artifact contracts the current renderer intentionally does
        // not support; synchronization extends history and never silently widens that promise.
        let compatibilityFloor = existing |> List.tryLast |> Option.map _.Version
        let discovered =
            releasedEntries repository
            |> List.filter (fun entry -> compatibilityFloor |> Option.forall (fun floor -> ReleaseCapsule.compareVersions entry.Version floor >= 0))
        if discovered.IsEmpty then invalidOp $"No compatible immutable LiveDocs release capsules were found for {repository}."
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

    let private localTarget (output: string) (page: string) (target: string) =
        let target = target.Split([| '#'; '?' |], 2)[0]
        if String.IsNullOrWhiteSpace target
           || target.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase) then None
        else
            let asFile (relative: string) =
                let path = Path.GetFullPath(Path.Combine(output, relative))
                if path <> output && not (path.StartsWith(output + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then
                    Path.Combine(output, ".livedocs-unsafe-link")
                elif Directory.Exists path then Path.Combine(path, "index.html")
                else path
            if target.StartsWith '/' then
                let relative = Uri.UnescapeDataString(target.TrimStart '/')
                let direct = asFile relative
                if File.Exists direct then Some direct
                else
                    let slash = relative.IndexOf '/'
                    Some(asFile (if slash >= 0 then relative.Substring(slash + 1) else relative))
            else
                Some(asFile (Path.Combine(Path.GetDirectoryName page, Uri.UnescapeDataString target)))

    let verify (indexPath: string) (output: string) =
        let index = ReleaseCapsule.loadHistoryIndex indexPath
        let root = Path.GetFullPath output
        let entryPoint version =
            if version = index.CurrentVersion then Path.Combine(root, "index.html")
            else Path.Combine(root, "history", version, "index.html")
        for entry in index.Entries do
            let path = entryPoint entry.Version
            if not (File.Exists path) then invalidOp $"Missing version entry point: {path}"
        let links = Regex("(?:href|src)=['\"]([^'\"]+)['\"]", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
        let failures = ResizeArray<string>()
        for page in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories) do
            let relativePage = Path.GetRelativePath(root, page)
            for found in links.Matches(File.ReadAllText page) do
                match localTarget root relativePage found.Groups[1].Value with
                | Some target when not (File.Exists target) -> failures.Add($"{relativePage} -> {found.Groups[1].Value}")
                | _ -> ()
        if failures.Count > 0 then
            let detail = failures |> Seq.truncate 50 |> String.concat Environment.NewLine
            invalidOp $"Generated links do not resolve:{Environment.NewLine}{detail}"
        let landing = File.ReadAllText(entryPoint index.CurrentVersion)
        let positions = index.Entries |> List.map (fun entry -> landing.IndexOf($">{entry.Version}<", StringComparison.Ordinal))
        if positions |> List.exists (fun position -> position < 0) || positions <> List.sort positions then
            invalidOp "Version switcher is missing versions or is not newest-first."
        Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories) |> Seq.length
