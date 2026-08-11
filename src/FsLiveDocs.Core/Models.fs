namespace FsLiveDocs.Core

open System
open Newtonsoft.Json

// <snippet:ProjectStructure>
/// <summary>Represents a link to a specific line in a source file.</summary>
type SourceLink = {
    /// <summary>The relative or absolute path to the source file.</summary>
    File: string
    /// <summary>The 1-based line number.</summary>
    Line: int
}
// </snippet:ProjectStructure>

// <snippet:ExampleModel>
/// <summary>Represents an executable code example extracted from documentation.</summary>
/// <example name="CreateExample" data-livedocs="snapshot">
/// > ExampleModel.Create("Basic Usage", "1+1", Some "2", None);;
/// val it: ExampleModel = { Name = "Basic Usage"
///   Content = "1+1"
///   ExpectedOutput = Some "2"
///   Scenario = None
///   IsSnapshotTest = false }
/// </example>
type ExampleModel = {
    /// <summary>The unique name of the example, used for transclusion.</summary>
    Name: string
    /// <summary>The raw F# code content.</summary>
    Content: string
    /// <summary>Optional expected output to verify against during testing.</summary>
    ExpectedOutput: string option
    /// <summary>Optional scenario name to provide mocks/DI for this example.</summary>
    Scenario: string option
    /// <summary>Whether this example should be picked up by the generated snapshot test project.</summary>
    IsSnapshotTest: bool
}
// </snippet:ExampleModel>

/// <summary>Represents a parameter of a function or method.</summary>
type ParameterModel = {
    /// <summary>The name of the parameter.</summary>
    Name: string
    /// <summary>The formatted F# type string.</summary>
    Type: string
    /// <summary>The documentation description for this parameter.</summary>
    DescriptionHtml: string
}

/// <summary>Represents a member (function, value, or method) of an F# entity.</summary>
type MemberModel = {
    /// <summary>The fully qualified unique identifier for the member.</summary>
    Id: string
    /// <summary>The display name of the member.</summary>
    Name: string
    /// <summary>The full F# type signature.</summary>
    Signature: string
    /// <summary>List of parameters, if any.</summary>
    Parameters: ParameterModel list
    /// <summary>The return type of the member.</summary>
    ReturnType: string
    /// <summary>The HTML-formatted summary docstring.</summary>
    SummaryHtml: string
    /// <summary>The HTML-formatted remarks/long-form documentation.</summary>
    RemarksHtml: string
    /// <summary>Executable examples associated with this member.</summary>
    Examples: ExampleModel list
    /// <summary>Source location of the member declaration.</summary>
    Location: SourceLink
}

/// <summary>The domain kind for a documented API entity.</summary>
type EntityKind =
    | Namespace
    | Module
    | Record
    | Union
    | Type
    override this.ToString() =
        match this with
        | Namespace -> "Namespace"
        | Module -> "Module"
        | Record -> "Record"
        | Union -> "Union"
        | Type -> "Type"

/// <summary>The evaluation status for a documentation example.</summary>
type ExampleStatus =
    | FirstCut
    | Verified
    | Mismatch
    | Error
    override this.ToString() =
        match this with
        | FirstCut -> "first-cut"
        | Verified -> "verified"
        | Mismatch -> "mismatch"
        | Error -> "error"

/// <summary>Represents an F# entity (Module, Type, Interface, etc.).</summary>
type EntityModel = {
    /// <summary>The fully qualified unique identifier.</summary>
    Id: string
    /// <summary>The display name (normalized).</summary>
    Name: string
    /// <summary>The kind of entity (e.g., Module, Type).</summary>
    Kind: EntityKind
    /// <summary>The HTML-formatted introduction for this entity.</summary>
    SummaryHtml: string
    /// <summary>Members belonging to this entity.</summary>
    Members: MemberModel list
    /// <summary>Executable examples associated with this entity.</summary>
    Examples: ExampleModel list
    /// <summary>Nested entities (sub-modules or nested types).</summary>
    Entities: EntityModel list
}

/// <summary>Represents a setup scenario for a DocTest.</summary>
type ScenarioModel = {
    /// <summary>The name matching the 'scenario' attribute in examples.</summary>
    Name: string
    /// <summary>The fully qualified ID of the function to call for setup.</summary>
    MethodId: string
}

/// <summary>The root model representing a documented package or solution.</summary>
type PackageInfo = {
    /// <summary>Assembly or package name supplied by the extracted project.</summary>
    Name: string
    /// <summary>Entity ids contributed by the package before hierarchy reconstruction.</summary>
    EntityIds: string list
}

/// <summary>The root model representing a documented package or solution.</summary>
type PackageModel = { 
    /// <summary>The semantic version of the documentation snapshot.</summary>
    Version: string 
    /// <summary>Top-level entities in the package.</summary>
    Entities: EntityModel list
    /// <summary>Global DocTest scenarios available for examples.</summary>
    Scenarios: ScenarioModel list
    /// <summary>Package provenance retained across multi-project extraction and merge.</summary>
    Packages: PackageInfo list
}

/// <summary>Versioned, schema-tagged API model stored as a release artifact.</summary>
type ApiModelArtifact = { SchemaVersion: int; Package: PackageModel }

/// <summary>
/// A quality problem found in the documented API itself, reported against the source that
/// declares it. Diagnostics describe a single extraction run rather than the documented
/// snapshot, so they are deliberately not part of <see cref="T:FsLiveDocs.Core.PackageModel"/>.
/// </summary>
type ApiDiagnostic = {
    /// <summary>Stable kebab-case identifier for the kind of problem.</summary>
    Code: string
    /// <summary>Full name of the member the diagnostic concerns.</summary>
    Symbol: string
    /// <summary>Source location that declares the member.</summary>
    Location: SourceLink
    /// <summary>One-line description of what was found.</summary>
    Message: string
    /// <summary>What the author can change to resolve it.</summary>
    Remedy: string
}

/// Renderer-neutral semantic classification persisted for a documentation release.
type SemanticTokenKind =
    | PlainText | Keyword | Identifier | TypeName | Function | Property | UnionCase
    | ActivePatternCase | Module | Namespace | Operator | Number | String | Comment
    | Punctuation | Preprocessor

type SemanticToken = { Text: string; Kind: SemanticTokenKind; Tooltip: int option }
type SemanticLine = { Tokens: SemanticToken list }
type SemanticTooltipSection = { Heading: string option; Content: string }
type SemanticTooltip = {
    Signature: string option
    Documentation: string option
    Sections: SemanticTooltipSection list
    Footer: string option
}
type SemanticDiagnosticSeverity = | Warning | Error
type SemanticDiagnostic = {
    Severity: SemanticDiagnosticSeverity
    Message: string
    StartLine: int
    StartColumn: int
    EndLine: int
    EndColumn: int
}
type SemanticCodeBlock = {
    Id: string
    SourceHash: string
    ContextHash: string
    Lines: SemanticLine list
    Tooltips: SemanticTooltip list
    Diagnostics: SemanticDiagnostic list
}
type SemanticPage = { SourcePath: string; Blocks: SemanticCodeBlock list }
type SemanticDocumentationArtifact = { SchemaVersion: int; Prelude: string; Pages: SemanticPage list }

/// <summary>One immutable API model and tagged documentation source in a history build.</summary>
type HistoryEntry = {
    Version: string
    ModelPath: string
    ModelSha256: string
    SemanticPath: string option
    SemanticSha256: string option
    DocsPath: string
}

/// <summary>Local build manifest materialized from a repository's durable release history.</summary>
type HistoryManifest = {
    SchemaVersion: int
    CurrentVersion: string
    Entries: HistoryEntry list
}

type ExampleModel with
    /// <summary>Create an example record from its individual fields.</summary>
    static member Create(name: string, content: string, expectedOutput: string option, scenario: string option, ?isSnapshotTest: bool) =
        {
            Name = name
            Content = content
            ExpectedOutput = expectedOutput
            Scenario = scenario
            IsSnapshotTest = defaultArg isSnapshotTest false
        }

/// <summary>Represents the evaluated result of a documentation example.</summary>
type ExampleSnapshotModel = {
    /// <summary>The unique name of the example.</summary>
    Name: string
    /// <summary>The scenario name, if one was applied.</summary>
    Scenario: string option
    /// <summary>The example source as extracted from the documentation comment.</summary>
    Source: string
    /// <summary>The expected output already present in the source, if any.</summary>
    ExpectedOutput: string option
    /// <summary>The actual output produced by running the example.</summary>
    ActualOutput: string
    /// <summary>The verification status for the example.</summary>
    Status: ExampleStatus
}

/// <summary>Represents the snapshot payload for one project.</summary>
type ProjectSnapshotModel = {
    /// <summary>The project file that was evaluated.</summary>
    ProjectPath: string
    /// <summary>The project namespace used by the runner.</summary>
    ProjectNamespace: string
    /// <summary>The examples selected for snapshot verification.</summary>
    Examples: ExampleSnapshotModel list
}

/// <summary>Build-time configuration for the generated documentation site.</summary>
type NavigationItem = {
    /// <summary>Text displayed in the top navigation.</summary>
    Label: string
    /// <summary>Root-relative site path or absolute URL.</summary>
    Href: string
}

/// <summary>Build-time configuration for the generated documentation site.</summary>
type SiteConfig = {
    /// <summary>Optional repository URL used to build source links for members.</summary>
    RepoUrl: string option
    /// <summary>Optional consumer name used in the navbar and page titles.</summary>
    SiteName: string option
    /// <summary>Optional short consumer mark used in the navbar.</summary>
    LogoText: string option
    /// <summary>Optional root-relative or absolute image used as the navbar logo.</summary>
    LogoPath: string option
    /// <summary>Optional dark-theme variant of <c>LogoPath</c>.</summary>
    LogoDarkPath: string option
    /// <summary>Whether to display the site name beside the navbar mark. Defaults to true.</summary>
    ShowSiteName: bool option
    /// <summary>Optional root-relative or absolute consumer stylesheet loaded after FsLiveDocs styles.</summary>
    Stylesheet: string option
    /// <summary>Optional DaisyUI themes exposed by the theme picker. Defaults to the built-in theme set.</summary>
    Themes: string list option
    /// <summary>Optional top-level navigation. Defaults to Home and API.</summary>
    Navigation: NavigationItem list option
    /// <summary>Repository-owned F# setup compiled and shown on every page containing checked F#.</summary>
    FSharpPrelude: string option
}

/// <summary>Resolved project paths and namespace information used by the doc-test runner.</summary>
type ResolvedProject = {
    /// <summary>The path to the source project file.</summary>
    ProjectPath: string
    /// <summary>The path to the built assembly used by FSI.</summary>
    AssemblyPath: string
    /// <summary>The project namespace opened before executing examples.</summary>
    ProjectNamespace: string
}

/// <summary>Metadata extracted from Markdown frontmatter.</summary>
[<CLIMutable>]
type ContentMetadata = {
    /// <summary>Title of the page.</summary>
    Title: string
    /// <summary>Optional category or type identifier.</summary>
    Type: string option
    /// <summary>Optional documentation project used to compile code blocks on this page.</summary>
    Project: string option
    /// <summary>Optional target framework used to compile code blocks on this page.</summary>
    TargetFramework: string option
    /// <summary>Optional runtime/compiler platform described by this page, such as fable.</summary>
    Platform: string option
}

/// <summary>A processed documentation page.</summary>
type ContentPage = {
    /// <summary>Frontmatter metadata.</summary>
    Metadata: ContentMetadata
    /// <summary>Rendered HTML content.</summary>
    ContentHtml: string
    /// <summary>Relative file path from the docs root.</summary>
    FilePath: string
    /// <summary>Relative HTML output path, with documentation ordering prefixes removed.</summary>
    OutputPath: string
    /// <summary>Ordering prefix of the top-level documentation section.</summary>
    SectionOrder: int
}

type FSharpListConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) =
        objectType.IsGenericType && objectType.GetGenericTypeDefinition() = typedefof<list<_>>
    override _.WriteJson(writer, value, serializer) =
        let list = value :?> System.Collections.IEnumerable
        list |> Seq.cast<obj> |> Seq.toArray |> fun items -> serializer.Serialize(writer, items)
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        let elementType = objectType.GetGenericArguments().[0]
        let listType = typedefof<ResizeArray<_>>.MakeGenericType(elementType)
        let list = serializer.Deserialize(reader, listType) :?> System.Collections.IEnumerable
        let methodInfo = typedefof<list<_>>.Assembly.GetType("Microsoft.FSharp.Collections.ListModule").GetMethod("OfSeq").MakeGenericMethod(elementType)
        methodInfo.Invoke(null, [| list |])

type FSharpOptionConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) =
        objectType.IsGenericType && objectType.GetGenericTypeDefinition() = typedefof<option<_>>
    override _.WriteJson(writer, value, serializer) =
        let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(value.GetType())
        let case, fields = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(value, value.GetType())
        if case.Name = "None" then
            writer.WriteNull()
        else
            serializer.Serialize(writer, fields.[0])
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        let innerType = objectType.GetGenericArguments().[0]
        if reader.TokenType = JsonToken.Null then
            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(objectType)
            let noneCase = cases |> Array.find (fun c -> c.Name = "None")
            Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(noneCase, [||])
        else
            let value = serializer.Deserialize(reader, innerType)
            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(objectType)
            let someCase = cases |> Array.find (fun c -> c.Name = "Some")
            Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| value |])

type FSharpUnionConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) =
        Microsoft.FSharp.Reflection.FSharpType.IsUnion(objectType) &&
        not (objectType.IsGenericType && (objectType.GetGenericTypeDefinition() = typedefof<option<_>> || objectType.GetGenericTypeDefinition() = typedefof<list<_>>))
    override _.WriteJson(writer, value, serializer) =
        let case, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(value, value.GetType())
        writer.WriteValue(case.Name)
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        if reader.TokenType = JsonToken.String then
            let name = reader.Value :?> string
            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(objectType)
            match cases |> Array.tryFind (fun c -> c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) with
            | Some case -> Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||])
            | None when objectType = typeof<SemanticTokenKind> ->
                let plainText = cases |> Array.find (fun case -> case.Name = nameof PlainText)
                Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(plainText, [||])
            | None -> failwithf "Unknown union case %s for type %s" name objectType.Name
        else
            failwithf "Expected string when reading union, got %O" reader.TokenType

module Serialization =
    let jsonSettings =
        let settings = JsonSerializerSettings()
        settings.Converters.Add(FSharpListConverter())
        settings.Converters.Add(FSharpOptionConverter())
        settings.Converters.Add(FSharpUnionConverter())
        settings
