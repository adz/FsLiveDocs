namespace FsLiveDocs.Runner.Effects

open Axial
open Axial.Console
open Axial.FileSystem
open Axial.PlatformService
open Axial.Process

/// The one live environment every effect-migrated FsLiveDocs module runs its Flows against.
/// A single shared type means a single shared `runOrFallback`/`runOrRaise` (below), instead of
/// each module redefining its own environment record and copy-pasting the same two helpers.
type LiveEnvironment =
    { FileSystem: IFileSystem
      Process: IProcess }
    interface IHasFileSystem with
        member this.FileSystem = this.FileSystem
    interface IHasProcess with
        member this.Process = this.Process

[<RequireQualifiedAccess>]
module LiveEnvironment =
    let instance : LiveEnvironment =
        { FileSystem = FileSystem.live
          Process = Process.live Clock.live FileSystem.live Console.live }
