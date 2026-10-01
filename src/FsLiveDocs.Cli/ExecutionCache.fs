namespace FsLiveDocs.Cli

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes

/// <summary>
/// Cross-invocation cache for examples that declared themselves deterministic.
/// </summary>
/// <remarks>
/// Only a passing result is stored, and only the caller decides when reuse is sound: the block's
/// <c>deterministic</c> fence option is the author's assertion that the example does not read a
/// clock, network, filesystem, environment, or process. The key must include every input that can
/// change the result, so a changed source, reference, prelude, compiler, or tool cannot hit an
/// older entry. Read and write are best-effort: caching is an optimization and must never fail a
/// run.
/// </remarks>
module internal ExecutionCache =

    [<Literal>]
    let private SchemaVersion = 1

    let private directory = Path.Combine(".livedocs", "cache", "execution")

    let private sha256 (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    /// A stable identity for one deterministic execution, from a complete list of inputs.
    let key (parts: string list) =
        sha256 (String.concat "\n--livedocs-execution--\n" parts)

    let private pathFor (key: string) = Path.Combine(directory, key + ".json")

    /// A cached passing output, or None when the cache misses or the entry cannot be trusted.
    let tryRead (key: string) : string option =
        try
            let path = pathFor key
            if not (File.Exists path) then
                None
            else
                match JsonNode.Parse(File.ReadAllText path) with
                | :? JsonObject as node when node["SchemaVersion"].GetValue<int>() = SchemaVersion ->
                    Some(node["Output"].GetValue<string>())
                | _ -> None
        with _ ->
            None

    /// Best-effort write of a passing output. A cache failure must not fail the run.
    let write (key: string) (output: string) =
        try
            Directory.CreateDirectory directory |> ignore
            let node = JsonObject()
            node["SchemaVersion"] <- JsonValue.Create SchemaVersion
            node["Key"] <- JsonValue.Create key
            node["Output"] <- JsonValue.Create output
            node["CheckedAtUtc"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("o"))
            File.WriteAllText(pathFor key, node.ToJsonString())
        with _ ->
            ()
