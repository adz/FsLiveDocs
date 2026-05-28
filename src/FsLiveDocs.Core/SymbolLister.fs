namespace FsLiveDocs.Core

open System
open System.IO
open FSharp.Formatting.ApiDocs
open FSharp.Formatting.Templating
open FSharp.Compiler.Symbols
open System.Text.RegularExpressions
open System.Xml.Linq
open System.Reflection

/// <summary>Provides capabilities to scan F# projects and extract symbols using FSharp.Formatting.</summary>
/// <example name="ExtractExamplesExample">
/// let examples = SymbolLister.extractExamples "EXAMPLE"
/// printfn "COUNT: %d" examples.Length
/// // EXPECTED: COUNT: 0
/// </example>
module SymbolLister =

    let private rawXml (comment: ApiDocComment) =
        match comment.Xml with
        | Some xml -> xml.ToString(SaveOptions.DisableFormatting)
        | None -> ""

    /// <summary>Extracts &lt;example&gt; tags from XML documentation for verification and transclusion.</summary>
    let extractExamples (xmlDoc: string) =
        let pattern = @"<example(?:\s+name=""(?<name>[^""]+)"")?(?:\s+scenario=""(?<scenario>[^""]+)"")?>(?<code>.*?)<\/example>"
        let matches = Regex.Matches(xmlDoc, pattern, RegexOptions.Singleline)
        [ for m in matches do
            let rawCode = m.Groups.["code"].Value
            let lines = rawCode.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            let nonEmpty = lines |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
            
            let normalizedContent =
                if nonEmpty.Length = 0 then ""
                else
                    let minIndent =
                        nonEmpty
                        |> Array.map (fun line -> line.Length - line.TrimStart().Length)
                        |> Array.fold min Int32.MaxValue
                    
                    lines
                    |> Array.map (fun line ->
                        if line.Length >= minIndent then line.Substring(minIndent)
                        else line.TrimStart())
                    |> String.concat "\n"
                    |> fun s -> s.Trim()

            let parts = normalizedContent.Split([| "// EXPECTED:" |], StringSplitOptions.None)
            let content = parts.[0].Trim()
            let expected = if parts.Length > 1 then Some (parts.[1].Trim()) else None
            yield { 
                Name = if m.Groups.["name"].Success then m.Groups.["name"].Value else "Example"
                Content = content
                ExpectedOutput = expected
                Scenario = if m.Groups.["scenario"].Success then Some m.Groups.["scenario"].Value else None
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

        {
            Id = e.Symbol.FullName
            Name = e.Name
            Kind = 
                if e.Symbol.IsFSharpModule then "Module"
                elif e.Symbol.IsNamespace then "Namespace"
                elif e.Symbol.IsFSharpRecord then "Record"
                elif e.Symbol.IsFSharpUnion then "Union"
                else "Type"
            SummaryHtml = e.Comment.Summary.HtmlText
            Members = members
            Examples = e.Comment |> rawXml |> extractExamples
            Entities = nested
        }

    let private isSyntheticDefaultNamespace (e: EntityModel) =
        e.Kind = "Namespace"
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
            t.GetMethods(BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
            |> Array.toList
            |> List.choose (fun m ->
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
                        })))
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
                        Kind = "Namespace"
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
        
        let searchPaths = [
            Path.Combine(projDir, "../../artifacts/bin")
            Path.Combine(projDir, "bin/Debug/net10.0")
            Path.Combine(projDir, "bin/Release/net10.0")
        ]

        let dllPath = 
            searchPaths 
            |> List.filter Directory.Exists
            |> List.tryPick (fun path ->
                let files = Directory.GetFiles(path, $"{assemblyName}.dll", SearchOption.AllDirectories)
                if files.Length > 0 then Some files.[0] else None
            )
            |> Option.defaultValue ""

        if String.IsNullOrEmpty dllPath || not (File.Exists dllPath) then
            return { Version = "0.1.0"; Entities = []; Scenarios = [] }
        else
            // FSharp.Formatting REQUIRES the .xml file to be next to the .dll
            let xmlPath = Path.ChangeExtension(dllPath, ".xml")
            if not (File.Exists xmlPath) then
                printfn "Warning: Skipping project %s because associated XML file was not found at %s" projName xmlPath
                return { Version = "0.1.0"; Entities = []; Scenarios = [] }
            else
                let input = ApiDocInput.FromFile(dllPath)
                let libDirs = 
                    [ 
                        Path.GetDirectoryName(dllPath)
                        System.AppContext.BaseDirectory
                    ] |> List.distinct
                
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

                return { Version = "0.1.0"; Entities = entities; Scenarios = extractScenariosFromAssembly dllPath }
    }

    /// <summary>Merges multiple PackageModels into a single unified documentation model and reconstructs hierarchy.</summary>
    let merge (packages: PackageModel list) =
        if packages.IsEmpty then 
            { Version = "0.1.0"; Entities = []; Scenarios = [] }
        else
            let allFlatEntities = packages |> List.collect (fun p -> p.Entities) |> List.distinctBy (fun e -> e.Id)
            let allScenarios = packages |> List.collect (fun p -> p.Scenarios) |> List.distinctBy (fun s -> s.Name)
            let hierarchical = reconstructHierarchy allFlatEntities |> pruneSyntheticDefaults
            { Version = (packages |> List.head).Version
              Entities = hierarchical
              Scenarios = allScenarios }
