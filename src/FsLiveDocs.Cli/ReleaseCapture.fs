namespace FsLiveDocs.Cli

open System
open System.IO
open Axial
open Axial.FileSystem
open Reified
open FsLiveDocs.Core
open FsLiveDocs.Runner
open FsLiveDocs.Core.Effects
open FsLiveDocs.Core.Schema

/// Owns release extraction, verification, renderer-neutral assembly, and capsule persistence.
module internal ReleaseCapture =

    let private run description flow = Run.orRaise FileSystemError.describe description flow

    let private reportCodec = Json.compile ReleaseSchema.releaseCapsuleReport

    type Request =
        {
            ProjectPaths: string list
            Version: string option
            OutputPath: string option
            DryRun: bool
            WarnAsError: bool
            Site: SiteConfig
            /// None selects the historical single-tree pipeline; Some uses all configured sets.
            DocsSets: DocsSet list option
            ToolVersion: string
            ReportProgress: string -> int -> int -> unit
            ReportAudit: DocAnalysis.Analysis -> unit
            ReportApiDiagnostics: bool -> ApiDiagnostic list -> unit
            ReportLinkWarnings: bool -> string list -> unit
        }

    type Result =
        { Report: ReleaseCapsuleReport
          ReportPath: string option
          PlannedOutputPath: string
          DryRun: bool }

    let private currentRevision () = Git.currentRevision (Directory.GetCurrentDirectory())

    /// Runs the complete release-capture policy. Reporting functions may present progress, but
    /// cannot weaken compiler, warning, execution, provenance, or integrity checks.
    let capture (request: Request) =
        if request.ProjectPaths.IsEmpty then invalidOp "Release capture requires at least one project path."
        let prelude = request.Site.FSharpPrelude |> Option.defaultValue ""

        // Capture owns the single release verification pass: it audits and executes examples once
        // and then assembles the capsule from the same result, instead of running `test` first and
        // executing every example a second time.
        let verification =
            Verification.run
                { ProjectPaths = request.ProjectPaths
                  Version = request.Version
                  DocsSets = request.DocsSets
                  Prelude = prelude
                  WarnAsError = request.WarnAsError
                  ReportProgress = request.ReportProgress
                  ReportAudit = request.ReportAudit
                  ReportApiDiagnostics = request.ReportApiDiagnostics
                  ExecuteExamples = true }

        let package = verification.Package
        let analysis = verification.Analysis

        if DocAnalysis.compilerFailureCount analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before capture."
        if request.WarnAsError && not verification.ApiDiagnostics.IsEmpty then
            invalidOp "API documentation warnings were treated as errors because --warn-as-error was passed."

        let failed = verification.Outcomes |> List.filter (fun outcome -> not outcome.Passed)
        if not failed.IsEmpty then
            let detail =
                failed
                |> List.truncate 5
                |> List.map (fun outcome ->
                    let message = outcome.Message |> Option.defaultValue "failed"
                    $"  {outcome.Id}: {message}")
                |> String.concat Environment.NewLine
            invalidOp $"Documentation examples failed before capture:{Environment.NewLine}{detail}"

        // Materialize compiler-derived meaning before the capsule is assembled.
        let semantic = DocAnalysis.semanticArtifact analysis

        let resolvedSets =
            request.DocsSets
            |> Option.defaultValue
                [ DocsSet.implicit request.Site.SiteName request.ProjectPaths request.Site.FSharpPrelude ]

        // The audit already resolved these pages; reuse them rather than scanning the tree again.
        let pages = verification.Pages

        let prepared =
            DocumentationSets.prepareCurrentWithSourceLinks
                request.Site request.DocsSets.IsSome resolvedSets package semantic ""
        request.ReportLinkWarnings request.WarnAsError prepared.Warnings

        let api: ApiModelArtifact =
            { SchemaVersion = History.ApiModelSchemaVersion
              Package = package }

        let contentPages =
            pages
            |> List.map (fun page ->
                { SourcePath = page.SourcePath
                  SetId = page.SetId
                  Metadata = page.Metadata
                  Markdown = page.Expanded })

        let outputPath =
            request.OutputPath
            |> Option.defaultValue $".livedocs/releases/{package.Version}.livedocs.zip"

        let actualOutputPath =
            if request.DryRun then
                Path.Combine(Path.GetTempPath(), "fslivedocs-dry-run-" + Guid.NewGuid().ToString("N") + ".zip")
            else outputPath
        let capsulePhase = Timing.beginPhase "Write release capsule" None
        let created =
            match request.DocsSets with
            | Some _ ->
                ReleaseCapsule.createWithDocsSets
                    actualOutputPath
                    (currentRevision ())
                    request.ToolVersion
                    api
                    semantic
                    request.Site
                    prepared.Sets
                    contentPages
                    (DocumentationSets.captureAssets prepared)
            | None ->
                ReleaseCapsule.create
                    actualOutputPath
                    (currentRevision ())
                    request.ToolVersion
                    api
                    semantic
                    request.Site
                    contentPages
                    (DocumentationSets.captureAssets prepared)

        let report = ReleaseCapsule.inspect actualOutputPath
        if report.Sha256 <> created.Sha256 then
            invalidOp "Release capsule checksum changed during post-write verification."

        let plannedOutputPath = Path.GetFullPath outputPath
        let publicReport = { report with Path = plannedOutputPath }
        Timing.endPhase capsulePhase
        if request.DryRun then
            run $"Could not remove the dry-run capsule at {actualOutputPath}" (FileSystem.deleteFile actualOutputPath)
            { Report = publicReport; ReportPath = None; PlannedOutputPath = plannedOutputPath; DryRun = true }
        else
            let reportPath = outputPath + ".report.json"
            let reportJson = Json.serialize reportCodec publicReport
            let sha256Path = outputPath + ".sha256"
            // A bare-checksum sidecar lets a CI publish step register the capsule with
            // `history add --sha256-file` instead of parsing tool output. Both writes are one
            // Flow, run once -- not two independently-run operations -- so they share one
            // execution boundary the way a caller composing this into a larger workflow expects.
            let writeArtifacts =
                flow {
                    do! FileSystem.writeAllText reportPath reportJson
                    do! FileSystem.writeAllText sha256Path (publicReport.Sha256.ToLowerInvariant() + "\n")
                }
            run $"Could not write the release report and checksum sidecar for {outputPath}" writeArtifacts
            { Report = publicReport; ReportPath = Some(Path.GetFullPath reportPath); PlannedOutputPath = plannedOutputPath; DryRun = false }
