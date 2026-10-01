namespace FsLiveDocs.Cli

open System
open System.Diagnostics
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
      DurationMs: float }

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

                  Timing.case projectPath projectPath "snapshot" "snapshot-group" groupMs (if passed then "pass" else "fail")

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
                            DurationMs = 0.0 } ]

    /// Independent Markdown `run` and `transcript` blocks. Each is executed without recompiling
    /// the owning unit: the audit that ran before execution proved every block compiles.
    let private executeMarkdown (pages: DocAnalysis.Page list) (references: string list) =
        [ for page in pages do
              let externallyExecuted =
                  page.Blocks
                  |> List.choose (fun block ->
                      match block.Mode, block.Origin with
                      | (Run | Transcript), XmlExample -> Some block.Id
                      | _ -> None)
                  |> Set.ofList

              let executable =
                  DocumentationDiscovery.verificationCases page.SelectedProject page.Prelude page.Blocks
                  |> List.choose (function
                      | Execute block when not (externallyExecuted.Contains block.Id) -> Some block
                      | ExecuteTranscript block when not (externallyExecuted.Contains block.Id) -> Some block
                      | _ -> None)

              for block in executable do
                  let stopwatch = Stopwatch.StartNew()

                  let passed, message =
                      try
                          GeneratedVerification.executeDiscoveredBlock references page.Blocks block
                          true, None
                      with error ->
                          false, Some error.Message

                  Timing.case
                      block.Id
                      page.SelectedProject
                      (modeName block.Mode)
                      "markdown"
                      stopwatch.Elapsed.TotalMilliseconds
                      (if passed then "pass" else "fail")

                  yield
                      { Id = block.Id
                        Project = page.SelectedProject
                        Mode = modeName block.Mode
                        Kind = "markdown"
                        Passed = passed
                        Message = message
                        DurationMs = stopwatch.Elapsed.TotalMilliseconds } ]

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
                      yield! executeMarkdown analysis.Pages references ])

        { Package = package
          ProjectFingerprint = projectFingerprint
          Analysis = analysis
          Pages = analysis.Pages
          ApiDiagnostics = apiDiagnostics
          Outcomes = outcomes }
