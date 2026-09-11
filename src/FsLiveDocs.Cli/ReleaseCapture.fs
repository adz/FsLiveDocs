namespace FsLiveDocs.Cli

open System
open System.IO
open Axial
open Axial.FileSystem
open FsLiveDocs.Core
open FsLiveDocs.Runner

/// Owns release extraction, verification, renderer-neutral assembly, and capsule persistence.
module internal ReleaseCapture =

    type private RunnerEnvironment =
        { FileSystem: IFileSystem }
        interface IHasFileSystem with
            member this.FileSystem = this.FileSystem

    let private environment : RunnerEnvironment = { FileSystem = FileSystem.live }

    /// Runs a composed file-system Flow synchronously, raising a clear diagnostic on any typed
    /// failure instead of letting a raw I/O exception (disk full, permissions) escape uncaught.
    /// Callers compose every step of one artifact-writing decision into a single Flow first, so
    /// this runs once per decision -- not once per underlying file operation.
    let private run (description: string) (flow: Flow<RunnerEnvironment, FileSystemError, 'value>) =
        match flow |> Flow.run environment with
        | Exit.Success value -> value
        | Exit.Failure(Cause.Fail error) -> invalidOp $"{description}: {FileSystemError.describe error}"
        | Exit.Failure cause -> invalidOp $"{description}: {cause}"

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
        }

    type Result =
        { Report: ReleaseCapsuleReport
          ReportPath: string option
          PlannedOutputPath: string
          DryRun: bool }

    let private currentRevision () = Git.currentRevision (Directory.GetCurrentDirectory())

    let private verifyExplicitCases projectPaths (pages: DocAnalysis.Page list) references =
        for projectPath in projectPaths do
            let projectPackage = SymbolLister.extractFromProject projectPath |> Async.RunSynchronously
            for name in DocTestRunner.snapshotExampleNames projectPackage do
                let snapshot = DocTestRunner.collectSnapshotByName projectPackage projectPath references name |> Async.RunSynchronously
                match snapshot.Status with
                | ExampleStatus.Verified | ExampleStatus.FirstCut -> ()
                | ExampleStatus.Mismatch ->
                    invalidOp $"XML example {name} output did not match its expected release output."
                | ExampleStatus.Error ->
                    invalidOp $"XML example {name} failed during release capture: {snapshot.ActualOutput}"

        for page in pages do
            let externallyExecuted =
                page.Blocks
                |> List.choose (fun block ->
                    match block.Mode, block.Origin with
                    | (Run | Transcript), XmlExample -> Some block.Id
                    | _ -> None)
                |> Set.ofList

            for case in
                DocumentationDiscovery.generatedCases
                    page.SelectedProject
                    page.Prelude
                    page.Relative
                    page.Expanded
                    externallyExecuted do
                match case.Action with
                | ExecuteBlock _ | ExecuteTranscriptBlock _ ->
                    GeneratedVerification.runCase references case |> Async.RunSynchronously
                | CompileUnit _ -> ()

    /// Runs the complete release-capture policy. Reporting functions may present progress, but
    /// cannot weaken compiler, warning, execution, provenance, or integrity checks.
    let capture (request: Request) =
        if request.ProjectPaths.IsEmpty then invalidOp "Release capture requires at least one project path."
        let prelude = request.Site.FSharpPrelude |> Option.defaultValue ""
        let extracted, apiDiagnostics, projectFingerprint =
            PackageExtraction.extractCachedWithProgress request.ReportProgress prelude request.ProjectPaths
        let package = { extracted with Version = request.Version |> Option.defaultValue extracted.Version }
        let analysis =
            match request.DocsSets with
            | Some sets ->
                DocAnalysis.analyzeDocsSetsWithProgress
                    request.ReportProgress
                    sets
                    request.ProjectPaths
                    projectFingerprint
                    package
            | None ->
                DocAnalysis.analyzeWithProgress
                    request.ReportProgress
                    prelude
                    request.ProjectPaths
                    projectFingerprint
                    package

        request.ReportAudit analysis
        if DocAnalysis.compilerFailureCount analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before capture."
        request.ReportApiDiagnostics request.WarnAsError apiDiagnostics
        if request.WarnAsError && not apiDiagnostics.IsEmpty then
            invalidOp "API documentation warnings were treated as errors because --warn-as-error was passed."

        let resolvedSets =
            request.DocsSets
            |> Option.defaultValue
                [ DocsSet.implicit request.Site.SiteName request.ProjectPaths request.Site.FSharpPrelude ]

        let pages =
            match request.DocsSets with
            | Some sets -> DocAnalysis.pagesForDocsSets sets request.ProjectPaths package
            | None ->
                DocAnalysis.pages request.ProjectPaths package
                |> List.map (fun page -> { page with Prelude = prelude })

        let references =
            request.ProjectPaths
            |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.distinct
        verifyExplicitCases request.ProjectPaths pages references

        let semantic = DocAnalysis.semanticArtifact analysis
        let prepared = DocumentationSets.prepareCurrent request.DocsSets.IsSome resolvedSets package semantic ""

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
        if request.DryRun then
            run $"Could not remove the dry-run capsule at {actualOutputPath}" (FileSystem.deleteFile actualOutputPath)
            { Report = publicReport; ReportPath = None; PlannedOutputPath = plannedOutputPath; DryRun = true }
        else
            let reportPath = outputPath + ".report.json"
            let reportJson = Newtonsoft.Json.JsonConvert.SerializeObject(publicReport, Newtonsoft.Json.Formatting.Indented, Serialization.jsonSettings)
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
