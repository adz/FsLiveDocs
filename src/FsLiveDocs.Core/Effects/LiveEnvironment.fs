namespace FsLiveDocs.Core.Effects

open Axial
open Axial.Console
open Axial.FileSystem
open Axial.HttpClient
open Axial.PlatformService
open Axial.Process

/// The one live environment every effect-migrated FsLiveDocs module runs its Flows against.
/// A single shared type means a single shared `runOrFallback`/`runOrRaise` (below), instead of
/// each module redefining its own environment record and copy-pasting the same two helpers.
type LiveEnvironment =
    { FileSystem: IFileSystem
      Process: IProcess
      Http: IHttp
      EnvironmentVariables: IEnvironmentVariables }
    interface IHasFileSystem with
        member this.FileSystem = this.FileSystem
    interface IHasProcess with
        member this.Process = this.Process
    interface IHasHttp with
        member this.Http = this.Http
    interface IHasEnvironmentVariables with
        member this.EnvironmentVariables = this.EnvironmentVariables

[<RequireQualifiedAccess>]
module LiveEnvironment =
    // One HttpClient for the process, not one per call -- HttpClient is designed to be shared
    // and reused; disposing and recreating it per request (the pre-migration ReleaseHistoryCommands
    // pattern) exhausts sockets under load.
    let private sharedHttpClient = new System.Net.Http.HttpClient()

    let instance : LiveEnvironment =
        { FileSystem = FileSystem.live
          Process = Process.live Clock.live FileSystem.live Console.live
          Http = Http.live Clock.live sharedHttpClient
          EnvironmentVariables = EnvironmentVariables.live }
