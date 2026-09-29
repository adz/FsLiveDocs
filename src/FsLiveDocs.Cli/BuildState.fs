namespace FsLiveDocs.Cli

open System
open System.IO
open System.Text
open System.Security.Cryptography

/// Durable, cheap-to-validate state for a completed local site build.
module internal BuildState =

    [<CLIMutable>]
    type FileStamp =
        { Path: string
          Length: int64
          LastWriteUtcTicks: int64
          Sha256: string }

    [<CLIMutable>]
    type Snapshot =
        { FormatVersion: int
          Invocation: string
          Files: FileStamp array }

    let private statePath root = Path.Combine(root, ".livedocs", "cache", "build-state.json")

    let private filesUnder (directory: string) =
        let rec walk path = seq {
            for childDirectory in Directory.EnumerateDirectories path do
                yield! walk childDirectory
            yield! Directory.EnumerateFiles path
        }
        if Directory.Exists directory then walk directory else Seq.empty

    let private fullPath (root: string) (path: string) =
        if Path.IsPathRooted path then Path.GetFullPath path else Path.GetFullPath(Path.Combine(root, path))

    let private inputFiles root documentationRoots =
        seq {
            for documentationRoot in documentationRoots do
                yield! filesUnder (fullPath root documentationRoot)
            yield! filesUnder (Path.Combine(root, ".livedocs", "history"))
            yield Path.Combine(root, ".livedocs", "config.json")
            yield Path.Combine(root, ".livedocs", "history.json")
        }

    let private fileStamp fullRoot path =
        let fullPath = Path.GetFullPath path
        let relativePath = Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/')
        if File.Exists fullPath then
            let info = FileInfo fullPath
            use stream = File.OpenRead fullPath
            { Path = relativePath
              Length = info.Length
              LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
              Sha256 = SHA256.HashData(stream) |> Convert.ToHexString |> _.ToLowerInvariant() }
        else
            { Path = relativePath
              Length = -1L
              LastWriteUtcTicks = -1L
              Sha256 = "" }

    let capture root invocation documentationRoots =
        let fullRoot = Path.GetFullPath root
        let files =
            inputFiles fullRoot documentationRoots
            |> Seq.distinctBy Path.GetFullPath
            |> Seq.map (fileStamp fullRoot)
            |> Seq.sortBy _.Path
            |> Seq.toArray
        { FormatVersion = 3; Invocation = invocation; Files = files }

    let private encode (value: string) = Convert.ToBase64String(Encoding.UTF8.GetBytes value)
    let private decode (value: string) = Encoding.UTF8.GetString(Convert.FromBase64String value)

    let save root snapshot =
        let path = statePath (Path.GetFullPath root)
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        let temporary = path + ".tmp-" + Guid.NewGuid().ToString("N")
        let lines =
            [ yield string snapshot.FormatVersion
              yield encode snapshot.Invocation
              for file in snapshot.Files do
                  yield $"{encode file.Path}\t{file.Length}\t{file.LastWriteUtcTicks}\t{file.Sha256}" ]
        File.WriteAllLines(temporary, lines)
        File.Move(temporary, path, true)

    let private load path =
        let lines = File.ReadAllLines path
        if lines.Length < 2 then invalidOp "Build state is incomplete."
        let files =
            lines
            |> Array.skip 2
            |> Array.map (fun line ->
                match line.Split('\t') with
                | [| path; length; ticks; sha256 |] ->
                    { Path = decode path
                      Length = Int64.Parse length
                      LastWriteUtcTicks = Int64.Parse ticks
                      Sha256 = sha256 }
                | _ -> invalidOp "Build state contains an invalid file stamp.")
        { FormatVersion = Int32.Parse lines.[0]
          Invocation = decode lines.[1]
          Files = files }

    let isCurrent root invocation documentationRoots =
        let fullRoot = Path.GetFullPath root
        let path = statePath fullRoot
        let output = Path.Combine(fullRoot, "output")
        if not (File.Exists path) || not (Directory.Exists output) || (Directory.EnumerateFileSystemEntries(output) |> Seq.isEmpty) then
            false
        else
            try
                let saved = load path
                let current = capture fullRoot invocation documentationRoots
                saved.FormatVersion = 3
                && saved.Invocation = invocation
                && Array.length saved.Files = Array.length current.Files
                && Array.forall2 (=) saved.Files current.Files
            with _ -> false
