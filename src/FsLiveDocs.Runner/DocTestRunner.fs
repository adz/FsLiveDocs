namespace FsLiveDocs.Runner

open System
open System.IO
open FsLiveDocs.Core

/// <summary>The orchestration layer for verified docstrings (DocTests).</summary>
module DocTestRunner =

    let private getAllExamples (entities: EntityModel list) =
        let rec walk (e: EntityModel) =
            let members = e.Members |> List.collect (fun m -> m.Examples)
            let nested = e.Entities |> List.collect walk
            e.Examples @ members @ nested

        entities |> List.collect walk

    let private getSnapshotExamples (entities: EntityModel list) =
        getAllExamples entities
        |> List.filter (fun ex -> ex.IsSnapshotTest)

    /// <summary>Returns stable names for all explicitly selected XML examples.</summary>
    let snapshotExampleNames (package: PackageModel) =
        getSnapshotExamples package.Entities |> List.map _.Name

    let private statusOf (expected: string option) (actual: string) =
        match expected with
        | None -> ExampleStatus.FirstCut
        | Some expectedText when String.Equals(actual, expectedText.Trim(), StringComparison.Ordinal) -> ExampleStatus.Verified
        | Some _ -> ExampleStatus.Mismatch

    /// <summary>Runs one named XML example so generated test discovery identifies it directly.</summary>
    let collectSnapshotByName (package: PackageModel) (projectPath: string) (references: string list) name = async {
        let example =
            getSnapshotExamples package.Entities
            |> List.tryFind (fun example -> example.Name = name)
            |> Option.defaultWith (fun () -> invalidOp $"Snapshot-selected XML example no longer exists: {name}. Regenerate tests.")
        let project = ProjectResolver.resolve projectPath
        if String.IsNullOrWhiteSpace project.AssemblyPath || not (File.Exists project.AssemblyPath) then
            invalidOp $"Could not resolve built assembly for {project.ProjectPath}"
        let scenario =
            example.Scenario
            |> Option.map (fun scenarioName ->
                package.Scenarios
                |> List.tryFind (fun scenario -> scenario.Name = scenarioName)
                |> Option.defaultWith (fun () -> invalidOp $"XML example {name} references missing scenario {scenarioName}."))
        let output, expected, source =
            FsiTranscriptRunner.runExample
                { Project = project; References = references; Scenario = scenario; Example = example }
        let actual = output.Trim()
        return {
            Name = example.Name
            Scenario = example.Scenario
            Source = source
            ExpectedOutput = expected
            ActualOutput = actual
            Status = statusOf expected actual
        }
    }

    /// <summary>Runs snapshot-selected examples and returns structured results for a generated Verify project.</summary>
    /// <param name="package">The package model containing examples and scenarios.</param>
    /// <param name="projectPath">The project that produced the package.</param>
    /// <param name="references">Additional references needed by generated examples.</param>
    /// <returns>A snapshot payload that can be verified by a generated test project.</returns>
    let collectSnapshots (package: PackageModel) (projectPath: string) (references: string list) = async {
        let project = ProjectResolver.resolve projectPath

        if String.IsNullOrWhiteSpace project.AssemblyPath || not (File.Exists project.AssemblyPath) then
            return {
                ProjectPath = project.ProjectPath
                ProjectNamespace = project.ProjectNamespace
                Examples = [
                    {
                        Name = "project"
                        Scenario = None
                        Source = ""
                        ExpectedOutput = None
                        ActualOutput = $"Could not resolve built assembly for {project.ProjectPath}"
                        Status = ExampleStatus.Error
                    }
                ]
            }
        else
            let examples = getSnapshotExamples package.Entities

            let results =
                examples
                |> List.map (fun ex ->
                    let scenario =
                        ex.Scenario
                        |> Option.bind (fun sName -> package.Scenarios |> List.tryFind (fun s -> s.Name = sName))

                    let output, expected, source =
                        FsiTranscriptRunner.runExample
                            {
                                Project = project
                                References = references
                                Scenario = scenario
                                Example = ex
                            }
                    let actual = output.Trim()
                    {
                        Name = ex.Name
                        Scenario = ex.Scenario
                        Source = source
                        ExpectedOutput = expected
                        ActualOutput = actual
                        Status = statusOf expected actual
                    })

            return {
                ProjectPath = project.ProjectPath
                ProjectNamespace = project.ProjectNamespace
                Examples = results
            }
    }

    /// <summary>Verifies all docstring examples extracted from a package.</summary>
    /// <param name="package">The package model containing examples and scenarios.</param>
    /// <param name="projectPath">The project that produced the package.</param>
    /// <param name="references">Additional references needed by generated examples.</param>
    /// <returns>A list of example names paired with pass/fail results and diagnostic output.</returns>
    let verifyExamples (package: PackageModel) (projectPath: string) (references: string list) = async {
        let project = ProjectResolver.resolve projectPath

        if String.IsNullOrWhiteSpace project.AssemblyPath || not (File.Exists project.AssemblyPath) then
            return [
                "project", false, $"Could not resolve built assembly for {project.ProjectPath}"
            ]
        else
            let allExamples = getSnapshotExamples package.Entities

            if List.isEmpty allExamples then
                return []
            else
                let mutable results = []
                for ex in allExamples do
                    let scenario =
                        ex.Scenario
                        |> Option.bind (fun sName -> package.Scenarios |> List.tryFind (fun s -> s.Name = sName))

                    let output, expected, _ =
                        FsiTranscriptRunner.runExample
                            {
                                Project = project
                                References = references
                                Scenario = scenario
                                Example = ex
                            }
                    let actual = output.Trim()

                    match expected with
                    | Some expectedText ->
                        let expectedText = expectedText.Trim()
                        if String.Equals(actual, expectedText, StringComparison.Ordinal) then
                            results <- (ex.Name, true, actual) :: results
                        else
                            results <- (ex.Name, false, $"Expected:\n{expectedText}\n\nActual:\n{actual}") :: results
                    | None ->
                        results <- (ex.Name, true, actual) :: results

                return List.rev results
    }
