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

    /// Identifies one page's compiler-derived meaning without coupling it to unrelated pages.
    let pageKey commonContext sourcePath targetFramework prelude blockIdentities =
        [ yield commonContext
          yield sourcePath
          yield targetFramework
          yield prelude
          yield! blockIdentities ]
        |> String.concat "\n--fslivedocs-page-input--\n"
        |> sha256
