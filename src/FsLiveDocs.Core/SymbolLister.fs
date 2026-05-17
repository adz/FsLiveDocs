namespace FsLiveDocs.Core

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Xml

/// <summary>Provides capabilities to scan F# projects and extract symbols, signatures, and docstrings.</summary>
module SymbolLister =

    /// <summary>The shared compiler checker instance.</summary>
    let checker = FSharpChecker.Create(keepAssemblyContents = true)

    /// <summary>Normalizes F# compiler names (removes 'Module' suffix and generic backticks).</summary>
    let normalizeName (name: string) =
        name.Replace("Module", "")
            .Replace("`1", "")
            .Replace("`2", "")
            .Replace("`3", "")
            .Trim()

    /// <summary>Formats a member signature into a readable F# string.</summary>
    let getSignature (m: FSharpMemberOrFunctionOrValue) =
        m.FullType.Format(FSharpDisplayContext.Empty)

    /// <summary>Extracts parameters from a member, including their types.</summary>
    let getParameters (m: FSharpMemberOrFunctionOrValue) =
        [ for group in m.CurriedParameterGroups do
            for p in group do
                yield { 
                    Name = p.DisplayName
                    Type = p.Type.Format(FSharpDisplayContext.Empty)
                    DescriptionHtml = "" 
                }
        ]

    /// <summary>Extracts &lt;example&gt; tags from XML documentation for verification and transclusion.</summary>
    let extractExamples (xmlDoc: string) =
        let pattern = @"<example(?:\s+name=""(?<name>[^""]+)"")?(?:\s+scenario=""(?<scenario>[^""]+)"")?>(?<code>.*?)<\/example>"
        let matches = System.Text.RegularExpressions.Regex.Matches(xmlDoc, pattern, System.Text.RegularExpressions.RegexOptions.Singleline)
        [ for m in matches do
            let code = m.Groups.["code"].Value.Trim()
            let parts = code.Split([| "// EXPECTED:" |], StringSplitOptions.None)
            let content = parts.[0].Trim()
            let expected = if parts.Length > 1 then Some (parts.[1].Trim()) else None
            yield { 
                Name = if m.Groups.["name"].Success then m.Groups.["name"].Value else "Example"
                Content = content
                ExpectedOutput = expected
                Scenario = if m.Groups.["scenario"].Success then Some m.Groups.["scenario"].Value else None
            }
        ]

    /// <summary>Extracts raw text from an FSharpXmlDoc instance.</summary>
    let getXmlText (xmlDoc: FSharpXmlDoc) =
        match xmlDoc with
        | FSharpXmlDoc.None -> ""
        | FSharpXmlDoc.FromXmlText (doc) -> 
            try doc.UnprocessedLines |> String.concat "\n" with _ -> ""
        | _ -> ""

    /// <summary>Recursively maps an FSharpEntity to an EntityModel, extracting members, fields, and scenarios.</summary>
    let rec mapEntity (e: FSharpEntity) : EntityModel * ScenarioModel list =
        let xmlDoc = getXmlText e.XmlDoc
        
        let introPath = Path.Combine("docs", "api", e.FullName + ".md")
        let summary = 
            if File.Exists(introPath) then
                let content = File.ReadAllText(introPath)
                if content.StartsWith("---") then
                    let parts = content.Split([| "---" |], StringSplitOptions.RemoveEmptyEntries)
                    if parts.Length > 1 then String.concat "---" parts.[1..] else content
                else content
            else xmlDoc

        let mutable scenarios = []
        
        // 1. Extract standard members
        let members = 
            e.MembersFunctionsAndValues 
            |> Seq.filter (fun m -> not m.IsCompilerGenerated)
            |> Seq.map (fun m -> 
                let mXmlDoc = getXmlText m.XmlDoc
                for attr in m.Attributes do
                    if attr.AttributeType.FullName = "FsLiveDocs.Core.DocScenarioAttribute" then
                        let args = attr.ConstructorArguments
                        if args.Count > 0 then
                            match args.[0] with
                            | (_, (:? string as name)) -> 
                                scenarios <- { Name = name; MethodId = m.FullName } :: scenarios
                            | _ -> ()

                {
                    Id = m.FullName
                    Name = m.DisplayName
                    Signature = getSignature m
                    Parameters = getParameters m
                    ReturnType = m.ReturnParameter.Type.Format(FSharpDisplayContext.Empty)
                    SummaryHtml = mXmlDoc
                    RemarksHtml = ""
                    Examples = extractExamples mXmlDoc
                    Location = { File = m.DeclarationLocation.FileName; Line = m.DeclarationLocation.StartLine }
                }
            ) |> Seq.toList

        // 2. Extract record fields if any
        let fields =
            if e.IsFSharpRecord then
                e.FSharpFields
                |> Seq.map (fun f -> 
                    {
                        Id = e.FullName + "." + f.Name
                        Name = f.Name
                        Signature = f.FieldType.Format(FSharpDisplayContext.Empty)
                        Parameters = []
                        ReturnType = f.FieldType.Format(FSharpDisplayContext.Empty)
                        SummaryHtml = getXmlText f.XmlDoc
                        RemarksHtml = ""
                        Examples = []
                        Location = { File = f.DeclarationLocation.FileName; Line = f.DeclarationLocation.StartLine }
                    }
                ) |> Seq.toList
            else []

        // 3. Extract union cases if any
        let cases =
            if e.IsFSharpUnion then
                e.UnionCases
                |> Seq.map (fun c -> 
                    {
                        Id = e.FullName + "." + c.Name
                        Name = c.Name
                        Signature = c.Name // Union cases don't have a standard signature in the same way
                        Parameters = []
                        ReturnType = e.DisplayName
                        SummaryHtml = getXmlText c.XmlDoc
                        RemarksHtml = ""
                        Examples = []
                        Location = { File = c.DeclarationLocation.FileName; Line = c.DeclarationLocation.StartLine }
                    }
                ) |> Seq.toList
            else []

        let nested = e.NestedEntities |> Seq.map mapEntity |> Seq.toList
        let nestedEntities = nested |> List.map fst
        let nestedScenarios = nested |> List.collect snd
        
        let entity = {
            Id = e.FullName
            Name = normalizeName e.DisplayName
            Kind = if e.IsFSharpModule then "Module" elif e.IsFSharpRecord then "Record" elif e.IsFSharpUnion then "Union" else "Type"
            SummaryHtml = summary
            Members = members @ fields @ cases
            Entities = nestedEntities
        }
        entity, scenarios @ nestedScenarios

    /// <summary>Walks through all implementation file declarations to find entities and scenarios.</summary>
    let rec walkDeclarations (decls: FSharpImplementationFileDeclaration list) =
        let mutable entities = []
        let mutable scenarios = []
        for d in decls do
            match d with
            | FSharpImplementationFileDeclaration.Entity (e, subDecls) -> 
                let ent, sc = mapEntity e
                entities <- ent :: entities
                scenarios <- sc @ scenarios
                let subEnts, subScs = walkDeclarations subDecls
                entities <- subEnts @ entities
                scenarios <- subScs @ scenarios
            | _ -> ()
        entities, scenarios

    /// <summary>Merges multiple PackageModels into a single unified documentation model.</summary>
    let merge (packages: PackageModel list) =
        if packages.IsEmpty then 
            { Version = "0.1.0"; Entities = []; Scenarios = [] }
        else
            { Version = (packages |> List.head).Version
              Entities = packages |> List.collect (fun p -> p.Entities)
              Scenarios = packages |> List.collect (fun p -> p.Scenarios) }

    /// <summary>Scans a project file and extracts all documented symbols.</summary>
    let extractFromProject (projectPath: string) = async {
        let sourceFiles = Directory.GetFiles(Path.GetDirectoryName(projectPath), "*.fs", SearchOption.AllDirectories)
        let options : FSharpProjectOptions = {
            ProjectFileName = projectPath
            ProjectId = None
            SourceFiles = sourceFiles
            OtherOptions = [| "--targetprofile:netcore" |]
            ReferencedProjects = [||]
            IsIncompleteTypeCheckEnvironment = false
            UseScriptResolutionRules = false
            LoadTime = DateTime.Now
            UnresolvedReferences = None
            OriginalLoadReferences = []
            Stamp = None
        }
        
        let! checkResults = checker.ParseAndCheckProject(options)
        
        let results = 
            checkResults.AssemblyContents.ImplementationFiles
            |> List.map (fun f -> walkDeclarations f.Declarations)
        
        let entities = results |> List.collect fst
        let scenarios = results |> List.collect snd

        return { Version = "0.1.0"; Entities = entities; Scenarios = scenarios }
    }
