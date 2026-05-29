namespace FsLiveDocs.SnapshotTests

open System.Threading.Tasks
open FsLiveDocs.Core
open FsLiveDocs.Runner
open VerifyXunit
open Xunit

module SnapshotTests =
    [<Fact>]
    let ``FsLiveDocs.Core snapshot examples`` () =
        task {
            let projectPath = @"../../src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            let! snapshot = DocTestRunner.collectSnapshots package projectPath []
            return! Verifier.Verify(snapshot)
        }
    [<Fact>]
    let ``FsLiveDocs.Runner snapshot examples`` () =
        task {
            let projectPath = @"../../src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj"
            let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            let! snapshot = DocTestRunner.collectSnapshots package projectPath []
            return! Verifier.Verify(snapshot)
        }
    [<Fact>]
    let ``FsLiveDocs.Renderer snapshot examples`` () =
        task {
            let projectPath = @"../../src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj"
            let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            let! snapshot = DocTestRunner.collectSnapshots package projectPath []
            return! Verifier.Verify(snapshot)
        }
    [<Fact>]
    let ``FsLiveDocs.Cli snapshot examples`` () =
        task {
            let projectPath = @"../../src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj"
            let package = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            let! snapshot = DocTestRunner.collectSnapshots package projectPath []
            return! Verifier.Verify(snapshot)
        }
