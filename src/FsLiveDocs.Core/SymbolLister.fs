namespace FsLiveDocs.Core

open System
open System.IO
open FSharp.Formatting.ApiDocs
open FSharp.Formatting.Templating
open FSharp.Compiler.Symbols
open System.Text.RegularExpressions
open System.Xml.Linq
open System.Reflection
open System.Text.Json

/// <summary>Provides capabilities to scan F# projects and extract symbols using FSharp.Formatting.</summary>
/// <example name="ExtractExamplesExample" data-livedocs="snapshot">
/// > SymbolLister.extractExamples "<summary>No examples here</summary>";;
/// val it: ExampleModel list = []
/// </example>
module SymbolLister =

    let private ancestors directory =
        let rec loop current =
            seq {
                if not (String.IsNullOrWhiteSpace current) then
                    yield current
                    let parent = Directory.GetParent(current)
                    if not (isNull parent) then yield! loop parent.FullName
            }
        loop directory

    let private rawXml (comment: ApiDocComment) =
        match comment.Xml with
        | Some xml -> xml.ToString(SaveOptions.DisableFormatting)
        | None -> ""

    let private tryGetAttribute (name: string) (attrs: string) =
        let pattern = $"\\b{name}\\s*=\\s*\"(?<value>[^\"]*)\""
        let matchResult = Regex.Match(attrs, pattern, RegexOptions.IgnoreCase)
        if matchResult.Success then Some matchResult.Groups.["value"].Value else None

    let private hasSnapshotMarker (attrs: string) =
        match tryGetAttribute "data-livedocs" attrs |> Option.orElseWith (fun () -> tryGetAttribute "test" attrs) with
        | Some value when value.Equals("snapshot", StringComparison.OrdinalIgnoreCase) -> true
        | Some value when value.Equals("true", StringComparison.OrdinalIgnoreCase) -> true
        | _ ->
            match tryGetAttribute "snapshot" attrs with
            | Some value when value.Equals("true", StringComparison.OrdinalIgnoreCase) -> true
            | _ -> false

    /// <summary>Extracts &lt;example&gt; tags from XML documentation for verification and transclusion.</summary>
    let extractExamples (xmlDoc: string) =
        let pattern = @"<(?<tag>example|code)(?<attrs>[^>]*)>(?<code>.*?)</\k<tag>>"
        let matches = Regex.Matches(xmlDoc, pattern, RegexOptions.Singleline)
        [ for m in matches do
            let tag = m.Groups.["tag"].Value
            let attrs = m.Groups.["attrs"].Value
            let rawCode = System.Net.WebUtility.HtmlDecode(m.Groups.["code"].Value)
            let parsed = ExampleTranscript.parse rawCode
            let explicitSnapshot = hasSnapshotMarker attrs
            let language =
                tryGetAttribute "language" attrs
                |> Option.orElseWith (fun () -> tryGetAttribute "lang" attrs)
            let isFSharpCode =
                match language with
                | Some lang when lang.Equals("fsharp", StringComparison.OrdinalIgnoreCase)
                                 || lang.Equals("fs", StringComparison.OrdinalIgnoreCase) -> true
                | _ -> false
            let transcriptMarker =
                tag.Equals("example", StringComparison.OrdinalIgnoreCase)
                && (rawCode.Contains("> ") || rawCode.Contains("- "))
            yield { 
                Name =
                    match tryGetAttribute "name" attrs with
                    | Some value when not (String.IsNullOrWhiteSpace value) -> value
                    | _ -> "Example"
                Content = parsed.DisplayText
                ExpectedOutput = parsed.ExpectedOutput
                Scenario =
                    match tryGetAttribute "scenario" attrs with
                    | Some value when not (String.IsNullOrWhiteSpace value) -> Some value
                    | _ -> None
                IsSnapshotTest =
                    transcriptMarker
                    || (tag.Equals("example", StringComparison.OrdinalIgnoreCase) && explicitSnapshot)
                    || (tag.Equals("code", StringComparison.OrdinalIgnoreCase) && explicitSnapshot && isFSharpCode)
            }
        ]

    let mapMember (m: ApiDocMember) : MemberModel =
        let location = 
            match m.Symbol.DeclarationLocation with
            | Some loc ->
                let file =
                    if String.IsNullOrWhiteSpace loc.FileName then ""
                    else
                        let relative =
                            if Path.IsPathRooted loc.FileName then
                                Path.GetRelativePath(Directory.GetCurrentDirectory(), loc.FileName)
                            else loc.FileName
                        relative.Replace('\\', '/')
                { File = file; Line = loc.StartLine }
            | None -> { File = ""; Line = 0 }

        {
            Id = m.Symbol.FullName
            Name = m.Name
            Signature = m.UsageHtml.HtmlText // usage is often better for members
            Parameters = 
                m.Parameters 
                |> List.map (fun p -> { 
                    Name = p.ParameterNameText
                    Type = p.ParameterType.HtmlText
                    DescriptionHtml = p.ParameterDocs |> Option.map (fun d -> d.HtmlText) |> Option.defaultValue "" 
                })
            ReturnType = m.ReturnInfo.ReturnType |> Option.map (fun (_, h) -> h.HtmlText) |> Option.defaultValue "unit"
            SummaryHtml = m.Comment.Summary.HtmlText
            RemarksHtml = m.Comment.Remarks |> Option.map (fun r -> r.HtmlText) |> Option.defaultValue ""
            Examples = m.Comment |> rawXml |> extractExamples
            Location = location
        }

    let rec mapEntity (e: ApiDocEntity) : EntityModel =
        let members = e.AllMembers |> Seq.map mapMember |> Seq.toList
        let nested = e.NestedEntities |> List.map mapEntity
        let entityId =
            e.Symbol.TryFullName
            |> Option.defaultWith (fun () ->
                if String.IsNullOrWhiteSpace e.Symbol.AccessPath then e.Symbol.CompiledName
                else e.Symbol.AccessPath + "." + e.Symbol.CompiledName)

        {
            Id = entityId
            Name = e.Name
            Kind =
                if e.Symbol.IsFSharpModule then EntityKind.Module
                elif e.Symbol.IsNamespace then EntityKind.Namespace
                elif e.Symbol.IsFSharpRecord then EntityKind.Record
                elif e.Symbol.IsFSharpUnion then EntityKind.Union
                else EntityKind.Type
            SummaryHtml = e.Comment.Summary.HtmlText
            Members = members
            Examples = e.Comment |> rawXml |> extractExamples
            Entities = nested
        }

    let private isSyntheticDefaultNamespace (e: EntityModel) =
        e.Kind = EntityKind.Namespace
        && e.Name.Equals("Default", StringComparison.OrdinalIgnoreCase)
        && String.IsNullOrWhiteSpace e.SummaryHtml
        && e.Members.IsEmpty

    let rec private pruneSyntheticDefaults (entities: EntityModel list) =
        entities
        |> List.collect (fun e ->
            let prunedChildren = pruneSyntheticDefaults e.Entities
            let pruned = { e with Entities = prunedChildren }
            if isSyntheticDefaultNamespace pruned then pruned.Entities else [ pruned ])

    let private entityExamples (e: EntityModel) =
        if isNull (box e.Examples) then [] else e.Examples

    let private getAssemblyName (projectPath: string) =
        try
            let project = XDocument.Load(projectPath)
            let assemblyName =
                project.Descendants(XName.Get "AssemblyName")
                |> Seq.tryPick (fun e ->
                    let value = e.Value.Trim()
                    if String.IsNullOrWhiteSpace value then None else Some value)

            assemblyName |> Option.defaultValue (Path.GetFileNameWithoutExtension(projectPath))
        with _ ->
            Path.GetFileNameWithoutExtension(projectPath)

    let private getPackageReferenceDirectories (projectPath: string) =
        let projectDir = Path.GetDirectoryName(projectPath)
        let projectName = Path.GetFileNameWithoutExtension(projectPath)
        let sharedAssets =
            ancestors projectDir
            |> Seq.map (fun root -> Path.Combine(root, "artifacts", "obj", projectName, "project.assets.json"))
            |> Seq.tryFind File.Exists
            |> Option.defaultValue ""
        let localAssets = Path.Combine(projectDir, "obj", "project.assets.json")

        [ sharedAssets; localAssets ]
        |> List.tryFind File.Exists
        |> Option.map (fun assetsPath ->
            use document = JsonDocument.Parse(File.ReadAllText(assetsPath))
            let root = document.RootElement
            let packageRoots =
                root.GetProperty("packageFolders").EnumerateObject()
                |> Seq.map (fun property -> property.Name)
                |> Seq.toList

            root.GetProperty("targets").EnumerateObject()
            |> Seq.collect (fun target -> target.Value.EnumerateObject())
            |> Seq.collect (fun library ->
                let parts = library.Name.Split('/')
                let mutable compile = Unchecked.defaultof<JsonElement>
                if parts.Length = 2 && library.Value.TryGetProperty("compile", &compile) then
                    compile.EnumerateObject()
                    |> Seq.collect (fun asset ->
                        if asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) then
                            packageRoots
                            |> Seq.map (fun packageRoot ->
                                Path.Combine(packageRoot, parts.[0].ToLowerInvariant(), parts.[1], Path.GetDirectoryName(asset.Name)))
                        else Seq.empty)
                else Seq.empty)
            |> Seq.filter Directory.Exists
            |> Seq.distinct
            |> Seq.toList)
        |> Option.defaultValue []

    let private extractScenariosFromAssembly (dllPath: string) =
        let scenarioAttributeName = "FsLiveDocs.Core.DocScenarioAttribute"
        let assembly = Assembly.LoadFrom(dllPath)
        let types =
            try
                assembly.GetTypes() |> Array.toList
            with :? ReflectionTypeLoadException as ex ->
                ex.Types |> Array.choose (fun t -> if isNull t then None else Some t) |> Array.toList

        types
        |> List.collect (fun t ->
            try
                t.GetMethods(BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
                |> Array.toList
                |> List.choose (fun m ->
                    try
                        m.GetCustomAttributesData()
                        |> Seq.tryFind (fun cad -> cad.AttributeType.FullName = scenarioAttributeName)
                        |> Option.bind (fun cad ->
                            let scenarioName =
                                cad.ConstructorArguments
                                |> Seq.tryHead
                                |> Option.map (fun arg -> string arg.Value)
                                |> Option.defaultValue ""

                            if String.IsNullOrWhiteSpace scenarioName then None
                            else
                                let typeName =
                                    if String.IsNullOrWhiteSpace t.FullName then t.Name else t.FullName

                                Some {
                                    Name = scenarioName
                                    MethodId = $"{typeName.Replace('+', '.')}.{m.Name}"
                                })
                    with
                    | :? FileNotFoundException
                    | :? FileLoadException
                    | :? TypeLoadException -> None)
            with
            | :? FileNotFoundException
            | :? FileLoadException
            | :? TypeLoadException -> [])
        |> List.distinctBy (fun s -> s.Name)

    /// <summary>Groups flattened entities into a hierarchical tree based on their IDs.</summary>
    let reconstructHierarchy (entities: EntityModel list) =
        let rec buildTree (currentPath: string) (available: EntityModel list) =
            available
            |> List.groupBy (fun e -> 
                let relativeId = if String.IsNullOrEmpty currentPath then e.Id else e.Id.Substring(currentPath.Length + 1)
                let parts = relativeId.Split('.')
                parts.[0]
            )
            |> List.map (fun (name, group) ->
                let fullId = if String.IsNullOrEmpty currentPath then name else currentPath + "." + name
                // If there's an exact match for this ID, it's our current entity
                let currentEntity = group |> List.tryFind (fun e -> e.Id = fullId)
                
                // Other entities in the group are children/descendants
                let descendants = group |> List.filter (fun e -> e.Id <> fullId)
                let children = buildTree fullId descendants

                match currentEntity with
                | Some e -> { e with Entities = mergeEntities (e.Entities @ children) }
                | None -> 
                    { 
                        Id = fullId
                        Name = name
                        Kind = EntityKind.Namespace
                        SummaryHtml = ""
                        Members = []
                        Examples = []
                        Entities = children 
                    }
            )
        and mergeEntities (entities: EntityModel list) =
            entities
            |> List.groupBy (fun e -> e.Id)
            |> List.map (fun (id, group) ->
                let first = List.head group
                {
                    Id = id
                    Name = first.Name
                    Kind = first.Kind
                    SummaryHtml = 
                        group 
                        |> List.map (fun e -> e.SummaryHtml) 
                        |> List.filter (not << String.IsNullOrWhiteSpace) 
                        |> List.tryHead 
                        |> Option.defaultValue ""
                    Members = group |> List.collect (fun e -> e.Members) |> List.distinctBy (fun m -> m.Id)
                    Examples = group |> List.collect entityExamples |> List.distinctBy (fun ex -> ex.Name)
                    Entities = mergeEntities (group |> List.collect (fun e -> e.Entities))
                }
            )

        buildTree "" entities

    /// <summary>Scans a project file and extracts all documented symbols using FSharp.Formatting.</summary>
    let extractFromProject (projectPath: string) = async {
        // ApiDocs needs the DLL and XML to be built first.
        let projName = Path.GetFileNameWithoutExtension(projectPath)
        let assemblyName = getAssemblyName projectPath
        let projDir = Path.GetDirectoryName(projectPath)
        
        let searchPaths =
            [ yield Path.Combine(projDir, "bin")
              for root in ancestors projDir do
                  yield Path.Combine(root, "artifacts", "bin", projName)
                  yield Path.Combine(root, "artifacts", "bin") ]
            |> List.distinct

        let dllPath = 
            searchPaths 
            |> List.filter Directory.Exists
            |> List.collect (fun path ->
                Directory.GetFiles(path, $"{assemblyName}.dll", SearchOption.AllDirectories)
                |> Array.filter (fun dll -> File.Exists(Path.ChangeExtension(dll, ".xml")))
                |> Array.toList)
            |> List.distinct
            |> List.sortByDescending File.GetLastWriteTimeUtc
            |> List.tryHead
            |> Option.defaultValue ""

        if String.IsNullOrEmpty dllPath || not (File.Exists dllPath) then
            return { Version = "0.1.0"; Entities = []; Scenarios = []; Packages = [] }
        else
            // FSharp.Formatting REQUIRES the .xml file to be next to the .dll
            let xmlPath = Path.ChangeExtension(dllPath, ".xml")
            if not (File.Exists xmlPath) then
                printfn "Warning: Skipping project %s because associated XML file was not found at %s" projName xmlPath
                return { Version = "0.1.0"; Entities = []; Scenarios = []; Packages = [] }
            else
                let input = ApiDocInput.FromFile(dllPath)
                let libDirs = 
                    [ 
                        Path.GetDirectoryName(dllPath)
                        System.AppContext.BaseDirectory
                    ]
                    @ getPackageReferenceDirectories projectPath
                    |> List.distinct
                
                let model = 
                    let oldOut = Console.Out
                    try
                        using (new StringWriter()) (fun sw -> 
                            Console.SetOut(sw)
                            ApiDocs.GenerateModel([input], "Project", Substitutions.Empty, qualify=false, libDirs = libDirs)
                        )
                    finally
                        Console.SetOut(oldOut)
                
                let rec flatten (e: ApiDocEntity) =
                    seq {
                        yield mapEntity e
                        for n in e.NestedEntities do
                            yield! flatten n
                    }

                let isProjectEntity (e: ApiDocEntity) =
                    e.Symbol.Assembly.SimpleName.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)

                let entities =
                    model.EntityInfos 
                    |> Seq.filter (fun ei -> isProjectEntity ei.Entity)
                    |> Seq.collect (fun ei -> flatten ei.Entity)
                    |> Seq.toList

                return {
                    Version = "0.1.0"
                    Entities = entities
                    Scenarios = extractScenariosFromAssembly dllPath
                    Packages = [ { Name = assemblyName; EntityIds = entities |> List.map (fun entity -> entity.Id) |> List.distinct } ]
                }
    }

    /// <summary>Merges multiple PackageModels into a single unified documentation model and reconstructs hierarchy.</summary>
    let merge (packages: PackageModel list) =
        if packages.IsEmpty then 
            { Version = "0.1.0"; Entities = []; Scenarios = []; Packages = [] }
        else
            let allFlatEntities = packages |> List.collect (fun p -> p.Entities) |> List.distinctBy (fun e -> e.Id)
            let allScenarios = packages |> List.collect (fun p -> p.Scenarios) |> List.distinctBy (fun s -> s.Name)
            let packageInfo =
                packages
                |> List.collect (fun p -> if isNull (box p.Packages) then [] else p.Packages)
                |> List.distinctBy (fun p -> p.Name)
            let hierarchical = reconstructHierarchy allFlatEntities |> pruneSyntheticDefaults
            { Version = (packages |> List.head).Version
              Entities = hierarchical
              Scenarios = allScenarios
              Packages = packageInfo }
