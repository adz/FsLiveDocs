namespace FsLiveDocs.Cli

open System
open System.IO
open FsLiveDocs.Core
open FsLiveDocs.Runner

/// Owns release extraction, verification, renderer-neutral assembly, and capsule persistence.
module internal ReleaseCapture =

    type Request = {
        ProjectPaths: string list
        Version: string option
        OutputPath: string option
        DryRun: bool
        WarnAsError: bool
        Site: SiteConfig
        ToolVersion: string
        ReportProgress: string -> int -> int -> unit
        ReportAudit: DocAnalysis.Analysis -> unit
        ReportApiDiagnostics: bool -> ApiDiagnostic list -> unit
    }

    type Result = {
        Report: ReleaseCapsuleReport
        ReportPath: string option
        PlannedOutputPath: string
        DryRun: bool
    }

    let private currentRevision () =
        let startInfo = Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        use gitProcess = Diagnostics.Process.Start(startInfo)
        let revision = gitProcess.StandardOutput.ReadToEnd().Trim()
        gitProcess.WaitForExit()
        if gitProcess.ExitCode <> 0 || String.IsNullOrWhiteSpace revision then
            invalidOp "Release capture requires a Git commit so the capsule can record source provenance."
        revision

    let private captureAssets docsDir =
        Directory.GetFiles(docsDir, "*", SearchOption.AllDirectories)
        |> Array.filter (fun path -> not (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        |> Array.map (fun path -> Path.GetRelativePath(docsDir, path).Replace('\\', '/'), File.ReadAllBytes path)
        |> Array.toList

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
            for case in DocumentationDiscovery.generatedCases page.SelectedProject "" page.Relative page.Expanded externallyExecuted do
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
            DocAnalysis.analyzeWithProgress request.ReportProgress prelude request.ProjectPaths projectFingerprint package
        request.ReportAudit analysis
        if DocAnalysis.compilerFailureCount analysis <> 0 then
            invalidOp "Documentation contains uncovered or non-compiling F# blocks. Fix the mapped audit failures before capture."
        request.ReportApiDiagnostics request.WarnAsError apiDiagnostics
        if request.WarnAsError && not apiDiagnostics.IsEmpty then
            invalidOp "API documentation warnings were treated as errors because --warn-as-error was passed."

        let pages = DocAnalysis.pages request.ProjectPaths package
        let references =
            request.ProjectPaths
            |> List.map (ProjectResolver.resolve >> _.AssemblyPath)
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.distinct
        verifyExplicitCases request.ProjectPaths pages references

        let semantic = DocAnalysis.semanticArtifact analysis
        let api: ApiModelArtifact = { SchemaVersion = History.ApiModelSchemaVersion; Package = package }
        let contentPages =
            pages
            |> List.map (fun page ->
                { SourcePath = page.Relative; Metadata = page.Metadata; Markdown = page.Expanded })
        let outputPath = request.OutputPath |> Option.defaultValue $".livedocs/releases/{package.Version}.livedocs.zip"
        let actualOutputPath =
            if request.DryRun then
                Path.Combine(Path.GetTempPath(), "fslivedocs-dry-run-" + Guid.NewGuid().ToString("N") + ".zip")
            else outputPath
        let created =
            ReleaseCapsule.create actualOutputPath (currentRevision ()) request.ToolVersion api semantic request.Site contentPages (captureAssets "docs")
        let report = ReleaseCapsule.inspect actualOutputPath
        if report.Sha256 <> created.Sha256 then
            invalidOp "Release capsule checksum changed during post-write verification."

        let plannedOutputPath = Path.GetFullPath outputPath
        let publicReport = { report with Path = plannedOutputPath }
        if request.DryRun then
            File.Delete actualOutputPath
            { Report = publicReport; ReportPath = None; PlannedOutputPath = plannedOutputPath; DryRun = true }
        else
            let reportPath = outputPath + ".report.json"
            File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(publicReport, Newtonsoft.Json.Formatting.Indented, Serialization.jsonSettings))
            // A bare-checksum sidecar lets a CI publish step register the capsule with
            // `history add --sha256-file` instead of parsing tool output.
            File.WriteAllText(outputPath + ".sha256", publicReport.Sha256.ToLowerInvariant() + "\n")
            { Report = publicReport; ReportPath = Some(Path.GetFullPath reportPath); PlannedOutputPath = plannedOutputPath; DryRun = false }
