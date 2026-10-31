namespace FsLiveDocs.Core

open System
open System.IO
open System.Text.Json.Nodes

/// <summary>Resolves the compiler references MSBuild would pass for a project, including
/// framework and SDK references that never appear in <c>project.assets.json</c>.</summary>
/// <remarks>Resolution is best effort: extraction still works with its package-directory
/// fallback when MSBuild cannot evaluate the project (for example before a restore).</remarks>
module ProjectReferences =

    let private tryProperty (node: JsonNode) (name: string) : JsonNode option =
        match node with
        | null -> None
        | :? JsonObject as obj ->
            let mutable value = Unchecked.defaultof<JsonNode>
            if obj.TryGetPropertyValue(name, &value) then Option.ofObj value else None
        | _ -> None

    let private readString (item: JsonNode) name =
        tryProperty item name
        |> Option.map (fun value -> value.GetValue<string>())
        |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let private referencePaths (projectPath: string) =
        let fullPath = Path.GetFullPath projectPath
        let dimensions = MsBuild.evaluate fullPath [ "-getProperty:TargetFramework,TargetFrameworks" ]
        let property name = MsBuild.property dimensions name
        let framework =
            match property "TargetFramework", property "TargetFrameworks" with
            | Some target, _ -> Some target
            | None, Some targets ->
                targets.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.tryHead
            | None, None -> None
        let frameworkArgument = framework |> Option.map (fun value -> $"-property:TargetFramework={value}") |> Option.toList
        let json =
            MsBuild.evaluate
                fullPath
                (frameworkArgument @ [ "-target:ResolveReferences"; "-getItem:ReferencePath" ])

        match tryProperty json "Items" |> Option.bind (fun items -> tryProperty items "ReferencePath") with
        | Some(:? JsonArray as items) ->
            items
            |> Seq.choose (fun item -> readString item "FullPath" |> Option.orElseWith (fun () -> readString item "Identity"))
            |> Seq.filter File.Exists
            |> Seq.distinct
            |> Seq.toList
        | _ -> []

    /// <summary>The directories holding the project's MSBuild-resolved compiler references.</summary>
    let referenceDirectories (projectPath: string) =
        try
            referencePaths projectPath
            |> List.choose (Path.GetDirectoryName >> Option.ofObj)
            |> List.distinct
        with _ -> []
