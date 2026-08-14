namespace FsLiveDocs.Core

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
