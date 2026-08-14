namespace FsLiveDocs.Runner

open System
open FsLiveDocs.Core

/// The single stable execution boundary used by generated xUnit cases.
module GeneratedVerification =

    let private verifyCompilationUnit projectPath references sourcePath expandedMarkdown unitId = async {
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

    let private executeBlock projectPath references sourcePath expandedMarkdown blockId =
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

    /// Runs one generated case without exposing verification ordering or composition to its caller.
    let runCase references (case: GeneratedVerificationCase) = async {
        // Recreate the canonical cases first. This validates coverage and makes stale generated
        // source fail even when a developer selects only one generated xUnit fact.
        let currentCases =
            DocumentationDiscovery.generatedCases
                case.ProjectPath
                ""
                case.SourcePath
                case.ExpandedMarkdown
                Set.empty

        let current =
            currentCases
            |> List.tryFind (fun candidate -> candidate.Id = case.Id && candidate.Action = case.Action)
            |> Option.defaultWith (fun () ->
                invalidOp $"Generated documentation case no longer exists: {case.Id}. Regenerate tests.")

        match current.Action with
        | CompileUnit unitId ->
            do! verifyCompilationUnit current.ProjectPath references current.SourcePath current.ExpandedMarkdown unitId
        | ExecuteBlock blockId ->
            // A selected execution fact must retain the compile-before-execute contract even when
            // its companion compilation fact is not run by the test filter.
            let blocks = DocumentationDiscovery.discoverMarkdown current.SourcePath (Some current.ProjectPath) current.ExpandedMarkdown
            let owningUnit =
                DocumentationDiscovery.compilationUnits current.ProjectPath "" blocks
                |> List.find (fun unit -> unit.Blocks |> List.exists (fun block -> block.Id = blockId))
            do! verifyCompilationUnit current.ProjectPath references current.SourcePath current.ExpandedMarkdown owningUnit.Id
            executeBlock current.ProjectPath references current.SourcePath current.ExpandedMarkdown blockId
        | ExecuteTranscriptBlock blockId ->
            executeBlock current.ProjectPath references current.SourcePath current.ExpandedMarkdown blockId
    }

    /// <summary>
    /// Compiles XML examples that no page transcludes and no snapshot runs, against the project
    /// that declares them.
    /// </summary>
    /// <remarks>
    /// A markdown fence is compiled unless excluded in writing; this brings XML examples to the
    /// same standard. An example carrying its own no-check reason is skipped, as is one already
    /// covered elsewhere. Failures are returned rather than raised, so the caller decides whether
    /// they warn or fail the run.
    /// </remarks>
    let compileUncoveredExamples (projectPath: string) (prelude: string) (references: string list) (covered: Set<string>) (package: PackageModel) = async {
        let examples =
            let rec walk (entity: EntityModel) =
                [ let entityExamples = if isNull (box entity.Examples) then [] else entity.Examples
                  for example in entityExamples do
                      yield entity.Id, { File = ""; Line = 0 }, example
                  for entityMember in entity.Members do
                      for example in entityMember.Examples do
                          yield entityMember.Id, entityMember.Location, example
                  for nested in entity.Entities do
                      yield! walk nested ]
            package.Entities
            |> List.collect walk
            |> List.filter (fun (_, _, example) ->
                example.NoCheckReason.IsNone
                && not example.IsSnapshotTest
                && not (covered.Contains example.Name))

        if examples.IsEmpty then
            return []
        else
            let blocks =
                examples
                |> List.mapi (fun ordinal (owner, _, example) -> {
                    Id = $"{owner}#example-{example.Name}"
                    Origin = XmlExample
                    SourcePath = owner
                    Ordinal = ordinal
                    ExpandedSource = example.Content
                    SourceHash = DocumentationDiscovery.sourceHash Isolated example.Content
                    // Isolated: an example is a standalone illustration, not part of a page's flow.
                    Mode = Isolated
                    Project = Some projectPath
                })

            // An example demonstrates the library that declares it, so that library's own
            // assembly has to be on the compilation's reference list. Evaluating the project
            // alone yields its dependencies but never its own output.
            let evaluated = DocumentationCompiler.evaluateProject projectPath
            let project = { evaluated with References = List.distinct (evaluated.References @ references) }
            let! results = DocumentationCompiler.checkBlocksWithProject project prelude blocks
            let locations =
                examples
                |> List.map (fun (owner, location, example) -> $"{owner}#example-{example.Name}", (owner, location))
                |> Map.ofList

            return
                results
                |> List.collect (fun result ->
                    result.Diagnostics
                    |> List.filter (fun diagnostic -> diagnostic.Severity = SemanticDiagnosticSeverity.Error)
                    |> List.truncate 1
                    |> List.map (fun diagnostic ->
                        let owner, location =
                            locations |> Map.tryFind result.Unit.Id |> Option.defaultValue (result.Unit.Id, { File = ""; Line = 0 })
                        {
                            Code = "example-does-not-compile"
                            Symbol = result.Unit.Id
                            Location = location
                            Message = $"This example does not compile: {diagnostic.Message}"
                            Remedy = $"Fix the example, transclude it into a page, or exclude it with data-livedocs=\"no-check\" reason=\"...\" ({owner})."
                        }))
    }
