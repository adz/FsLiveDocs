namespace FsLiveDocs.Core

type SourceLink = {
    File: string
    Line: int
}

type ExampleModel = {
    Name: string
    Content: string
    ExpectedOutput: string option
    Scenario: string option
}

type ParameterModel = {
    Name: string
    Type: string
    DescriptionHtml: string
}

type MemberModel = {
    Id: string
    Name: string
    Signature: string
    Parameters: ParameterModel list
    ReturnType: string
    SummaryHtml: string
    RemarksHtml: string
    Examples: ExampleModel list
    Location: SourceLink
}

type EntityModel = {
    Id: string
    Name: string
    Kind: string // Module, Type, etc.
    SummaryHtml: string
    Members: MemberModel list
    Entities: EntityModel list
}

type ScenarioModel = {
    Name: string
    MethodId: string
}

type PackageModel = { 
    Version: string 
    Entities: EntityModel list
    Scenarios: ScenarioModel list
}

[<CLIMutable>]
type ContentMetadata = {
    Title: string
    Weight: int
    Type: string option
}

type ContentPage = {
    Metadata: ContentMetadata
    ContentHtml: string
    FilePath: string
}
