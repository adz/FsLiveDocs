namespace FsLiveDocs.Runner

open System
open FsLiveDocs.Core

/// Stable entry points used by generated xUnit cases.
module GeneratedVerification =

    let validateCoverage projectPath sourcePath expandedMarkdown =
        let blocks = DocumentationDiscovery.discoverMarkdown sourcePath (Some projectPath) expandedMarkdown
        DocumentationDiscovery.verificationCases projectPath "" blocks |> ignore

    let verifyCompilationUnit projectPath references sourcePath expandedMarkdown unitId = async {
        let blocks = DocumentationDiscovery.discoverMarkdown sourcePath (Some projectPath) expandedMarkdown
        DocumentationDiscovery.validateCoverage blocks
        let unit =
            DocumentationDiscovery.compilationUnits projectPath "" blocks
            |> List.tryFind (fun candidate -> candidate.Id = unitId)
            |> Option.defaultWith (fun () -> invalidOp $"Generated documentation case no longer exists: {unitId}. Regenerate tests.")
        let evaluated = DocumentationCompiler.evaluateProject projectPath
        let project = { evaluated with References = List.distinct (evaluated.References @ references) }
        let! result = DocumentationCompiler.checkUnit project unit
        let errors = result.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
        if not errors.IsEmpty then
            let details =
                errors
                |> List.map (fun diagnostic -> $"{defaultArg diagnostic.BlockId sourcePath}({diagnostic.StartLine},{diagnostic.StartColumn}): {diagnostic.Message}")
                |> String.concat Environment.NewLine
            invalidOp details
    }

    let executeBlock projectPath references sourcePath expandedMarkdown blockId =
        let block =
            DocumentationDiscovery.discoverMarkdown sourcePath (Some projectPath) expandedMarkdown
            |> List.tryFind (fun candidate -> candidate.Id = blockId)
            |> Option.defaultWith (fun () -> invalidOp $"Generated execution case no longer exists: {blockId}. Regenerate tests.")
        let content, expected =
            match block.Mode with
            | Run ->
                let pageSource =
                    DocumentationDiscovery.discoverMarkdown sourcePath (Some projectPath) expandedMarkdown
                    |> List.takeWhile (fun candidate -> candidate.Id <> block.Id)
                    |> fun preceding -> preceding @ [ block ]
                    |> List.filter (fun candidate ->
                        match candidate.Mode with Page | Prepare | Run -> true | _ -> false)
                    |> List.map _.ExpandedSource
                    |> String.concat "\n\n"
                pageSource, None
            | Transcript ->
                let parsed = ExampleTranscript.parse block.ExpandedSource
                block.ExpandedSource, parsed.ExpectedOutput
            | _ -> invalidOp $"{blockId} is not an executable documentation block."
        let project = ProjectResolver.resolve projectPath
        let example = ExampleModel.Create(block.Id, content, expected, None)
        let output, expectedOutput, _ =
            FsiTranscriptRunner.runExample { Project = project; References = references; Scenario = None; Example = example }
        match expectedOutput with
        | Some expected when output.Trim() <> expected.Trim() ->
            invalidOp $"{blockId} output mismatch.{Environment.NewLine}Expected:{Environment.NewLine}{expected}{Environment.NewLine}Actual:{Environment.NewLine}{output}"
        | _ when output.Contains("error FS", StringComparison.OrdinalIgnoreCase) -> invalidOp $"{blockId} execution failed:{Environment.NewLine}{output}"
        | _ -> ()
