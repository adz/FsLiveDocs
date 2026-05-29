namespace FsLiveDocs.Core

open System

module ExampleTranscript =
    let private normalizeIndent (text: string) =
        let lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
        let nonEmpty = lines |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        if nonEmpty.Length = 0 then ""
        else
            let minIndent =
                nonEmpty
                |> Array.map (fun line -> line.Length - line.TrimStart().Length)
                |> Array.fold min Int32.MaxValue

            lines
            |> Array.map (fun line ->
                if line.Length >= minIndent then line.Substring(minIndent)
                else line.TrimStart())
            |> String.concat "\n"
            |> fun value -> value.Trim()

    let private isFsiInputLine (line: string) =
        let trimmed = line.TrimStart()
        trimmed.StartsWith("> ") || trimmed.StartsWith("- ")

    let private stripFsiPrompt (line: string) =
        let trimmed = line.TrimStart()
        if trimmed.StartsWith("> ") then trimmed.Substring(2)
        elif trimmed.StartsWith("- ") then trimmed.Substring(2)
        else trimmed

    type Parsed = {
        DisplayText: string
        Script: string
        ExpectedOutput: string option
    }

    let parse (raw: string) =
        let normalized = normalizeIndent raw
        let lines = normalized.Split('\n')
        let isSession = lines |> Array.exists isFsiInputLine

        if isSession then
            let script =
                lines
                |> Array.choose (fun line ->
                    if isFsiInputLine line then Some (stripFsiPrompt line)
                    else None)
                |> String.concat "\n"

            let output =
                lines
                |> Array.choose (fun line ->
                    if String.IsNullOrWhiteSpace line then None
                    elif isFsiInputLine line then None
                    else Some (line.TrimEnd()))
                |> String.concat "\n"
                |> fun value -> value.Trim()

            {
                DisplayText = normalized
                Script = script
                ExpectedOutput = if String.IsNullOrWhiteSpace output then None else Some output
            }
        else
            let parts = normalized.Split([| "// EXPECTED:" |], StringSplitOptions.None)
            let content = parts.[0].Trim()
            let expected = if parts.Length > 1 then Some (parts.[1].Trim()) else None
            {
                DisplayText = content
                Script = content
                ExpectedOutput = expected
            }

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
/// <example name="CreateExample">
/// > let example = { Name = "Basic Usage"; Content = "printfn \"Hello\""; ExpectedOutput = Some "Hello"; Scenario = None };;
/// > printfn "%s" example.Name;;
/// Basic Usage
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

/// <summary>Represents an F# entity (Module, Type, Interface, etc.).</summary>
type EntityModel = {
    /// <summary>The fully qualified unique identifier.</summary>
    Id: string
    /// <summary>The display name (normalized).</summary>
    Name: string
    /// <summary>The kind of entity (e.g., "Module", "Type").</summary>
    Kind: string
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

/// <summary>Build-time configuration for the generated documentation site.</summary>
type SiteConfig = {
    /// <summary>Optional repository URL used to build source links for members.</summary>
    RepoUrl: string option
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
