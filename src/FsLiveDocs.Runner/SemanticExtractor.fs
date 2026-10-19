namespace FsLiveDocs.Runner

open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq
open Microsoft.FSharp.Reflection
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FsLiveDocs.Core

/// Converts compiler symbols immediately into renderer-neutral FsLiveDocs records.
module SemanticExtractor =

    let private normalizeDocumentation (value: string) =
        Regex.Replace(value, @"\s+", " ").Trim()

    let private xmlDocumentation = Collections.Concurrent.ConcurrentDictionary<string, Lazy<Map<string, string>>>()

    let private readXmlMember xmlPath signature =
        if String.IsNullOrWhiteSpace xmlPath || String.IsNullOrWhiteSpace signature || not (File.Exists xmlPath) then None
        else
            try
                // Tooltip extraction asks for many symbols from the same assembly. Index one XML
                // file once per file version instead of reparsing it for every symbol use.
                let fullPath = Path.GetFullPath xmlPath
                let cacheKey = fullPath + "|" + string (File.GetLastWriteTimeUtc(fullPath).Ticks)
                let members =
                    xmlDocumentation.GetOrAdd(cacheKey, fun _ -> lazy (
                        let document = XDocument.Load(fullPath)
                        document.Descendants(XName.Get "member")
                        |> Seq.choose (fun memberElement ->
                            match memberElement.Attribute(XName.Get "name") with
                            | null -> None
                            | attribute ->
                                let documentation =
                                    memberElement.Elements()
                                    |> Seq.map (fun element -> element.Value |> normalizeDocumentation)
                                    |> Seq.filter (String.IsNullOrWhiteSpace >> not)
                                    |> String.concat "\n"
                                if String.IsNullOrWhiteSpace documentation then None
                                else Some(attribute.Value, documentation))
                        |> Map.ofSeq)).Value
                Map.tryFind signature members
            with _ -> None

    /// FCS deliberately models XML docs as a union. Reflection here keeps this boundary tolerant of additive FCS changes.
    let private documentationFromXmlDoc signature (xmlDoc: FSharpXmlDoc) =
        let case, fields = FSharpValue.GetUnionFields(xmlDoc, typeof<FSharpXmlDoc>)
        match case.Name with
        | "FromXmlText" ->
            fields
            |> Array.collect (function
                | :? (string array) as lines -> lines
                | :? string as line -> [| line |]
                | _ -> [||])
            |> String.concat "\n"
            |> normalizeDocumentation
            |> Option.ofObj
            |> Option.filter (String.IsNullOrWhiteSpace >> not)
        | "FromXmlFile" ->
            fields
            |> Array.choose (function :? string as value -> Some value | _ -> None)
            |> Array.tryFind (fun value -> value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            |> Option.bind (fun path -> readXmlMember path signature)
        | _ -> None

    let private symbolDocumentation (symbol: FSharpSymbol) =
        let signatureAndDoc =
            match symbol with
            | :? FSharpMemberOrFunctionOrValue as value -> Some(value.XmlDocSig, value.XmlDoc)
            | :? FSharpEntity as entity -> Some(entity.XmlDocSig, entity.XmlDoc)
            | :? FSharpUnionCase as unionCase -> Some(unionCase.XmlDocSig, unionCase.XmlDoc)
            | :? FSharpField as field -> Some(field.XmlDocSig, field.XmlDoc)
            | _ -> None
        signatureAndDoc
        |> Option.bind (fun (signature, xmlDoc) ->
            documentationFromXmlDoc signature xmlDoc
            |> Option.orElseWith (fun () ->
                symbol.Assembly.FileName
                |> Option.bind (fun assemblyPath -> readXmlMember (Path.ChangeExtension(assemblyPath, ".xml")) signature)))

    let private symbolKind (symbol: FSharpSymbol) =
        match symbol with
        | :? FSharpEntity as entity when entity.IsNamespace -> SemanticTokenKind.Namespace
        | :? FSharpEntity as entity when entity.IsFSharpModule -> SemanticTokenKind.Module
        | :? FSharpEntity -> SemanticTokenKind.TypeName
        | :? FSharpUnionCase -> SemanticTokenKind.UnionCase
        | :? FSharpActivePatternCase -> SemanticTokenKind.ActivePatternCase
        | :? FSharpMemberOrFunctionOrValue as value when value.IsProperty -> SemanticTokenKind.Property
        | :? FSharpMemberOrFunctionOrValue as value when value.CurriedParameterGroups.Count > 0 -> SemanticTokenKind.Function
        | :? FSharpMemberOrFunctionOrValue -> SemanticTokenKind.Identifier
        | :? FSharpField -> SemanticTokenKind.Property
        | _ -> SemanticTokenKind.Identifier

    let private symbolSignature (use': FSharpSymbolUse) =
        try
            match use'.Symbol with
            | :? FSharpMemberOrFunctionOrValue as value ->
                Some $"{value.DisplayName}: {value.FullType.Format(use'.DisplayContext)}"
            | :? FSharpEntity as entity -> entity.TryFullName |> Option.orElse (Some entity.DisplayName)
            | :? FSharpUnionCase as unionCase -> Some unionCase.DisplayName
            | :? FSharpField as field -> Some $"{field.DisplayName}: {field.FieldType.Format(use'.DisplayContext)}"
            | symbol -> Some symbol.DisplayName
        with _ -> Some use'.Symbol.DisplayName

    let private lexicalKind (text: string) =
        let keywords =
            set [ "abstract"; "and"; "as"; "assert"; "base"; "begin"; "class"; "default"; "delegate"; "do"; "done"; "downcast"; "downto"; "elif"; "else"; "end"; "exception"; "extern"; "false"; "finally"; "fixed"; "for"; "fun"; "function"; "global"; "if"; "in"; "inherit"; "inline"; "interface"; "internal"; "lazy"; "let"; "match"; "member"; "module"; "mutable"; "namespace"; "new"; "null"; "of"; "open"; "or"; "override"; "private"; "public"; "rec"; "return"; "return!"; "select"; "static"; "struct"; "then"; "to"; "true"; "try"; "type"; "upcast"; "use"; "use!"; "val"; "void"; "when"; "while"; "with"; "yield"; "yield!" ]
        if String.IsNullOrWhiteSpace text then PlainText
        elif text.StartsWith("//") then Comment
        elif text.StartsWith("\"") || text.StartsWith("@\"") then String
        elif Char.IsDigit text.[0] then Number
        elif keywords.Contains text then Keyword
        elif Regex.IsMatch(text, @"^[A-Za-z_'\p{L}][\w'\p{L}]*$") then Identifier
        elif Regex.IsMatch(text, @"^[!%&*+\-./<=>?@^|~:]+$") then Operator
        else Punctuation

    let private tokenPattern = Regex("""//.*$|@?"(?:""|\\.|[^"])*"|\d+(?:\.\d+)?|[A-Za-z_'\p{L}][\w'\p{L}]*|[!%&*+\-./<=>?@^|~:]+|\s+|.""", RegexOptions.Compiled)

    /// The fully-qualified F# name for a documented symbol, or None for locals, synthetic
    /// generated modules, and compiler infrastructure.
    let private symbolLink (symbol: FSharpSymbol) =
        let qualified (name: string) =
            not (String.IsNullOrWhiteSpace name)
            && name.Contains('.')
            && not (name.StartsWith("FsLiveDocsGeneratedPage", StringComparison.Ordinal))
            && not (name.Contains(".md", StringComparison.OrdinalIgnoreCase))
            && not (name.Contains(".fsx", StringComparison.OrdinalIgnoreCase))
            && not (name.Contains(".fs", StringComparison.OrdinalIgnoreCase))

        match symbol with
        | :? FSharpMemberOrFunctionOrValue as value ->
            let name =
                match value.DeclaringEntity with
                | Some owner ->
                    let ownerName =
                        match owner.TryFullName with
                        | Some full when full.EndsWith("Module", System.StringComparison.Ordinal) ->
                            Some(full.Substring(0, full.Length - "Module".Length))
                        | other -> other

                    match ownerName with
                    | Some full when not (System.String.IsNullOrWhiteSpace full) && not (System.String.IsNullOrWhiteSpace value.DisplayName) ->
                        full + "." + value.DisplayName
                    | _ -> value.FullName
                | None -> value.FullName
            if qualified name then Some name else None
        | :? FSharpEntity as entity ->
            entity.TryFullName |> Option.filter qualified
        | _ -> None

    let private buildTooltip (use': FSharpSymbolUse) =
        {
            Signature = symbolSignature use'
            Documentation = symbolDocumentation use'.Symbol
            Sections = []
            // Assembly ownership is compiler implementation detail, especially for
            // namespaces contributed by several references. It is never user-facing hover content.
            Footer = None
            Link = symbolLink use'.Symbol
        }

    /// Select only imported modules that own symbols actually used in this example.
    /// Prefer the most specific authored open; never infer an open for a qualified use.
    let deriveRequirements (available: string list) (packageForAssembly: string -> string option) (uses: (string * string * bool) list) =
        uses
        |> List.choose (fun (owner, assembly, qualified) ->
            if owner.StartsWith("FsLiveDocsGenerated", StringComparison.Ordinal) then None
            elif qualified then packageForAssembly assembly |> Option.map (fun package -> { Open = ""; Package = Some package })
            else
                let owner = Regex.Replace(owner, @"^.*?\.(?:md|fsx)_page\.", "", RegexOptions.IgnoreCase)
                available
                |> List.filter (fun name -> owner = name || owner.StartsWith(name + ".", StringComparison.Ordinal))
                |> List.sortByDescending _.Length
                |> List.tryHead
                |> Option.map (fun name -> { Open = name; Package = packageForAssembly assembly }))
        |> List.distinct
        |> List.sortBy (fun requirement -> requirement.Open, requirement.Package)

    let private openPattern = Regex(@"(?m)^\s*open\s+([\w.]+)\s*$", RegexOptions.Compiled)

    let private exampleContext (range: CompilationSourceRange) (source: string) (uses: FSharpSymbolUse array) (packageForAssembly: string -> string option) =
        let lines = DocumentationDiscovery.normalizeSource(source).Split('\n')
        let available =
            lines |> Seq.take (min lines.Length range.EndLine)
            |> Seq.choose (fun line -> let matched = openPattern.Match line in if matched.Success then Some matched.Groups.[1].Value else None)
            |> Seq.distinct |> Seq.toList
        let localLine (use': FSharpSymbolUse) =
            if use'.Range.StartLine > 0 && use'.Range.StartLine <= lines.Length then lines.[use'.Range.StartLine - 1] else ""
        let requirements =
            uses
            |> Array.filter (fun use' -> use'.Range.StartLine >= range.StartLine && use'.Range.StartLine <= range.EndLine && not use'.IsFromDefinition)
            |> Array.choose (fun use' ->
                let owner =
                    match use'.Symbol with
                    // Instance/static members are reached through a value or type, not an open.
                    // Extension members are the exception: their defining module must be in scope.
                    | :? FSharpMemberOrFunctionOrValue as value when value.IsMember && not value.IsExtensionMember -> None
                    | :? FSharpMemberOrFunctionOrValue as value -> value.DeclaringEntity |> Option.bind _.TryFullName
                    | :? FSharpEntity as entity -> entity.TryFullName |> Option.bind (fun name ->
                        let last = name.LastIndexOf('.')
                        if last < 0 then None else Some(name.Substring(0, last)))
                    | :? FSharpField as field -> field.DeclaringEntity |> Option.bind _.TryFullName
                    | :? FSharpUnionCase as case' -> case'.DeclaringEntity.TryFullName
                    | _ -> None
                owner |> Option.map (fun name ->
                    let line = localLine use'
                    let startColumn = min line.Length use'.Range.StartColumn
                    let before = line.Substring(0, startColumn).TrimEnd()
                    let usedText = line.Substring(startColumn, max 0 (min line.Length use'.Range.EndColumn - startColumn))
                    let explicitlyQualified =
                        available |> List.exists (fun opened -> usedText.StartsWith(opened + ".", StringComparison.Ordinal))
                    name, use'.Symbol.Assembly.SimpleName,
                    before.EndsWith(".", StringComparison.Ordinal) || explicitlyQualified || line.TrimStart().StartsWith("open ", StringComparison.Ordinal)))
            |> Array.toList
            |> deriveRequirements available packageForAssembly
        let signatures =
            uses
            |> Array.filter (fun use' -> use'.IsFromDefinition && use'.Range.StartLine >= range.StartLine && use'.Range.StartLine <= range.EndLine)
            |> Array.choose (fun use' ->
                match use'.Symbol with
                | :? FSharpMemberOrFunctionOrValue as value when value.IsModuleValueOrMember && not value.IsMember ->
                    let line = localLine use'
                    if Regex.IsMatch(line, @"^\s*(?:let|and)\s+(?:rec\s+|mutable\s+|inline\s+|private\s+)*") then
                        try Some(use'.Range.StartLine, $"val {value.DisplayName}: {value.FullType.Format(use'.DisplayContext)}") with _ -> None
                    else None
                | _ -> None)
            |> Array.distinctBy snd
            |> Array.sortBy fst
            |> Array.map snd |> Array.toList
        { Requires = requirements; Types = signatures }

    let extractBlock contextHash (range: CompilationSourceRange) (uses: FSharpSymbolUse array) diagnostics (context: ExampleContext option) =
        let relevantUses =
            uses
            |> Array.filter (fun use' -> use'.Range.StartLine >= range.StartLine && use'.Range.StartLine <= range.EndLine)
        let tooltips = ResizeArray<SemanticTooltip>()
        let tooltipIndexes = Collections.Generic.Dictionary<string, int>()
        let tooltipFor use' =
            let tooltip = buildTooltip use'
            let key = defaultArg tooltip.Signature "" + "\n" + defaultArg tooltip.Documentation "" + "\n" + defaultArg tooltip.Link ""
            match tooltipIndexes.TryGetValue key with
            | true, index -> index
            | _ ->
                let index = tooltips.Count
                tooltipIndexes.[key] <- index
                tooltips.Add tooltip
                index
        let lines = DocumentationDiscovery.normalizeSource(range.Block.ExpandedSource).Split('\n')
        let semanticLines =
            lines
            |> Array.mapi (fun lineIndex line ->
                let syntheticLine = range.StartLine + lineIndex
                let lineUses = relevantUses |> Array.filter (fun use' -> use'.Range.StartLine = syntheticLine)
                let tokens =
                    tokenPattern.Matches(line)
                    |> Seq.cast<Match>
                    |> Seq.map (fun matched ->
                        let owning =
                            lineUses
                            |> Array.tryFind (fun use' -> matched.Index >= use'.Range.StartColumn && matched.Index + matched.Length <= use'.Range.EndColumn)
                        match owning with
                        | Some use' -> { Text = matched.Value; Kind = symbolKind use'.Symbol; Tooltip = Some(tooltipFor use') }
                        | None -> { Text = matched.Value; Kind = lexicalKind matched.Value; Tooltip = None })
                    |> Seq.toList
                { Tokens = tokens })
            |> Array.toList
        {
            Id = range.Block.Id
            SourceHash = range.Block.SourceHash
            ContextHash = contextHash
            Lines = semanticLines
            Tooltips = Seq.toList tooltips
            Diagnostics = diagnostics
            ExampleContext = context
        }

    let extract contextHash ranges source packageForAssembly (checkResults: FSharpCheckFileResults) (mappedDiagnostics: MappedCompilerDiagnostic list) =
        let uses = checkResults.GetAllUsesOfAllSymbolsInFile() |> Seq.toArray
        ranges
        |> List.map (fun range ->
            let diagnostics =
                mappedDiagnostics
                |> List.filter (fun diagnostic -> diagnostic.BlockId = Some range.Block.Id)
                |> List.map (fun diagnostic ->
                    { Severity = diagnostic.Severity; Message = diagnostic.Message; StartLine = diagnostic.StartLine; StartColumn = diagnostic.StartColumn; EndLine = diagnostic.EndLine; EndColumn = diagnostic.EndColumn })
            let context =
                match range.Block.Mode with
                | Page | Run -> Some(exampleContext range source uses packageForAssembly)
                | _ -> None
            extractBlock contextHash range uses diagnostics context)

    /// Creates a complete artifact only from successful compiler results.
    let artifact (packageForAssembly: string -> string option) (results: CheckedCompilationUnit list) =
        let errors =
            results
            |> List.collect _.Diagnostics
            |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
        if not errors.IsEmpty then
            let first = List.head errors
            invalidOp $"Cannot create semantic data: {defaultArg first.BlockId first.SourcePath}({first.StartLine},{first.StartColumn}): {first.Message}"
        let blocks =
            results
            |> List.collect (fun result ->
                match result.CheckResults with
                | None -> invalidOp $"Compiler checking aborted for {result.Unit.Id}."
                | Some checkResults ->
                    let hash = DocumentationDiscovery.contextHash result.Unit.Prelude result.Unit.Blocks
                    extract hash result.BlockRanges result.SyntheticSource packageForAssembly checkResults result.Diagnostics)
        {
            SchemaVersion = History.SemanticSchemaVersion
            Prelude = results |> List.tryHead |> Option.map (_.Unit.Prelude) |> Option.defaultValue ""
            Pages =
                blocks
                |> List.groupBy (fun block -> block.Id.Split('#').[0])
                |> List.map (fun (sourcePath, pageBlocks) -> { SourcePath = sourcePath; Blocks = pageBlocks })
                |> List.sortBy _.SourcePath
        }
