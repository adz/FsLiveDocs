namespace FsLiveDocs.Cli

open System
open System.Security.Cryptography
open System.Text

/// Stable, dependency-precise identities for disposable analysis artifacts.
module internal AnalysisCache =

    let private sha256 (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    /// Identifies one project's extracted symbol model without coupling it to documentation.
    let projectKey extractorContext projectInputHash =
        sha256 (extractorContext + "\n" + projectInputHash)

    /// Identifies generated-example verification, which depends on documentation coverage.
    let verificationKey extractorContext documentationHash prelude projectKeys =
        [ yield extractorContext
          yield documentationHash
          yield prelude
          yield! projectKeys ]
        |> String.concat "\n--fslivedocs-verification-input--\n"
        |> sha256

    /// One file's content identity, with an explicit marker for a path that is not present.
    type FileIdentity = { FullPath: string; ContentSha256: string option }

    /// The semantic inputs that made one page's compiler result meaningful.
    type PageCompilerContext =
        { SetId: string
          SelectedProject: string
          RequestedFramework: string option
          EffectiveFramework: string
          Configuration: string
          CompilerInputHash: string }

    let private field (tag: string) (value: string) =
        let tagLength = Encoding.UTF8.GetByteCount tag
        let valueLength = Encoding.UTF8.GetByteCount value
        $"{tagLength}:{tag}{valueLength}:{value}"

    let private optionField (tag: string) (value: string option) =
        match value with
        | None -> field (tag + "=none") ""
        | Some value -> field (tag + "=some") value

    /// Identifies one page's compiler-derived meaning without coupling it to unrelated pages.
    /// Every field is tagged and length-delimited so no user-controlled value can collide with
    /// another field or with the absent-framework marker.
    let pageKey commonContext setId selectedProject requestedFramework compilerInputHash sourcePath prelude blockIdentities =
        [ yield field "common-context" commonContext
          yield field "set-id" setId
          yield field "selected-project" selectedProject
          yield optionField "requested-framework" requestedFramework
          yield field "compiler-input-hash" compilerInputHash
          yield field "source-path" sourcePath
          yield field "prelude" prelude
          for block in blockIdentities do
              yield field "block" block ]
        |> String.concat ""
        |> sha256

    /// The complete semantic artifact is exactly the set of page meanings plus the artifact-wide
    /// default prelude and shared context. Deriving it from page keys keeps both cache layers on
    /// one dependency list.
    let artifactKey commonContext defaultPrelude pageKeys =
        [ yield field "common-context" commonContext
          yield field "default-prelude" defaultPrelude
          for key in pageKeys do
              yield field "page-key" key ]
        |> String.concat ""
        |> sha256
