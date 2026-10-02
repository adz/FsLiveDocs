namespace FsLiveDocs.Cli

open System
open System.Diagnostics
open System.IO
open FsLiveDocs.Core
open FsLiveDocs.Runner

/// <summary>
/// One documentation example's verification outcome, shared by <c>test</c> and <c>capture</c>.
/// </summary>
type internal VerificationOutcome =
    { Id: string
      Project: string
      Mode: string
      Kind: string
      Passed: bool
      Message: string option
      DurationMs: float
      /// <summary>True when a deterministic example reused a cached passing result.</summary>
      Cached: bool }

/// <summary>
/// The complete result of one documentation verification pass: compiler audit plus every executed
/// snapshot and Markdown example.
/// </summary>
type internal VerificationResult =
    { Package: PackageModel
      ProjectFingerprint: string
      Analysis: DocAnalysis.Analysis
      Pages: DocAnalysis.Page list
      ApiDiagnostics: ApiDiagnostic list
      Outcomes: VerificationOutcome list }

type internal VerificationRequest =
    { ProjectPaths: string list
      Version: string option
      DocsSets: DocsSet list option
      Prelude: string
      WarnAsError: bool
      ReportProgress: string -> int -> int -> unit
      ReportAudit: DocAnalysis.Analysis -> unit
      ReportApiDiagnostics: bool -> ApiDiagnostic list -> unit
      ExecuteExamples: bool }

/// <summary>
/// The single verification implementation behind <c>test</c> and <c>capture</c>. Case selection
/// and execution order live here so the two commands cannot drift.
/// </summary>
/// <remarks>
/// Extraction and the compiler audit run once. The resolved pages and package flow into example
/// execution, which no longer re-walks the documentation tree, re-parses Markdown, or recompiles
/// a unit the audit already checked. Callers apply their own failure policy to the returned
/// outcomes; this module never turns an example failure into process exit.
/// </remarks>
module internal Verification =

    let private modeName = function
        | Page -> "page"
        | Prepare -> "prepare"
        | Isolated -> "isolated"
        | Run -> "run"
        | Transcript -> "transcript"
        | NoCheck _ -> "no-check"

    let private referencesFor (projectPaths: string list) =
        projectPaths
        |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> List.distinct

    /// Snapshot-selected XML examples, grouped one worker per project (a shared FSI session).
    let private executeSnapshots (projectPaths: string list) (references: string list) =
        [ for projectPath in projectPaths do
              let projectPackage = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously

              if not (DocTestRunner.snapshotExampleNames projectPackage).IsEmpty then
                  let stopwatch = Stopwatch.StartNew()
                  let snapshots = DocTestRunner.collectSnapshots projectPackage projectPath references |> Async.RunSynchronously
                  let groupMs = stopwatch.Elapsed.TotalMilliseconds

                  let passed =
                      snapshots.Examples
                      |> List.forall (fun example ->
                          match example.Status with
                          | ExampleStatus.Verified
                          | ExampleStatus.FirstCut -> true
                          | _ -> false)

                  Timing.case projectPath projectPath "snapshot" "snapshot-group" groupMs (if passed then "pass" else "fail") None

                  for snapshot in snapshots.Examples do
                      let expected = snapshot.ExpectedOutput |> Option.defaultValue ""
                      let passed, message =
                          match snapshot.Status with
                          | ExampleStatus.Verified
                          | ExampleStatus.FirstCut -> true, None
                          | ExampleStatus.Mismatch -> false, Some $"Expected:{Environment.NewLine}{expected}{Environment.NewLine}Actual:{Environment.NewLine}{snapshot.ActualOutput}"
                          | ExampleStatus.Error -> false, Some snapshot.ActualOutput

                      yield
                          { Id = snapshot.Name
                            Project = projectPath
                            Mode = "snapshot"
                            Kind = "snapshot"
                            Passed = passed
                            Message = message
                            DurationMs = 0.0
                            Cached = false } ]

    /// Independent Markdown `run` and `transcript` blocks. Each is executed without recompiling
    /// the owning unit: the audit that ran before execution proved every block compiles. Blocks are
    /// batched by project graph, one fresh FSI session each, with bounded worker concurrency.
    let private fileIdentity (path: string) =
        if String.IsNullOrWhiteSpace path then ""
        elif File.Exists path then
            let info = FileInfo path
            $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
        else
            path

    /// Everything that can change an execution result except the block itself.
    let private executionIdentity (references: string list) (projectFingerprint: string) =
        let toolIdentity =
            [ typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly.ManifestModule.ModuleVersionId |> string
              typeof<FsLiveDocs.TranscriptHost.TranscriptRequest>.Assembly.ManifestModule.ModuleVersionId |> string
              typeof<FSharp.Compiler.CodeAnalysis.FSharpChecker>.Assembly.ManifestModule.ModuleVersionId |> string ]
            |> String.concat ","

        [ yield $"tool:{toolIdentity}"
          yield $"project-inputs:{projectFingerprint}"
          for reference in references do
              yield "reference:" + fileIdentity reference ]

    /// Independent Markdown `run` and `transcript` blocks. Each is executed without recompiling
    /// the owning unit: the audit that ran before execution proved every block compiles. Blocks are
    /// batched by project graph, one fresh FSI session each, with bounded worker concurrency. A
    /// block that declared `deterministic` reuses a cached passing result when the identity matches.
    let private executeMarkdown (pages: DocAnalysis.Page list) (references: string list) (projectFingerprint: string) =
        let targets =
            [ for page in pages do
                  let externallyExecuted =
                      page.Blocks
                      |> List.choose (fun block ->
                          match block.Mode, block.Origin with
                          | (Run | Transcript), XmlExample -> Some block.Id
                          | _ -> None)
                      |> Set.ofList

                  for verificationCase in DocumentationDiscovery.verificationCases page.SelectedProject page.Prelude page.Blocks do
                      match verificationCase with
                      | Execute block when not (externallyExecuted.Contains block.Id) -> yield page, block
                      | ExecuteTranscript block when not (externallyExecuted.Contains block.Id) -> yield page, block
                      | _ -> () ]

        if targets.IsEmpty then
            []
        else
            let commonIdentity = executionIdentity references projectFingerprint

            let projectIdentities =
                targets
                |> List.map (fun (page, _) -> page.SelectedProject)
                |> List.distinct
                |> List.map (fun projectPath ->
                    let resolved = ProjectResolver.resolve projectPath
                    projectPath, $"project:{projectPath}|assembly:{fileIdentity resolved.AssemblyPath}")
                |> Map.ofList

            let planned =
                targets
                |> List.mapi (fun index (page, block) ->
                    let cacheKey =
                        if not block.Deterministic then
                            None
                        else
                            let content, expected = GeneratedVerification.executionPayload page.Blocks block
                            let expectedText = expected |> Option.defaultValue ""

                            Some(
                                ExecutionCache.key
                                    [ yield! commonIdentity
                                      yield projectIdentities.[page.SelectedProject]
                                      yield $"prelude:{page.Prelude}"
                                      yield $"block:{block.Id}"
                                      yield $"source:{block.SourceHash}"
                                      yield $"content:{content}"
                                      yield $"expected:{expectedText}" ])

                    index, page, block, cacheKey)

            let cached =
                planned
                |> List.choose (fun (index, page, block, cacheKey) ->
                    match cacheKey with
                    | Some key ->
                        ExecutionCache.tryRead key
                        |> Option.map (fun output -> index, page, block, output)
                    | None -> None)

            let cachedIndexes = cached |> List.map (fun (index, _, _, _) -> index) |> Set.ofList
            let toRun = planned |> List.filter (fun (index, _, _, _) -> not (cachedIndexes.Contains index))

            let runResults =
                if toRun.IsEmpty then
                    []
                else
                    GeneratedVerification.executeDiscoveredBlocks
                        references
                        (toRun |> List.map (fun (_, page, block, _) -> page.Blocks, block))

            let runOutcomes =
                (toRun, runResults)
                ||> List.map2 (fun (index, page, block, cacheKey) result -> index, page, block, cacheKey, result)

            // Cache only passes; a failure must re-run so the next invocation sees the current error.
            for _, _, _, cacheKey, result in runOutcomes do
                match cacheKey with
                | Some key when result.Passed -> ExecutionCache.write key result.Output
                | _ -> ()

            [ for index, page, block, _output in cached do
                  let mode = modeName block.Mode
                  Timing.case block.Id page.SelectedProject mode "markdown" 0.0 "pass" (Some "cached")

                  yield
                      index,
                      { Id = block.Id
                        Project = page.SelectedProject
                        Mode = mode
                        Kind = "markdown"
                        Passed = true
                        Message = None
                        DurationMs = 0.0
                        Cached = true }

              for index, page, block, _, result in runOutcomes do
                  let mode = modeName block.Mode
                  let durationMs = result.SessionMs + result.EvalMs

                  Timing.case
                      block.Id
                      page.SelectedProject
                      mode
                      "markdown"
                      durationMs
                      (if result.Passed then "pass" else "fail")
                      (Some $"session {result.SessionMs:N0}ms")

                  yield
                      index,
                      { Id = block.Id
                        Project = page.SelectedProject
                        Mode = mode
                        Kind = "markdown"
                        Passed = result.Passed
                        Message = result.Message
                        DurationMs = durationMs
                        Cached = false } ]
            |> List.sortBy fst
            |> List.map snd

    let private analyze (request: VerificationRequest) (package: PackageModel) projectFingerprint =
        Timing.measure "Compiler audit" None (fun () ->
            match request.DocsSets with
            | Some sets ->
                DocAnalysis.analyzeDocsSetsWithProgress request.ReportProgress sets request.ProjectPaths projectFingerprint package
            | None ->
                DocAnalysis.analyzeWithProgress request.ReportProgress request.Prelude request.ProjectPaths projectFingerprint package)

    /// Runs extraction, the compiler audit, and example execution exactly once.
    let run (request: VerificationRequest) : VerificationResult =
        let extracted, apiDiagnostics, projectFingerprint =
            Timing.measure "Extract API documentation" None (fun () ->
                PackageExtraction.extractCachedWithProgress request.ReportProgress request.Prelude request.ProjectPaths)

        let package =
            { extracted with Version = request.Version |> Option.defaultValue extracted.Version }

        let analysis = analyze request package projectFingerprint
        request.ReportAudit analysis
        request.ReportApiDiagnostics request.WarnAsError apiDiagnostics

        let references = referencesFor request.ProjectPaths

        let outcomes =
            if not request.ExecuteExamples then
                []
            elif DocAnalysis.compilerFailureCount analysis <> 0 then
                // Execution depends on every block compiling; the audit has already reported why.
                []
            else
                Timing.measure "Execute examples" None (fun () ->
                    [ yield! executeSnapshots request.ProjectPaths references
                      yield! executeMarkdown analysis.Pages references projectFingerprint ])

        { Package = package
          ProjectFingerprint = projectFingerprint
          Analysis = analysis
          Pages = analysis.Pages
          ApiDiagnostics = apiDiagnostics
          Outcomes = outcomes }
