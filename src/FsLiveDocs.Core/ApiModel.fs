namespace FsLiveDocs.Core

open System

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
///   IsSnapshotTest = false
///   NoCheckReason = None }
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
    /// <summary>Why this example is deliberately excluded from verification, if it is.</summary>
    NoCheckReason: string option
}
// </snippet:ExampleModel>

/// <summary>Represents a parameter of a function or method.</summary>
type ParameterModel = {
    /// <summary>The name of the parameter.</summary>
    Name: string
    /// <summary>The formatted F# type string.</summary>
    Type: string
    /// <summary>The renderer-neutral documentation for this parameter.</summary>
    Description: DocumentationNode list
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
    /// <summary>The renderer-neutral summary docstring.</summary>
    Summary: DocumentationNode list
    /// <summary>The renderer-neutral remarks or long-form documentation.</summary>
    Remarks: DocumentationNode list
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

/// <summary>Represents an F# entity (Module, Type, Interface, etc.).</summary>
type EntityModel = {
    /// <summary>The fully qualified unique identifier.</summary>
    Id: string
    /// <summary>The display name (normalized).</summary>
    Name: string
    /// <summary>The kind of entity (e.g., Module, Type).</summary>
    Kind: EntityKind
    /// <summary>The renderer-neutral introduction for this entity.</summary>
    Summary: DocumentationNode list
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

type ExampleModel with
    /// <summary>Create an example record from its individual fields.</summary>
    static member Create(name: string, content: string, expectedOutput: string option, scenario: string option, ?isSnapshotTest: bool, ?noCheckReason: string) =
        {
            Name = name
            Content = content
            ExpectedOutput = expectedOutput
            Scenario = scenario
            IsSnapshotTest = defaultArg isSnapshotTest false
            NoCheckReason = noCheckReason
        }
