namespace FsLiveDocs.SnapshotTests

open System.Threading.Tasks
open FsLiveDocs.Core
open FsLiveDocs.Runner
open VerifyXunit
open Xunit

module SnapshotTests =
    let private xmlPackage0 = lazy (SymbolLister.extractFromProject @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/samples/DeepReference/Acme.Docs/Acme.Docs.fsproj" |> Async.RunSynchronously)

    [<Fact>]
    let ``xml Acme.Docs#example-PreferredCustomerPrice`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/samples/DeepReference/Acme.Docs/Acme.Docs.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage0.Value projectPath references @"PreferredCustomerPrice"
            return! Verifier.Verify(snapshot)
        }

    [<Fact>]
    let ``xml Acme.Docs#example-CalculateTotal`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/samples/DeepReference/Acme.Docs/Acme.Docs.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage0.Value projectPath references @"CalculateTotal"
            return! Verifier.Verify(snapshot)
        }

    let private xmlPackage1 = lazy (SymbolLister.extractFromProject @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj" |> Async.RunSynchronously)


    let private xmlPackage2 = lazy (SymbolLister.extractFromProject @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj" |> Async.RunSynchronously)

    [<Fact>]
    let ``xml FsLiveDocs.Core#example-ResolveSnippetExample`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage2.Value projectPath references @"ResolveSnippetExample"
            return! Verifier.Verify(snapshot)
        }

    [<Fact>]
    let ``xml FsLiveDocs.Core#example-DocScenarioUsage`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage2.Value projectPath references @"DocScenarioUsage"
            return! Verifier.Verify(snapshot)
        }

    [<Fact>]
    let ``xml FsLiveDocs.Core#example-UserGreeting`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage2.Value projectPath references @"UserGreeting"
            return! Verifier.Verify(snapshot)
        }

    [<Fact>]
    let ``xml FsLiveDocs.Core#example-ExtractExamplesExample`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage2.Value projectPath references @"ExtractExamplesExample"
            return! Verifier.Verify(snapshot)
        }

    [<Fact>]
    let ``xml FsLiveDocs.Core#example-MapTranscript`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage2.Value projectPath references @"MapTranscript"
            return! Verifier.Verify(snapshot)
        }

    [<Fact>]
    let ``xml FsLiveDocs.Core#example-CreateExample`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage2.Value projectPath references @"CreateExample"
            return! Verifier.Verify(snapshot)
        }

    let private xmlPackage3 = lazy (SymbolLister.extractFromProject @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj" |> Async.RunSynchronously)

    [<Fact>]
    let ``xml FsLiveDocs.Renderer#example-GenerateLlmsTxtExample`` () =
        task {
            let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj"
            let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
            let! snapshot = DocTestRunner.collectSnapshotByName xmlPackage3.Value projectPath references @"GenerateLlmsTxtExample"
            return! Verifier.Verify(snapshot)
        }

    let private xmlPackage4 = lazy (SymbolLister.extractFromProject @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj" |> Async.RunSynchronously)


    [<Fact>]
    let ``documentation 02-guides/01-verified-examples.md#page`` () =
        let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/samples/DeepReference/Acme.Docs/Acme.Docs.fsproj"
        let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
        let markdown = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("CiMgVmVyaWZ5IEYjIGV4YW1wbGVzCgpDaG9vc2UgdGhlIGxlYXN0IHBvd2VyZnVsIG1vZGUgdGhhdCBwcm92ZXMgeW91ciBkb2N1bWVudGF0aW9uIGNsYWltLiBPcmRpbmFyeSBleGFtcGxlcyBjb21waWxlIGJ1dCBkbyBub3QgZXhlY3V0ZS4KCiMjIENvbXBpbGUgYSBwYWdlCgpVc2Ugb3JkaW5hcnkgYGZzaGFycGAgZmVuY2VzIGZvciBwcm9ncmVzc2l2ZSBleGFtcGxlczoKCmBgYGBtYXJrZG93bgpgYGBmc2hhcnAKdHlwZSBPcmRlciA9IHsgVG90YWw6IGRlY2ltYWwgfQpgYGAKCmBgYGZzaGFycApsZXQgb3JkZXIgPSB7IFRvdGFsID0gMTIwTSB9CmBgYApgYGBgCgpGc0xpdmVEb2NzIGNvbXBpbGVzIHRoZXNlIGJsb2NrcyBpbiBvbmUgcGFnZSB1bml0LiBJdCBuZXZlciBleGVjdXRlcyB0aGVtLgoKIyMgQ29tcGlsZSBhbiBpbmRlcGVuZGVudCBleGFtcGxlCgpVc2UgYGlzb2xhdGVkYCB3aGVuIHRoZSBleGFtcGxlIG11c3Qgc3RhbmQgYWxvbmU6CgpgYGBgbWFya2Rvd24KYGBgZnNoYXJwIGlzb2xhdGVkCmxldCBub3JtYWxpemUgKHZhbHVlOiBzdHJpbmcpID0gdmFsdWUuVHJpbSgpCmBgYApgYGBgCgojIyBSdW4gYW4gZXhhbXBsZQoKVXNlIGBydW5gIG9ubHkgd2hlbiBydW50aW1lIGJlaGF2aW9yIGlzIHBhcnQgb2YgdGhlIGNvbnRyYWN0OgoKYGBgYG1hcmtkb3duCmBgYGZzaGFycCBydW4KcHJpbnRmbiAib3JkZXItdG90YWw9MTIwLjAiCmBgYApgYGBgCgpPcGVyYXRpb25hbCBjb2RlIGNhbiBhY2Nlc3MgZmlsZXMsIHByb2Nlc3NlcywgbmV0d29ya3MsIGNsb2Nrcywgb3IgaG9zdHMuIERvIG5vdCBtYXJrIG9yZGluYXJ5IGV4YW1wbGVzIGBydW5gIGZvciBzdHJvbmdlci1sb29raW5nIHZlcmlmaWNhdGlvbi4KCiMjIFZlcmlmeSBhbiBGU0kgdHJhbnNjcmlwdAoKVXNlIGB0cmFuc2NyaXB0YCBmb3IgaW5wdXQgYW5kIGV4cGVjdGVkIG91dHB1dDoKCmBgYGBtYXJrZG93bgpgYGBmc2hhcnAgdHJhbnNjcmlwdAo+IDIwICsgMjI7Owp2YWwgaXQ6IGludCA9IDQyCmBgYApgYGBgCgojIyBBZGQgcGFnZSBzZXR1cAoKVXNlIGBwcmVwYXJlYCBmb3IgZGVjbGFyYXRpb25zIHNoYXJlZCBieSBsYXRlciBibG9ja3M6CgpgYGBgbWFya2Rvd24KYGBgZnNoYXJwIHByZXBhcmUKdHlwZSBDdXN0b21lciA9IHsgTmFtZTogc3RyaW5nIH0KYGBgCmBgYGAKCiMjIE1hcmsgZGVsaWJlcmF0ZSBwc2V1ZG9jb2RlCgpVc2UgYG5vLWNoZWNrYCBvbmx5IHdoZW4gdGhlIGZyYWdtZW50IGNhbm5vdCBiZSBtYWRlIGNvbXBsZXRlOgoKYGBgYG1hcmtkb3duCmBgYGZzaGFycCBuby1jaGVjayByZWFzb249IlRoZSByZW1haW5pbmcgY2FzZXMgYXJlIGFwcGxpY2F0aW9uLXNwZWNpZmljIgptYXRjaCByZXN1bHQgd2l0aAp8IE9rIHZhbHVlIC0+IHB1Ymxpc2ggdmFsdWUKfCBFcnJvciBfIC0+IC4uLgpgYGAKYGBgYAoKRnNMaXZlRG9jcyByZXF1aXJlcyBhIG5vbmVtcHR5IHJlYXNvbi4gQXVkaXQgb3V0cHV0IHJlcG9ydHMgdGhlIGV4Y2x1c2lvbi4KCiMjIFZlcmlmeSBYTUwgZXhhbXBsZXMKCkFkZCBhbiBleGFtcGxlIHRvIFhNTCBkb2N1bWVudGF0aW9uOgoKYGBgZnNoYXJwCi8vLyA8c3VtbWFyeT5BZGRzIHR3byB2YWx1ZXMuPC9zdW1tYXJ5PgovLy8gPGV4YW1wbGUgbmFtZT0iYWRkLXZhbHVlcyIgZGF0YS1saXZlZG9jcz0ic25hcHNob3QiPgovLy8gPiBhZGQgMjAgMjI7OwovLy8gdmFsIGl0OiBpbnQgPSA0MgovLy8gPC9leGFtcGxlPgpsZXQgYWRkIGxlZnQgcmlnaHQgPSBsZWZ0ICsgcmlnaHQKYGBgCgpVc2UgYGRhdGEtbGl2ZWRvY3M9InNuYXBzaG90ImAgd2hlbiBvdXRwdXQgaXMgcGFydCBvZiB0aGUgY29udHJhY3QuIFVzZSBgZGF0YS1saXZlZG9jcz0ibm8tY2hlY2siIHJlYXNvbj0iLi4uImAgZm9yIGEgZGVsaWJlcmF0ZSBleGNsdXNpb24uCgojIyBQcmVwYXJlIFhNTCBleGFtcGxlcyB3aXRoIHNjZW5hcmlvcwoKU29tZSBYTUwgZXhhbXBsZXMgbmVlZCBkZXRlcm1pbmlzdGljIHN0YXRlIGJlZm9yZSB0aGVpciB0cmFuc2NyaXB0IHJ1bnMuIEluc3RhbGwgdGhlIHNtYWxsIGFubm90YXRpb25zIHBhY2thZ2UgaW4gdGhlIGxpYnJhcnkgdGhhdCBvd25zIHRoZSBleGFtcGxlOgoKYGBgYmFzaApkb3RuZXQgYWRkIHBhY2thZ2UgRnNMaXZlRG9jcy5Bbm5vdGF0aW9ucwpgYGAKCmBGc0xpdmVEb2NzLkFubm90YXRpb25zYCBjb250YWlucyBtZXRhZGF0YSBjb25zdW1lZCBieSBGc0xpdmVEb2NzOyBpdCBkb2VzIG5vdCBicmluZyB0aGUgQ0xJLCBjb21waWxlciBzZXJ2aWNlLCBvciByZW5kZXJlciBpbnRvIHlvdXIgbGlicmFyeS4gTWFyayBhIHB1YmxpYywgcGFyYW1ldGVybGVzcyBmdW5jdGlvbiB3aXRoIGEgdW5pcXVlIHNjZW5hcmlvIG5hbWU6CgpgYGBmc2hhcnAKb3BlbiBGc0xpdmVEb2NzCgptb2R1bGUgQ3VzdG9tZXJFeGFtcGxlcyA9CiAgICBsZXQgbXV0YWJsZSBwcml2YXRlIGN1cnJlbnRDdXN0b21lciA9ICJhbm9ueW1vdXMiCgogICAgWzxEb2NTY2VuYXJpbygicHJlZmVycmVkLWN1c3RvbWVyIik+XQogICAgbGV0IHByZXBhcmVQcmVmZXJyZWRDdXN0b21lciAoKSA9CiAgICAgICAgY3VycmVudEN1c3RvbWVyIDwtICJBZGEiCgogICAgLy8vIDxzdW1tYXJ5PkdyZWV0cyB0aGUgY3VycmVudCBjdXN0b21lci48L3N1bW1hcnk+CiAgICAvLy8gPGV4YW1wbGUgbmFtZT0icHJlZmVycmVkLWN1c3RvbWVyLWdyZWV0aW5nIgogICAgLy8vICAgICAgICAgIHNjZW5hcmlvPSJwcmVmZXJyZWQtY3VzdG9tZXIiCiAgICAvLy8gICAgICAgICAgZGF0YS1saXZlZG9jcz0ic25hcHNob3QiPgogICAgLy8vID4gQ3VzdG9tZXJFeGFtcGxlcy5ncmVldCgpOzsKICAgIC8vLyB2YWwgaXQ6IHN0cmluZyA9ICJIZWxsbyBBZGEiCiAgICAvLy8gPC9leGFtcGxlPgogICAgbGV0IGdyZWV0ICgpID0gJCJIZWxsbyB7Y3VycmVudEN1c3RvbWVyfSIKYGBgCgpGb3IgZWFjaCBleGFtcGxlIHRoYXQgbmFtZXMgdGhlIHNjZW5hcmlvLCBGc0xpdmVEb2NzIHN0YXJ0cyB0aGUgZXhhbXBsZSBzZXNzaW9uLCBsb2FkcyB0aGUgZG9jdW1lbnRlZCBwcm9qZWN0LCBjYWxscyBgcHJlcGFyZVByZWZlcnJlZEN1c3RvbWVyKClgLCBhbmQgdGhlbiBldmFsdWF0ZXMgdGhlIGV4YW1wbGUuIFNldHVwIG91dHB1dCBpcyBub3QgcGFydCBvZiB0aGUgZXhwZWN0ZWQgdHJhbnNjcmlwdC4KClVzZSBzY2VuYXJpb3MgZm9yIGZvY3VzZWQgZGV0ZXJtaW5pc3RpYyBzZXR1cCBzdWNoIGFzIGZpeHR1cmUgZGF0YSwgZGVwZW5kZW5jeS1pbmplY3Rpb24gc3RhdGUsIG9yIGFuIGluLW1lbW9yeSB0ZXN0IGRvdWJsZS4gS2VlcCBzZXR1cCBmYXN0IGFuZCBsb2NhbDogZXhlY3V0YWJsZSBkb2N1bWVudGF0aW9uIGhhcyB0aGUgc2FtZSBmaWxlLCBwcm9jZXNzLCBuZXR3b3JrLCBjbG9jaywgYW5kIGVudmlyb25tZW50IGFjY2VzcyBhcyB0aGUgdXNlciBydW5uaW5nIEZzTGl2ZURvY3MuCgpTY2VuYXJpbyBydWxlczoKCi0gdGhlIGBzY2VuYXJpb2AgdmFsdWUgbXVzdCBleGFjdGx5IG1hdGNoIHRoZSBgRG9jU2NlbmFyaW9gIG5hbWU7Ci0gc2NlbmFyaW8gbmFtZXMgbXVzdCBiZSB1bmlxdWUgYWNyb3NzIHRoZSBwcm9qZWN0cyBpbiBvbmUgZG9jdW1lbnRhdGlvbiBidWlsZDsKLSB0aGUgYW5ub3RhdGVkIEYjIGZ1bmN0aW9uIG11c3QgY29tcGlsZSB0byBhIGNhbGxhYmxlIHN0YXRpYywgcGFyYW1ldGVybGVzcyBtZXRob2Q7IGEgcHVibGljIGZ1bmN0aW9uIGluIGFuIEYjIG1vZHVsZSBpcyB0aGUgdXN1YWwgZm9ybTsKLSB0aGUgZXhhbXBsZSBmYWlscyB3aGVuIGl0cyBuYW1lZCBzY2VuYXJpbyBjYW5ub3QgYmUgZm91bmQ7Ci0gZWFjaCBleGFtcGxlIGdldHMgYSBmcmVzaCBGU0kgc2Vzc2lvbiwgc28gb25lIGV4YW1wbGUgbXVzdCBub3QgZGVwZW5kIG9uIGFub3RoZXIgZXhhbXBsZSBoYXZpbmcgcnVuIGZpcnN0LgoKRG8gbm90IGFkZCBgRnNMaXZlRG9jc2AgaXRzZWxmIGFzIGEgbGlicmFyeSBkZXBlbmRlbmN5LiBJdCBpcyBhIC5ORVQgdG9vbCBwYWNrYWdlLiBgRnNMaXZlRG9jcy5Bbm5vdGF0aW9uc2AgaXMgdGhlIGNvbXBpbGUtdGltZSBjb250cmFjdCBmb3IgYXR0cmlidXRlcyB1c2VkIGJ5IGRvY3VtZW50ZWQgcHJvamVjdHMuCgojIyBHZW5lcmF0ZSBzdGFibGUgdGVzdHMKCmBgYGJhc2gKZG90bmV0IGxpdmVkb2NzIGdlbmVyYXRlLXRlc3RzCmRvdG5ldCB0ZXN0IHRlc3RzL0ZzTGl2ZURvY3MuU25hcHNob3RUZXN0cy9Gc0xpdmVEb2NzLlNuYXBzaG90VGVzdHMuZnNwcm9qCmBgYAoKVGhlIGNvbW1hbmQgcHJvZHVjZXMgc3RhYmxlIHhVbml0IGNhc2VzIGZyb20gdGhlIHNhbWUgZG9jdW1lbnRhdGlvbiBkaXNjb3ZlcnkgcmVzdWx0IHVzZWQgYnkgYXVkaXQsIGJ1aWxkLCBhbmQgY2FwdHVyZS4KClJ1biB0aGUgZ2VuZXJhdGVkIHRlc3QgcHJvamVjdCBpbiBDSS4gRnNMaXZlRG9jcyBoYW5kbGVzIGNvdmVyYWdlIHZhbGlkYXRpb24sIGNvbXBpbGUtYmVmb3JlLWV4ZWN1dGUgb3JkZXJpbmcsIHRyYW5zY3JpcHQgYmVoYXZpb3IsIGFuZCBzdGFsZS1jYXNlIGRldGVjdGlvbi4KCiMjIEF1ZGl0IHdpdGhvdXQgZ2VuZXJhdGVkIHRlc3RzCgpgYGBiYXNoCmRvdG5ldCBsaXZlZG9jcyBhdWRpdApgYGAKCkF1ZGl0IGNsYXNzaWZpZXMgZXZlcnkgYmxvY2sgYXMgcGFzc2VkLCBleGNsdWRlZCwgb3IgZmFpbGVkLiBBIHN1Y2Nlc3NmdWwgcmVsZWFzZSBjYXB0dXJlIHJlcXVpcmVzIGNvbXBsZXRlIGNvdmVyYWdlLgo="))
        let case = { Id = @"02-guides/01-verified-examples.md#page"; ProjectPath = projectPath; SourcePath = @"02-guides/01-verified-examples.md"; ExpandedMarkdown = markdown; Action = CompileUnit @"02-guides/01-verified-examples.md#page" }
        GeneratedVerification.runCase references case |> Async.RunSynchronously

    [<Fact>]
    let ``documentation 02-guides/03-transclusion.md#fsharp-0`` () =
        let projectPath = @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/samples/DeepReference/Acme.Docs/Acme.Docs.fsproj"
        let references = [ @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/Acme.Docs/release/Acme.Docs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Cli/release/livedocs.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Core/release/FsLiveDocs.Core.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Renderer/release/FsLiveDocs.Renderer.dll"; @"/home/adam/projects/FsLiveDocs/feature/release-history-validation/artifacts/bin/FsLiveDocs.Runner/release/FsLiveDocs.Runner.dll" ]
        let markdown = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("CiMgVHJhbnNjbHVkZSBzb3VyY2UgYW5kIGV4YW1wbGVzCgpUcmFuc2NsdWRlIGNvbXBpbGVkIHNvdXJjZSBvciB2ZXJpZmllZCBYTUwgZXhhbXBsZXMgaW5zdGVhZCBvZiBjb3B5aW5nIGNvZGUgaW50byBhIGd1aWRlLgoKIyMgTWFyayBhIHNvdXJjZSBzbmlwcGV0CgpBZGQgbWFya2VycyBhcm91bmQgdGhlIHNvdXJjZToKCmBgYGZzaGFycCBpc29sYXRlZAovLyA8c25pcHBldDpQcm9qZWN0U3RydWN0dXJlPgp0eXBlIFNvdXJjZUxpbmsgPSB7CiAgICBGaWxlOiBzdHJpbmcKICAgIExpbmU6IGludAp9Ci8vIDwvc25pcHBldDpQcm9qZWN0U3RydWN0dXJlPgpgYGAKClJlZmVyZW5jZSB0aGUgc25pcHBldCBmcm9tIE1hcmtkb3duOgoKYGBgdGV4dAp7ezwgc25pcHBldCBpZD0iUHJvamVjdFN0cnVjdHVyZSIgbW9kZT0iaXNvbGF0ZWQiID59fQpgYGAKClN1cHBvcnRlZCBtb2RlcyBhcmUgYHByZXBhcmVgLCBgaXNvbGF0ZWRgLCBgcnVuYCwgYW5kIGBuby1jaGVja2AuIEEgYG5vLWNoZWNrYCBzbmlwcGV0IGFsc28gcmVxdWlyZXMgYHJlYXNvbj0iLi4uImAuCgojIyBUcmFuc2NsdWRlIGFuIFhNTCBleGFtcGxlCgpSZWZlcmVuY2UgYSBuYW1lZCBYTUwgZXhhbXBsZToKCmBgYHRleHQKe3s8IGV4YW1wbGUgaWQ9IkNyZWF0ZUV4YW1wbGUiID59fQpgYGAKCkZzTGl2ZURvY3MgcHJlc2VydmVzIHRoZSBleGFtcGxlJ3MgZXhlY3V0aW9uIG9yIGV4Y2x1c2lvbiBjb250cmFjdCB3aGVuIGl0IGNyZWF0ZXMgdGhlIGNhbm9uaWNhbCBmZW5jZWQgYmxvY2suCgojIyBVbmRlcnN0YW5kIHJlbGVhc2UgY2FwdHVyZQoKQ2FwdHVyZSBleHBhbmRzIHRyYW5zY2x1c2lvbnMgYmVmb3JlIGl0IHN0b3JlcyBjYW5vbmljYWwgTWFya2Rvd24uIEEgbGF0ZXIgaGlzdG9yeSByZW5kZXIgZG9lcyBub3QgbmVlZCB0aGUgb3JpZ2luYWwgc291cmNlIGZpbGUgb3Igc2hvcnRjb2RlIGltcGxlbWVudGF0aW9uLgoKQ3Jvc3MtcmVmZXJlbmNlcyByZW1haW4gc2VtYW50aWMgdW50aWwgcmVuZGVyIHRpbWUgc28gdGhlIGN1cnJlbnQgcmVuZGVyZXIgY2FuIGNyZWF0ZSBjdXJyZW50IHBhZ2UgVVJMcy4K"))
        let case = { Id = @"02-guides/03-transclusion.md#fsharp-0"; ProjectPath = projectPath; SourcePath = @"02-guides/03-transclusion.md"; ExpandedMarkdown = markdown; Action = CompileUnit @"02-guides/03-transclusion.md#fsharp-0" }
        GeneratedVerification.runCase references case |> Async.RunSynchronously
