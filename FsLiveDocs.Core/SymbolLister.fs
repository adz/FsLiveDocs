namespace FsLiveDocs.Core

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.EditorServices

module SymbolLister =

    let checker = FSharpChecker.Create(keepAssemblyContents = true)

    let normalizeName (name: string) =
        name.Replace("Module", "")
            .Replace("`1", "")
            .Replace("`2", "")
            .Trim()

    let getSignature (m: FSharpMemberOrFunctionOrValue) =
        m.FullType.Format(FSharpDisplayContext.Empty)

    let extractExamples (xmlDoc: string) =
        let pattern = @"<example(?:\s+name=""(?<name>[^""]+)"")?(?:\s+scenario=""(?<scenario>[^""]+)"")?>(?<code>.*?)<\/example>"
        let matches = System.Text.RegularExpressions.Regex.Matches(xmlDoc, pattern, System.Text.RegularExpressions.RegexOptions.Singleline)
        [ for m in matches do
            let code = m.Groups.["code"].Value.Trim()
            let parts = code.Split([| "// EXPECTED:" |], StringSplitOptions.None)
            let content = parts.[0].Trim()
            let expected = if parts.Length > 1 then Some (parts.[1].Trim()) else None
            yield { 
                Name = m.Groups.["name"].Value
                Content = content
                ExpectedOutput = expected
                Scenario = if m.Groups.["scenario"].Success then Some m.Groups.["scenario"].Value else None
            }
        ]

    let getXmlText (xmlDoc: FSharpXmlDoc) =
        match xmlDoc with
        | FSharpXmlDoc.None -> ""
        | FSharpXmlDoc.FromXmlText (doc) -> doc.ToString()
        | _ -> ""

    let rec mapEntity (e: FSharpEntity) : EntityModel =
        let xmlDoc = getXmlText e.XmlDoc
        {
            Id = e.FullName
            Name = normalizeName e.DisplayName
            Kind = if e.IsFSharpModule then "Module" else "Type"
            SummaryHtml = xmlDoc
            Members = 
                e.MembersFunctionsAndValues 
                |> Seq.filter (fun m -> not m.IsCompilerGenerated)
                |> Seq.map (fun m -> 
                    let mXmlDoc = getXmlText m.XmlDoc
                    {
                        Id = m.FullName
                        Name = m.DisplayName
                        Signature = getSignature m
                        SummaryHtml = mXmlDoc
                        RemarksHtml = ""
                        Examples = extractExamples mXmlDoc
                        Location = { File = m.DeclarationLocation.FileName; Line = m.DeclarationLocation.StartLine }
                    }
                ) |> Seq.toList
            Entities = e.NestedEntities |> Seq.map mapEntity |> Seq.toList
        }

    let rec walkDeclarations (decls: FSharpImplementationFileDeclaration list) =
        [ for d in decls do
            match d with
            | FSharpImplementationFileDeclaration.Entity (e, subDecls) -> 
                yield mapEntity e
                yield! walkDeclarations subDecls
            | _ -> ()
        ]

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
        
        let entities = 
            checkResults.AssemblyContents.ImplementationFiles
            |> List.collect (fun f -> walkDeclarations f.Declarations)

        return { Version = "0.1.0"; Entities = entities }
    }
