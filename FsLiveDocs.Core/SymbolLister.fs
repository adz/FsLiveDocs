namespace FsLiveDocs.Core

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Xml

module SymbolLister =

    let checker = FSharpChecker.Create(keepAssemblyContents = true)

    let normalizeName (name: string) =
        name.Replace("Module", "")
            .Replace("`1", "")
            .Replace("`2", "")
            .Trim()

    let getSignature (m: FSharpMemberOrFunctionOrValue) =
        m.FullType.Format(FSharpDisplayContext.Empty)

    let getParameters (m: FSharpMemberOrFunctionOrValue) =
        [ for group in m.CurriedParameterGroups do
            for p in group do
                yield { 
                    Name = p.DisplayName
                    Type = p.Type.Format(FSharpDisplayContext.Empty)
                    DescriptionHtml = "" 
                }
        ]

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

    let getXmlText (xmlDoc: FSharpXmlDoc) =
        match xmlDoc with
        | FSharpXmlDoc.None -> ""
        | FSharpXmlDoc.FromXmlText (doc) -> 
            doc.UnprocessedLines |> String.concat "\n"
        | _ -> ""

    let rec mapEntity (e: FSharpEntity) : EntityModel * ScenarioModel list =
        let xmlDoc = getXmlText e.XmlDoc
        let mutable scenarios = []
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
        
        let nested = e.NestedEntities |> Seq.map mapEntity |> Seq.toList
        let nestedEntities = nested |> List.map fst
        let nestedScenarios = nested |> List.collect snd
        
        let entity = {
            Id = e.FullName
            Name = normalizeName e.DisplayName
            Kind = if e.IsFSharpModule then "Module" else "Type"
            SummaryHtml = xmlDoc
            Members = members
            Entities = nestedEntities
        }
        entity, scenarios @ nestedScenarios

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
