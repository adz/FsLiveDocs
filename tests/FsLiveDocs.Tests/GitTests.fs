namespace FsLiveDocs.Tests

open System
open System.Diagnostics
open System.IO
open Xunit
open FsLiveDocs.Cli

module GitTests =

    let private runGit (directory: string) (arguments: string) =
        let startInfo = ProcessStartInfo("git", arguments)
        startInfo.WorkingDirectory <- directory
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        use proc = Process.Start(startInfo)
        proc.WaitForExit()
        Assert.Equal(0, proc.ExitCode)

    [<Fact>]
    let ``current revision returns the trimmed HEAD sha in a real git repository`` () =
        let directory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore
        runGit directory "init"
        runGit directory "config user.email test@example.com"
        runGit directory "config user.name Test"
        File.WriteAllText(Path.Combine(directory, "file.txt"), "content")
        runGit directory "add file.txt"
        runGit directory "commit -m initial"

        let revision = Git.currentRevision directory
        Assert.Matches("^[0-9a-f]{40}$", revision)

    [<Fact>]
    let ``current revision raises when the directory is not a git repository`` () =
        let directory = Path.Combine(Path.GetTempPath(), "FsLiveDocsTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore

        Assert.Throws<InvalidOperationException>(fun () -> Git.currentRevision directory |> ignore) |> ignore
