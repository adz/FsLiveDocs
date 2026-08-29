namespace FsLiveDocs.Cli

open System
open FsLiveDocs.Core

/// Static files and generated-source templates emitted by livedocs commands.
module internal Templates =

    [<Literal>]
    let HistoryIndex = """{
  "SchemaVersion": 1,
  "CurrentVersion": "0.0.0",
  "Entries": []
}
"""

    [<Literal>]
    let DocIndex = """---
title: Home
weight: 1
---

# Document your F# library

FsLiveDocs generates API reference pages and verifies F# examples with your project's compiler settings.

## Build the documentation

Replace the project path below, then run from your repository root:

```bash
dotnet build
livedocs audit
livedocs build
livedocs watch --host 127.0.0.1 --port 5000
```

Add an ordinary `fsharp` fence to a guide for compile-only verification. Use `run` only for intentional execution,
`transcript` for FSI input/output, `isolated` for standalone code, `prepare` for hidden setup, or
`no-check reason="..."` for deliberate pseudocode.

To capture a release after verification succeeds, run:

```bash
livedocs capture --version 1.0.0
```
"""

    [<Literal>]
    let GitHubWorkflow = """
# FsLiveDocs verifies documentation on every change, and on a v<semver> tag captures
# an immutable release capsule and records it in .livedocs/history.json.
#
# The tool never talks to a host. The steps marked "provider step" below do that with
# `gh` and `git`; replace them if you publish elsewhere. See:
#   docs/guides/continuous-integration.md
name: LiveDocs
on:
  pull_request:
  push:
    branches: [ main ]
    tags: [ 'v*' ]
permissions:
  contents: write
  pages: write
  id-token: write
concurrency:
  group: livedocs-${{ github.ref }}
  cancel-in-progress: false
jobs:
  documentation:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: dotnet tool restore
      - run: dotnet build --nologo

      - name: Verify documentation
        run: dotnet livedocs test --interactive false --banner false

      - name: Render the current site and release history
        if: github.ref == 'refs/heads/main'
        run: |
          dotnet livedocs build --interactive false --banner false
          if [ -s .livedocs/history.json ]; then
            dotnet livedocs build-history .livedocs/history.json --retry 3 --interactive false --banner false
            dotnet livedocs verify-output .livedocs/history.json --output output --interactive false --banner false
          fi
      - uses: actions/upload-pages-artifact@v3
        if: github.ref == 'refs/heads/main'
        with:
          path: output

      - name: Capture the release capsule
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          version="${GITHUB_REF_NAME#v}"
          name="${GITHUB_REPOSITORY#*/}"
          dotnet livedocs capture --version "$version" \
            --output "artifacts/$name-$version-livedocs.zip" \
            --interactive false --banner false

      - name: Check the candidate against the full release history
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          version="${GITHUB_REF_NAME#v}"
          name="${GITHUB_REPOSITORY#*/}"
          dotnet livedocs history-check \
            --capsule "artifacts/$name-$version-livedocs.zip" --version "$version" \
            --interactive false --banner false

      - name: Publish the capsule to the GitHub release   # provider step
        if: startsWith(github.ref, 'refs/tags/v')
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          version="${GITHUB_REF_NAME#v}"
          name="${GITHUB_REPOSITORY#*/}"
          gh release create "$GITHUB_REF_NAME" \
            "artifacts/$name-$version-livedocs.zip" \
            "artifacts/$name-$version-livedocs.zip.report.json" \
            --verify-tag --generate-notes

      - name: Record the release in history and commit it   # provider step (git)
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          version="${GITHUB_REF_NAME#v}"
          name="${GITHUB_REPOSITORY#*/}"
          branch="${{ github.event.repository.default_branch }}"
          url="https://github.com/$GITHUB_REPOSITORY/releases/download/$GITHUB_REF_NAME/$name-$version-livedocs.zip"
          git fetch origin "$branch"
          git switch "$branch"
          dotnet livedocs history-add --version "$version" --url "$url" \
            --sha256-file "artifacts/$name-$version-livedocs.zip.sha256" \
            --interactive false --banner false
          dotnet livedocs history-check --version "$version" --interactive false --banner false
          git config user.name  "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add .livedocs/history.json
          git commit -m "Record $version in the release history"
          git push origin "$branch"

  deploy-pages:
    if: github.ref == 'refs/heads/main'
    needs: documentation
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - name: Deploy GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v4
"""

    let snapshotProject eol projectReferences toolReferences =
        [ """<Project Sdk="Microsoft.NET.Sdk">"""
          ""
          "  <PropertyGroup>"
          "    <TargetFramework>net10.0</TargetFramework>"
          "    <IsPackable>false</IsPackable>"
          "  </PropertyGroup>"
          ""
          "  <ItemGroup>"
          "    <Compile Include=\"SnapshotTests.fs\" />"
          "  </ItemGroup>"
          ""
          "  <ItemGroup>"
          "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />"
          "    <PackageReference Include=\"FSharp.Core\" Version=\"10.1.201\" />"
          "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />"
          "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\" />"
          "    <PackageReference Include=\"Verify.Xunit\" Version=\"31.12.5\" />"
          "  </ItemGroup>"
          ""
          "  <ItemGroup>"
          projectReferences
          toolReferences
          "  </ItemGroup>"
          ""
          "</Project>" ]
        |> String.concat eol

    let xmlFacts (eol: string) (assemblyReferences: string) index (projectPath: string) (exampleNames: string list) =
        let projectName = IO.Path.GetFileNameWithoutExtension(projectPath)
        let escapedProjectPath = IO.Path.GetFullPath(projectPath).Replace("\"", "\"\"")
        let packageName = $"xmlPackage{index}"
        let facts =
            exampleNames
            |> List.map (fun exampleName ->
                let escapedName = exampleName.Replace("`", "'").Replace("\"", "\"\"")
                [ ""
                  "    [<Fact>]"
                  $"    let ``xml {projectName}#example-{escapedName}`` () ="
                  "        task {"
                  $"            let projectPath = @\"{escapedProjectPath}\""
                  $"            let references = [ {assemblyReferences} ]"
                  $"            let! snapshot = DocTestRunner.collectSnapshotByName {packageName}.Value projectPath references @\"{escapedName}\""
                  "            return! Verifier.Verify(snapshot)"
                  "        }" ]
                |> String.concat eol)
            |> String.concat eol
        [ $"    let private {packageName} = lazy (SymbolLister.extractFromProject @\"{escapedProjectPath}\" |> Async.RunSynchronously)"; facts ]
        |> String.concat eol

    let documentationFact eol assemblyReferences (case: GeneratedVerificationCase) =
        let quote (value: string) = value.Replace("\"", "\"\"")
        let escapedId = case.Id.Replace("`", "'") |> quote
        let encoded = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes case.ExpandedMarkdown)
        let action =
            match case.Action with
            | CompileUnit id -> $"CompileUnit @\"{quote id}\""
            | ExecuteBlock id -> $"ExecuteBlock @\"{quote id}\""
            | ExecuteTranscriptBlock id -> $"ExecuteTranscriptBlock @\"{quote id}\""
        [ ""
          "    [<Fact>]"
          $"    let ``documentation {escapedId}`` () ="
          $"        let projectPath = @\"{IO.Path.GetFullPath(case.ProjectPath) |> quote}\""
          $"        let references = [ {assemblyReferences} ]"
          $"        let markdown = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(\"{encoded}\"))"
          $"        let case = {{ Id = @\"{quote case.Id}\"; ProjectPath = projectPath; SourcePath = @\"{quote case.SourcePath}\"; ExpandedMarkdown = markdown; Action = {action} }}"
          "        GeneratedVerification.runCase references case |> Async.RunSynchronously" ]
        |> String.concat eol

    let snapshotTests eol xmlFacts documentationFacts =
        [ "namespace FsLiveDocs.SnapshotTests"
          ""
          "open System.Threading.Tasks"
          "open FsLiveDocs.Core"
          "open FsLiveDocs.Runner"
          "open VerifyXunit"
          "open Xunit"
          ""
          "module SnapshotTests ="
          xmlFacts
          documentationFacts ]
        |> String.concat eol
