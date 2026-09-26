namespace FsLiveDocs.Cli

open System
open System.IO
open System.Text

/// Durable, cheap-to-validate state for a completed local site build.
module internal BuildState =

    [<CLIMutable>]
    type FileStamp =
        { Path: string
          Length: int64
          LastWriteUtcTicks: int64 }

    [<CLIMutable>]
    type Snapshot =
        { FormatVersion: int
          Invocation: string
          Files: FileStamp array }

    let private statePath root = Path.Combine(root, ".livedocs", "cache", "build-state.json")

    let private ignoredDirectoryNames =
        set [ ".git"; "bin"; "obj"; "output"; "artifacts" ]

    let private ignoredPath (root: string) (path: string) =
        let relative = Path.GetRelativePath(root, path)
        let segments = relative.Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |], StringSplitOptions.RemoveEmptyEntries)
        ignoredDirectoryNames.Contains segments.[0]
        || (segments.Length >= 2
            && segments.[0] = ".livedocs"
            && (segments.[1] = "cache" || segments.[1] = "releases"))

    let private inputFiles root =
        let rec walk directory = seq {
            for childDirectory in Directory.EnumerateDirectories directory do
                if not (ignoredPath root childDirectory) then
                    yield! walk childDirectory
            for file in Directory.EnumerateFiles directory do
                if not (ignoredPath root file) then yield file
        }
        walk root

    let capture root invocation =
        let fullRoot = Path.GetFullPath root
        let files =
            inputFiles fullRoot
            |> Seq.map (fun path ->
                let info = FileInfo path
                { Path = Path.GetRelativePath(fullRoot, path).Replace('\\', '/')
                  Length = info.Length
                  LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks })
            |> Seq.sortBy _.Path
            |> Seq.toArray
        { FormatVersion = 1; Invocation = invocation; Files = files }

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
                  yield $"{encode file.Path}\t{file.Length}\t{file.LastWriteUtcTicks}" ]
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
                | [| path; length; ticks |] ->
                    { Path = decode path
                      Length = Int64.Parse length
                      LastWriteUtcTicks = Int64.Parse ticks }
                | _ -> invalidOp "Build state contains an invalid file stamp.")
        { FormatVersion = Int32.Parse lines.[0]
          Invocation = decode lines.[1]
          Files = files }

    let isCurrent root invocation =
        let fullRoot = Path.GetFullPath root
        let path = statePath fullRoot
        let output = Path.Combine(fullRoot, "output")
        if not (File.Exists path) || not (Directory.Exists output) || (Directory.EnumerateFileSystemEntries(output) |> Seq.isEmpty) then
            false
        else
            try
                let saved = load path
                let current = capture fullRoot invocation
                saved.FormatVersion = 1
                && saved.Invocation = invocation
                && Array.length saved.Files = Array.length current.Files
                && Array.forall2 (=) saved.Files current.Files
            with _ -> false
