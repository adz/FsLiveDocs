namespace FsLiveDocs.Core

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
    /// <summary>Fully-qualified F# symbol name, resolved to an API link when the symbol is documented.</summary>
    Link: string option
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
/// Compiler-derived, renderer-neutral dependencies and bindings for one checked example.
/// An empty Open denotes a documented package used through a fully qualified name.
type ExampleRequirement = { Open: string; Package: string option }
type ExampleContext = { Requires: ExampleRequirement list; Types: string list }

type SemanticCodeBlock = {
    Id: string
    SourceHash: string
    ContextHash: string
    Lines: SemanticLine list
    Tooltips: SemanticTooltip list
    Diagnostics: SemanticDiagnostic list
    ExampleContext: ExampleContext option
}
type SemanticPage = { SourcePath: string; Blocks: SemanticCodeBlock list }
type SemanticDocumentationArtifact = { SchemaVersion: int; Prelude: string; Pages: SemanticPage list }
