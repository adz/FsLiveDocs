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

    let private plainDisplay (html: string) =
        html
        |> fun value -> Regex.Replace(value, "<.*?>", String.Empty)
        |> Net.WebUtility.HtmlDecode

    let rec private documentationNodes (nodes: seq<XNode>) =
        [ for node in nodes do
            match node with
            | :? XText as text when not (String.IsNullOrEmpty text.Value) ->
                yield Documentation.text text.Value
            | :? XElement as element ->
                let name = element.Name.LocalName.ToLowerInvariant()
                let children = documentationNodes (element.Nodes())
                let value = element.Value
                let create kind text target language nested = {
                    Kind = kind
                    Text = text
                    Target = target
                    Language = language
                    Children = nested
                }
                match name with
                | "para" -> yield create Paragraph None None None children
                | "c" | "paramref" | "typeparamref" ->
                    let text =
                        if name = "c" then value
                        else element.Attribute(XName.Get "name") |> Option.ofObj |> Option.map _.Value |> Option.defaultValue value
                    yield create InlineCode (Some text) None None []
                | "code" ->
                    let language = element.Attribute(XName.Get "language") |> Option.ofObj |> Option.map _.Value
                    yield create CodeBlock (Some value) None language []
                | "see" | "seealso" ->
                    let target =
                        element.Attribute(XName.Get "cref")
                        |> Option.ofObj
                        |> Option.orElseWith (fun () -> element.Attribute(XName.Get "href") |> Option.ofObj)
                        |> Option.map _.Value
                    let kind =
                        match element.Attribute(XName.Get "href") with
                        | null -> SymbolReference
                        | _ -> ExternalLink
                    yield create kind None target None children
                | "a" ->
                    let target = element.Attribute(XName.Get "href") |> Option.ofObj |> Option.map _.Value
                    yield create ExternalLink None target None children
                | "list" ->
                    let kind =
                        match element.Attribute(XName.Get "type") |> Option.ofObj |> Option.map (_.Value.ToLowerInvariant()) with
                        | Some "number" -> OrderedList
                        | _ -> UnorderedList
                    yield create kind None None None children
                | "item" | "listheader" -> yield create ListItem None None None children
                | "br" -> yield create LineBreak None None None []
                | _ -> yield! children
            | _ -> () ]

    let private documentationSection name (comment: ApiDocComment) =
        match comment.Xml with
        | None -> []
        | Some xml ->
            xml.DescendantsAndSelf()
            |> Seq.tryFind (fun element -> element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun element -> documentationNodes (element.Nodes()))
            |> Option.defaultValue []

    let private parameterDocumentation parameterName (comment: ApiDocComment) =
        match comment.Xml with
        | None -> []
        | Some xml ->
            xml.DescendantsAndSelf()
            |> Seq.tryFind (fun element ->
                element.Name.LocalName.Equals("param", StringComparison.OrdinalIgnoreCase)
                && match element.Attribute(XName.Get "name") with
                   | null -> false
                   | attribute -> attribute.Value = parameterName)
            |> Option.map (fun element -> documentationNodes (element.Nodes()))
            |> Option.defaultValue []

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

    /// <summary>
    /// Reads a deliberate exclusion from verification, mirroring the markdown <c>no-check</c> fence.
    /// </summary>
    /// <remarks>
    /// A reason is required for the same purpose it is required on a fence: an exclusion that
    /// nobody has to justify is indistinguishable from an oversight.
    /// </remarks>
    let private noCheckReason (name: string) (attrs: string) =
        let marker =
            tryGetAttribute "data-livedocs" attrs
            |> Option.filter (fun value -> value.Equals("no-check", StringComparison.OrdinalIgnoreCase))
        match marker with
        | None -> None
        | Some _ ->
            match tryGetAttribute "reason" attrs with
            | Some reason when not (String.IsNullOrWhiteSpace reason) -> Some reason
            | _ -> invalidOp $"Example '{name}' uses data-livedocs=\"no-check\" without a non-empty reason=\"...\"."

    /// <summary>Extracts &lt;example&gt; tags from XML documentation for verification and transclusion.</summary>
    let extractExamples (xmlDoc: string) =
        let pattern = @"<(?<tag>example|code)(?<attrs>[^>]*)>(?<code>.*?)</\k<tag>>"
        let matches = Regex.Matches(xmlDoc, pattern, RegexOptions.Singleline)
        [ for m in matches do
            let tag = m.Groups.["tag"].Value
            let attrs = m.Groups.["attrs"].Value
            let matchedContent = m.Groups.["code"].Value
            let nestedCode =
                if tag.Equals("example", StringComparison.OrdinalIgnoreCase) then
                    Regex.Match(
                        matchedContent,
                        @"^\s*<code(?:\s[^>]*)?>(?<code>.*?)</code>\s*$",
                        RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
                else
                    Match.Empty
            let encodedCode =
                if nestedCode.Success then nestedCode.Groups.["code"].Value
                else matchedContent
            let rawCode = System.Net.WebUtility.HtmlDecode(encodedCode)
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
            let exampleName =
                match tryGetAttribute "name" attrs with
                | Some value when not (String.IsNullOrWhiteSpace value) -> value
                | _ -> "Example"
            yield {
                Name = exampleName
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
                NoCheckReason = noCheckReason exampleName attrs
            }
        ]

    /// <summary>
    /// The name the source gave each parameter, in flattened curried order, or <c>None</c>
    /// where the source gave none.
    /// </summary>
    /// <remarks>
    /// This is the single canonical name source. FSharp.Formatting invents a synthetic name
    /// independently for the usage signature and for the parameter metadata, so the two can
    /// disagree (<c>arg2</c> against <c>arg1</c>); the compiler's own <c>Name</c> is the only
    /// value both must be reconciled against. Matching on synthetic-looking text instead would
    /// misfire on a parameter genuinely named <c>arg1</c>.
    /// </remarks>
    let authoredParameterNames (m: ApiDocMember) : string option list =
        match box m.Symbol with
        | :? FSharpMemberOrFunctionOrValue as mfv ->
            mfv.CurriedParameterGroups |> Seq.collect id |> Seq.map (fun p -> p.Name) |> Seq.toList
        | _ -> []

    /// <summary>
    /// Finds the 1-based positions of public parameters that carry no source-level name,
    /// such as a union destructured directly in the parameter list
    /// (<c>let run (ColdTask operation) = ...</c>).
    /// </summary>
    let unnamedParameterPositions (m: ApiDocMember) : int list =
        match box m.Symbol with
        | :? FSharpMemberOrFunctionOrValue as mfv when not (List.isEmpty m.Parameters) ->
            // Operators and property accessors are positional by nature; there is no name to author.
            let isPositionalByNature =
                mfv.CompiledName.StartsWith("op_", StringComparison.Ordinal)
                || mfv.IsPropertyGetterMethod
                || mfv.IsPropertySetterMethod

            if isPositionalByNature then
                []
            else
                // A unit parameter is unnamed because there is nothing to name, and both
                // renderings agree on `()`.
                let isUnit (p: FSharpParameter) =
                    // `unit` carries no TryFullName, so match the definition's own names.
                    p.Type.HasTypeDefinition
                    && p.Type.TypeDefinition.LogicalName = "unit"

                mfv.CurriedParameterGroups
                |> Seq.collect id
                |> Seq.indexed
                |> Seq.choose (fun (index, p) ->
                    if p.Name.IsNone && not (isUnit p) then Some(index + 1) else None)
                |> Seq.toList
        | _ -> []

    /// <summary>
    /// The name to display for every parameter, in flattened curried order.
    /// </summary>
    /// <remarks>
    /// Prefers the name the source gave. Where the source destructured in place there is no such
    /// name, so the pattern the author actually wrote (<c>ColdTask operation</c>) is shown; it
    /// describes the argument better than any invented identifier and, unlike a name guessed from
    /// the type, it is not a guess. Only when the declaring source cannot be read does this fall
    /// back to FSharp.Formatting's synthetic name.
    /// </remarks>
    let displayParameterNames (m: ApiDocMember) : string list =
        let authored = authoredParameterNames m
        let fromSource =
            match m.Symbol.DeclarationLocation with
            | Some loc ->
                let texts = SourceParameters.parameterTexts loc.FileName loc.StartLine
                // Patterns map to parameters one-for-one only when no group is tupled.
                if texts.Length = m.Parameters.Length then texts else []
            | None -> []

        m.Parameters
        |> List.mapi (fun index p ->
            match authored |> List.tryItem index |> Option.flatten with
            | Some name -> name
            | None ->
                match fromSource |> List.tryItem index with
                | Some text when not (String.IsNullOrWhiteSpace text) -> text.Trim('(', ')', ' ')
                | _ -> p.ParameterNameText)

    /// <summary>
    /// Rewrites the synthetic placeholders FSharp.Formatting put in a usage signature so it names
    /// each argument exactly as the parameter table does.
    /// </summary>
    let reconcileUsageSignature (replacements: string list) (usage: string) =
        if String.IsNullOrWhiteSpace usage then
            usage
        else
            // FSharp.Formatting numbers its placeholders independently of the parameter metadata,
            // so the k-th placeholder left-to-right is matched to the k-th unnamed parameter
            // rather than trusting the number it carries.
            let replaceable = replacements
            let mutable next = 0
            Regex.Replace(
                usage,
                @"\barg\d+\b",
                fun _ ->
                    let replacement =
                        match replaceable |> List.tryItem next with
                        | Some name when name.Contains(" ") -> "(" + name + ")"
                        | Some name -> name
                        | None -> "arg" + string (next + 1)
                    next <- next + 1
                    replacement)

    /// <summary>The usage signature with every synthetic placeholder replaced by its display name.</summary>
    let mapMemberSignature (m: ApiDocMember) =
        let authored = authoredParameterNames m
        let unnamedDisplayNames =
            displayParameterNames m
            |> List.mapi (fun index name ->
                match authored |> List.tryItem index |> Option.flatten with
                | Some _ -> None
                | None -> Some name)
            |> List.choose id

        m.UsageHtml.HtmlText |> reconcileUsageSignature unnamedDisplayNames |> plainDisplay

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

        // One canonical name per argument, used for both the signature and the table so the two
        // cannot disagree.
        let displayNames = displayParameterNames m

        {
            Id = m.Symbol.FullName
            Name = m.Name
            Signature = mapMemberSignature m
            Parameters =
                m.Parameters
                |> List.mapi (fun index p -> {
                    Name = displayNames |> List.tryItem index |> Option.defaultValue p.ParameterNameText
                    Type = p.ParameterType.HtmlText |> plainDisplay
                    Description = parameterDocumentation p.ParameterNameText m.Comment
                })
            ReturnType = m.ReturnInfo.ReturnType |> Option.map (fun (_, h) -> plainDisplay h.HtmlText) |> Option.defaultValue "unit"
            Summary = documentationSection "summary" m.Comment
            Remarks = documentationSection "remarks" m.Comment
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
            Summary = documentationSection "summary" e.Comment
            Members = members
            Examples = e.Comment |> rawXml |> extractExamples
            Entities = nested
        }

    /// <summary>
    /// Synthetic placeholders the rendered usage signature shows in place of a parameter the
    /// table names differently.
    /// </summary>
    let signatureNameMismatches (m: ApiDocMember) : string list =
        // Only a usage signature that actually renders parameter names can contradict the table.
        // For .NET-style members FSharp.Formatting renders the member alone (`this.Bind`), naming
        // nothing, so there is nothing to disagree with. What must never appear is a synthetic
        // placeholder standing in for a parameter the table names differently.
        // Checked against the signature as rendered, after placeholders have been reconciled.
        let usage = mapMemberSignature m
        if String.IsNullOrWhiteSpace usage then
            []
        else
            let canonical = displayParameterNames m |> Set.ofList
            Regex.Matches(usage, @"\barg\d+\b")
            |> Seq.map (fun placeholder -> placeholder.Value)
            |> Seq.filter (fun placeholder -> not (canonical.Contains placeholder))
            |> Seq.distinct
            |> Seq.toList

    /// <summary>Reports every parameter-naming problem reachable from an entity.</summary>
    let rec parameterDiagnostics (e: ApiDocEntity) : ApiDiagnostic list =
        let locationOf (m: ApiDocMember) =
            match m.Symbol.DeclarationLocation with
            | Some loc -> { File = loc.FileName; Line = loc.StartLine }
            | None -> { File = ""; Line = 0 }

        let describeUnnamed (m: ApiDocMember) =
            // An unnamed parameter is only a problem when its pattern could not be recovered from
            // source; otherwise it is shown as the author wrote it and needs no attention.
            let displayNames = displayParameterNames m
            let unrecovered =
                unnamedParameterPositions m
                |> List.filter (fun position ->
                    match displayNames |> List.tryItem (position - 1) with
                    | Some name -> Regex.IsMatch(name, @"^arg\d+$")
                    | None -> true)

            match unrecovered with
            | [] -> None
            | positions ->
                let which = positions |> List.map string |> String.concat ", "
                let subject =
                    if positions.Length = 1 then $"The parameter at position {which} is"
                    else $"The parameters at positions {which} are"
                Some {
                    Code = "unnamed-parameter"
                    Symbol = m.Symbol.FullName
                    Location = locationOf m
                    Message = $"{subject} shown under a generated name, because no name could be read from the declaration."
                    Remedy = "Naming the parameter in the declaration would let the documentation use that name instead."
                }

        let describeMismatch (m: ApiDocMember) =
            match signatureNameMismatches m with
            | [] -> None
            | placeholders ->
                let listed = String.concat ", " placeholders
                Some {
                    Code = "signature-name-mismatch"
                    Symbol = m.Symbol.FullName
                    Location = locationOf m
                    Message = $"The rendered signature shows {listed}, which the parameter table names differently."
                    Remedy = "Name every public parameter so both renderings resolve to the same name."
                }

        [ yield! e.AllMembers |> Seq.choose describeUnnamed
          yield! e.AllMembers |> Seq.choose describeMismatch
          yield! e.NestedEntities |> List.collect parameterDiagnostics ]

    let private isSyntheticDefaultNamespace (e: EntityModel) =
        e.Kind = EntityKind.Namespace
        && e.Name.Equals("Default", StringComparison.OrdinalIgnoreCase)
        && Documentation.isEmpty e.Summary
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
                        Summary = []
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
                    Summary =
                        group
                        |> List.map (fun e -> e.Summary)
                        |> List.filter (Documentation.isEmpty >> not)
                        |> List.tryHead 
                        |> Option.defaultValue []
                    Members = group |> List.collect (fun e -> e.Members) |> List.distinctBy (fun m -> m.Id)
                    Examples = group |> List.collect entityExamples |> List.distinctBy (fun ex -> ex.Name)
                    Entities = mergeEntities (group |> List.collect (fun e -> e.Entities))
                }
            )

        buildTree "" entities

    /// <summary>
    /// Scans a project file and extracts all documented symbols using FSharp.Formatting,
    /// together with any parameter-naming problems found in the API itself.
    /// </summary>
    let extractFromProjectWithDiagnostics (projectPath: string) = async {
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
            return { Version = "0.1.0"; Entities = []; Scenarios = []; Packages = [] }, []
        else
            // FSharp.Formatting REQUIRES the .xml file to be next to the .dll
            let xmlPath = Path.ChangeExtension(dllPath, ".xml")
            if not (File.Exists xmlPath) then
                printfn "Warning: Skipping project %s because associated XML file was not found at %s" projName xmlPath
                return { Version = "0.1.0"; Entities = []; Scenarios = []; Packages = [] }, []
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

                let projectEntities =
                    model.EntityInfos
                    |> Seq.filter (fun ei -> isProjectEntity ei.Entity)
                    |> Seq.map (fun ei -> ei.Entity)
                    |> Seq.toList

                let diagnostics = projectEntities |> List.collect parameterDiagnostics

                let entities = projectEntities |> List.collect (flatten >> List.ofSeq)

                return {
                    Version = "0.1.0"
                    Entities = entities
                    Scenarios = extractScenariosFromAssembly dllPath
                    Packages = [ { Name = assemblyName; EntityIds = entities |> List.map (fun entity -> entity.Id) |> List.distinct } ]
                }, diagnostics
    }

    /// <summary>Scans a project file and extracts all documented symbols using FSharp.Formatting.</summary>
    let extractFromProject (projectPath: string) = async {
        let! package, _ = extractFromProjectWithDiagnostics projectPath
        return package
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
