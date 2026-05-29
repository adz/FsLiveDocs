namespace FsLiveDocs.Core

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
type PackageModel = { 
    /// <summary>The semantic version of the documentation snapshot.</summary>
    Version: string 
    /// <summary>Top-level entities in the package.</summary>
    Entities: EntityModel list
    /// <summary>Global DocTest scenarios available for examples.</summary>
    Scenarios: ScenarioModel list
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
type SiteConfig = {
    /// <summary>Optional repository URL used to build source links for members.</summary>
    RepoUrl: string option
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
    /// <summary>Ordering weight in the sidebar.</summary>
    Weight: int
    /// <summary>Optional category or type identifier.</summary>
    Type: string option
}

/// <summary>A processed documentation page.</summary>
type ContentPage = {
    /// <summary>Frontmatter metadata.</summary>
    Metadata: ContentMetadata
    /// <summary>Rendered HTML content.</summary>
    ContentHtml: string
    /// <summary>Relative file path from the docs root.</summary>
    FilePath: string
}
