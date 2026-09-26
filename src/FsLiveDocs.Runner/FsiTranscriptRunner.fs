namespace FsLiveDocs.Runner

open System
open System.IO
open FsLiveDocs.Core
open FsLiveDocs.TranscriptHost

/// <summary>Builds FSI transcript sessions and runs them in the isolated transcript worker.</summary>
module FsiTranscriptRunner =
    type DocTestExecutionContext = {
        Project: ResolvedProject
        References: string list
        Scenario: ScenarioModel option
        Example: ExampleModel
    }

    let private buildLoadScript (project: ResolvedProject) (references: string list) (extraOpens: string list) =
        let projectDependencies =
            if String.IsNullOrWhiteSpace project.AssemblyPath then []
            else
                let directory = Path.GetDirectoryName project.AssemblyPath
                if Directory.Exists directory then
                    Directory.GetFiles(directory, "*.dll")
                    |> Array.filter (fun path ->
                        let name = Path.GetFileNameWithoutExtension path
                        not (name.Equals("FSharp.Core", StringComparison.OrdinalIgnoreCase))
                        && not (name.Equals("FSharp.Compiler.Service", StringComparison.OrdinalIgnoreCase)))
                    |> Array.toList
                else []
        let refs =
            project.AssemblyPath :: (projectDependencies @ references)
            |> List.distinct
            |> List.filter (fun path -> not (String.IsNullOrWhiteSpace path))
            |> List.map (fun path -> $"#r @\"{Path.GetFullPath path}\"")

        let opens =
            [
                "open System"
                if not (String.IsNullOrWhiteSpace project.ProjectNamespace) then $"open {project.ProjectNamespace}"
                // Assembly names are not namespace names. Opening each reference worked only
                // by accident until FsLiveDocs.Annotations became an F# assembly whose package
                // name differs from its FsLiveDocs namespace.
                yield! extraOpens
            ]
            |> List.distinct

        String.concat "\n" (refs @ opens)

    let private script context =
        let transcript = ExampleTranscript.parse context.Example.Content
        let scenarioCall = context.Scenario |> Option.map (fun scenario -> $"{scenario.MethodId}()")
        let blocks =
            [ buildLoadScript context.Project context.References [] ]
            @ (scenarioCall |> Option.toList)
            @ transcript.Interactions
        transcript, scenarioCall, blocks

    let private request context : TranscriptExample =
        let _, scenarioCall, blocks = script context
        { Blocks = List.toArray blocks; SetupCount = 1 + (if scenarioCall.IsSome then 1 else 0) }

    /// Runs one example in its own transcript worker session.
    let runExample (context: DocTestExecutionContext) =
        let transcript, _, _ = script context
        let output = TranscriptHostClient.run [ request context ] |> List.exactlyOne
        output, transcript.ExpectedOutput, transcript.DisplayText

    /// Runs a project's independent documentation examples in one worker session. Definitions
    /// from later FSI interactions shadow earlier ones, while output is sliced per example.
    let runExamples (contexts: DocTestExecutionContext list) =
        let outputs = TranscriptHostClient.run (contexts |> List.map request)
        (contexts, outputs)
        ||> List.map2 (fun context output ->
            let transcript, _, _ = script context
            output, transcript.ExpectedOutput, transcript.DisplayText)
