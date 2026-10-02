namespace FsLiveDocs.Tests

open System
open System.IO
open Xunit
open FsLiveDocs.Runner

/// Tests that change the process's working directory, so they must not run beside other tests.
[<CollectionDefinition("Working directory", DisableParallelization = true)>]
type WorkingDirectoryCollection() = class end

[<Collection("Working directory")>]
module WorkerIsolationTests =

    /// Compiles one F# source file to a library in `directory`, referencing the running framework and `references`.
    let private compileLibrary (directory: string) (name: string) (source: string) (references: string list) =
        let sourcePath = Path.Combine(directory, $"{name}.fs")
        let outputPath = Path.Combine(directory, $"{name}.dll")
        File.WriteAllText(sourcePath, source)

        let framework =
            Directory.GetFiles(Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            |> Array.filter (fun path ->
                let file = Path.GetFileName path
                file.StartsWith("System.") || file = "netstandard.dll" || file = "mscorlib.dll")
            |> Array.toList

        let arguments =
            [ "fsc.exe"; $"-o:{outputPath}"; "-a"; "--noframework"; "--targetprofile:netcore"; "--nowarn:3370" ]
            @ [ $"-r:{typeof<list<int>>.Assembly.Location}" ]
            @ (framework @ references |> List.map (fun reference -> $"-r:{reference}"))
            @ [ sourcePath ]
            |> List.toArray

        let diagnostics, error =
            FSharp.Compiler.CodeAnalysis.FSharpChecker.Create().Compile(arguments) |> Async.RunSynchronously

        match error with
        | Some exn -> raise exn
        | None ->
            let errors =
                diagnostics
                |> Array.filter (fun diagnostic -> diagnostic.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
            Assert.True(errors.Length = 0, $"%A{errors}")

        outputPath

    [<Fact>]
    let ``a documented project's dependencies are not resolved from the tool's folder`` () =
        // The tool ships its own Axial beside the worker. A documented Axial package referenced before its Axial.dll
        // once type-checked against that copy, so APIs newer than the tool's Axial were "not defined".
        let runnerDirectory = Path.GetDirectoryName(typeof<FsiTranscriptRunner.DocTestExecutionContext>.Assembly.Location)
        Assert.True(File.Exists(Path.Combine(runnerDirectory, "Axial.dll")), "The tool's Axial must sit beside the worker for this test to mean anything.")

        let coreProject = ProjectResolver.resolveProjectPath "FsLiveDocs.Core.fsproj"
        let directory = Path.Combine(Path.GetTempPath(), "fslivedocs-tests", Guid.NewGuid().ToString "N")
        Directory.CreateDirectory directory |> ignore
        // The compiler also resolves from the working directory. Under the test runner that is the output folder, which
        // holds the tool's Axial too; `livedocs` runs from the documented repository instead.
        let workingDirectory = Environment.CurrentDirectory
        Environment.CurrentDirectory <- Directory.CreateDirectory(Path.Combine(directory, "empty")).FullName

        try
            // Versioned below the tool's Axial, as a consumer's development build often is, so the tool's copy can
            // satisfy the reference.
            let axial =
                compileLibrary
                    directory
                    "Axial"
                    "namespace Axial\n\ntype DocumentedOnly = static member Value = 42\n\nmodule AssemblyInfo =\n    [<assembly: System.Reflection.AssemblyVersion(\"0.0.1.0\")>]\n    do ()\n"
                    []

            // Exposing an Axial type makes the compiler resolve Axial as soon as Dependent is referenced.
            let dependent =
                compileLibrary
                    directory
                    "Dependent"
                    "module Dependent\n\ntype Wrapper = { Inner: Axial.DocumentedOnly option }\n\nlet value () = Axial.DocumentedOnly.Value\n"
                    [ axial ]

            let context : FsiTranscriptRunner.DocTestExecutionContext =
                { Project = { ProjectPath = coreProject; AssemblyPath = dependent; ProjectNamespace = "Dependent" }
                  References = []
                  Scenario = None
                  Example =
                    { Name = "isolation"
                      Content = "> Axial.DocumentedOnly.Value + Dependent.value ();;\nval it: int = 84"
                      ExpectedOutput = None
                      Scenario = None
                      IsSnapshotTest = false
                      NoCheckReason = None } }

            let output, _, _ = FsiTranscriptRunner.runExample context
            Assert.Equal("val it: int = 84", output)
        finally
            Environment.CurrentDirectory <- workingDirectory
            Directory.Delete(directory, true)

    [<Fact>]
    let ``exported namespaces come from assembly metadata, not the file name`` () =
        let directory = Path.Combine(Path.GetTempPath(), "fslivedocs-tests", Guid.NewGuid().ToString "N")
        Directory.CreateDirectory directory |> ignore
        try
            // The assembly is named MyPackage, but its source declares namespace FsLiveDocs.
            let library = compileLibrary directory "MyPackage" "namespace FsLiveDocs\nmodule Api = let value = 42" []
            let namespaces = ProjectResolver.exportedRootNamespaces library
            Assert.Contains("FsLiveDocs", namespaces)
            Assert.DoesNotContain("MyPackage", namespaces)
        finally
            Directory.Delete(directory, true)
