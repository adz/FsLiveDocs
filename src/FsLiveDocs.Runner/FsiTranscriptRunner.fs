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

        let projectNamespaces =
            match ProjectResolver.exportedRootNamespaces project.AssemblyPath with
            | [] when not (String.IsNullOrWhiteSpace project.ProjectNamespace) -> [ project.ProjectNamespace ]
            | namespaces -> namespaces

        let opens =
            [
                "open System"
                // Open the assembly's declared namespaces rather than a name guessed from the
                // project file: package and assembly names are not namespace names.
                yield! projectNamespaces |> List.map (fun namespaceName -> $"open {namespaceName}")
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

    /// One executed example with the output it produced and the two parts of its cost.
    type ExampleResult =
        { Output: string
          Expected: string option
          Source: string
          /// <summary>Time spent creating this example's FSI session, in milliseconds.</summary>
          SessionMs: float
          /// <summary>Time spent evaluating this example's blocks, in milliseconds.</summary>
          EvalMs: float }

    /// How many examples one independent batch sends to a worker. Bounds the blast radius of a
    /// single timeout, since the worker's limit kills the whole process.
    let private batchSize () =
        match Environment.GetEnvironmentVariable TranscriptHostClient.BatchSizeVariable with
        | configured when not (String.IsNullOrWhiteSpace configured) ->
            match Int32.TryParse configured with
            | true, size when size > 0 -> size
            | _ -> invalidOp $"{TranscriptHostClient.BatchSizeVariable} must be a positive integer, not '{configured}'."
        | _ -> 16

    /// How many batch workers may run at once. Kept small because each worker loads the F# compiler
    /// and its own FSI sessions; FsLiveDocs release capture is memory-sensitive.
    let private workerLimit (batchCount: int) =
        let configured =
            match Environment.GetEnvironmentVariable TranscriptHostClient.WorkerLimitVariable with
            | value when not (String.IsNullOrWhiteSpace value) ->
                match Int32.TryParse value with
                | true, limit when limit > 0 -> limit
                | _ -> invalidOp $"{TranscriptHostClient.WorkerLimitVariable} must be a positive integer, not '{value}'."
            | _ -> max 1 (min Environment.ProcessorCount 4)
        max 1 (min configured batchCount)

    /// <summary>
    /// Runs independent examples grouped by project graph, each in its own fresh FSI session.
    /// </summary>
    /// <remarks>
    /// One worker process handles one project's batch so dependency identities never mix, while
    /// batches from the same project may run concurrently in separate processes. Results are
    /// returned in input order regardless of completion order.
    /// </remarks>
    let runIndependent (contexts: DocTestExecutionContext list) : ExampleResult list =
        if contexts.IsEmpty then
            []
        else
            let batches =
                contexts
                |> List.indexed
                |> List.groupBy (fun (_, context) -> context.Project.ProjectPath)
                |> List.collect (fun (_, group) -> group |> List.chunkBySize (batchSize ()))

            use gate = new Threading.SemaphoreSlim(workerLimit batches.Length)

            let runBatch (batch: (int * DocTestExecutionContext) list) =
                async {
                    do! gate.WaitAsync() |> Async.AwaitTask
                    try
                        let requests = batch |> List.map (snd >> request)
                        let outputs = TranscriptHostClient.runFresh requests
                        return List.zip batch outputs
                    finally
                        gate.Release() |> ignore
                }

            batches
            |> List.map runBatch
            |> Async.Parallel
            |> Async.RunSynchronously
            |> Array.toList
            |> List.collect id
            |> List.map (fun ((index, context), (output, timing)) -> index, context, output, timing)
            |> List.sortBy (fun (index, _, _, _) -> index)
            |> List.map (fun (_, context, output, timing) ->
                let transcript, _, _ = script context
                { Output = output
                  Expected = transcript.ExpectedOutput
                  Source = transcript.DisplayText
                  SessionMs = timing.SessionMs
                  EvalMs = timing.EvalMs })
