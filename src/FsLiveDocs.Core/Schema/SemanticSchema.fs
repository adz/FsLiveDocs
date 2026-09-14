namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the renderer-neutral semantic classification persisted as a
/// `SemanticDocumentationArtifact` release asset. Wire-compatible with the pre-Reified
/// Newtonsoft format: see `DocumentationSchema` for the shared compatibility rules.
module SemanticSchema =

    /// An older renderer must still open a capsule a newer capture tool produced, which may
    /// classify tokens this build has never heard of. `Schema.enum` fails closed on an unknown
    /// case, so this decodes as bare text and falls back to `PlainText` for anything it does not
    /// recognize -- the same forward-compatibility behavior `FSharpUnionConverter`'s special case
    /// for this one type gave the pre-Reified reader.
    let semanticTokenKind : Schema<SemanticTokenKind> =
        let toCase =
            function
            | "PlainText" -> SemanticTokenKind.PlainText
            | "Keyword" -> SemanticTokenKind.Keyword
            | "Identifier" -> SemanticTokenKind.Identifier
            | "TypeName" -> SemanticTokenKind.TypeName
            | "Function" -> SemanticTokenKind.Function
            | "Property" -> SemanticTokenKind.Property
            | "UnionCase" -> SemanticTokenKind.UnionCase
            | "ActivePatternCase" -> SemanticTokenKind.ActivePatternCase
            | "Module" -> SemanticTokenKind.Module
            | "Namespace" -> SemanticTokenKind.Namespace
            | "Operator" -> SemanticTokenKind.Operator
            | "Number" -> SemanticTokenKind.Number
            | "String" -> SemanticTokenKind.String
            | "Comment" -> SemanticTokenKind.Comment
            | "Punctuation" -> SemanticTokenKind.Punctuation
            | "Preprocessor" -> SemanticTokenKind.Preprocessor
            | _ -> SemanticTokenKind.PlainText

        let toText =
            function
            | SemanticTokenKind.PlainText -> "PlainText"
            | SemanticTokenKind.Keyword -> "Keyword"
            | SemanticTokenKind.Identifier -> "Identifier"
            | SemanticTokenKind.TypeName -> "TypeName"
            | SemanticTokenKind.Function -> "Function"
            | SemanticTokenKind.Property -> "Property"
            | SemanticTokenKind.UnionCase -> "UnionCase"
            | SemanticTokenKind.ActivePatternCase -> "ActivePatternCase"
            | SemanticTokenKind.Module -> "Module"
            | SemanticTokenKind.Namespace -> "Namespace"
            | SemanticTokenKind.Operator -> "Operator"
            | SemanticTokenKind.Number -> "Number"
            | SemanticTokenKind.String -> "String"
            | SemanticTokenKind.Comment -> "Comment"
            | SemanticTokenKind.Punctuation -> "Punctuation"
            | SemanticTokenKind.Preprocessor -> "Preprocessor"

        Schema.text |> Schema.convert toCase toText

    let semanticDiagnosticSeverity : Schema<SemanticDiagnosticSeverity> =
        Schema.enum [
            EnumCase.create "Warning" SemanticDiagnosticSeverity.Warning
            EnumCase.create "Error" SemanticDiagnosticSeverity.Error
        ]

    let semanticToken : Schema<SemanticToken> =
        schema<SemanticToken> {
            fieldAs "Text" (fun (t: SemanticToken) -> t.Text) { withSchema Schema.text }
            fieldAs "Kind" (fun (t: SemanticToken) -> t.Kind) { withSchema semanticTokenKind }
            fieldAs "Tooltip" (fun (t: SemanticToken) -> t.Tooltip) { withSchema (Schema.option Schema.int) }
            construct (fun text kind tooltip -> { Text = text; Kind = kind; Tooltip = tooltip })
        }

    let semanticLine : Schema<SemanticLine> =
        schema<SemanticLine> {
            fieldAs "Tokens" (fun (l: SemanticLine) -> l.Tokens) { withSchema (Schema.listWith semanticToken) }
            construct (fun tokens -> { Tokens = tokens })
        }

    let semanticTooltipSection : Schema<SemanticTooltipSection> =
        schema<SemanticTooltipSection> {
            fieldAs "Heading" (fun (s: SemanticTooltipSection) -> s.Heading) { withSchema (Schema.option Schema.text) }
            fieldAs "Content" (fun (s: SemanticTooltipSection) -> s.Content) { withSchema Schema.text }
            construct (fun heading content -> { Heading = heading; Content = content })
        }

    let semanticTooltip : Schema<SemanticTooltip> =
        schema<SemanticTooltip> {
            fieldAs "Signature" (fun (t: SemanticTooltip) -> t.Signature) { withSchema (Schema.option Schema.text) }
            fieldAs "Documentation" (fun (t: SemanticTooltip) -> t.Documentation) { withSchema (Schema.option Schema.text) }
            fieldAs "Sections" (fun (t: SemanticTooltip) -> t.Sections) { withSchema (Schema.listWith semanticTooltipSection) }
            fieldAs "Footer" (fun (t: SemanticTooltip) -> t.Footer) { withSchema (Schema.option Schema.text) }
            construct (fun signature documentation sections footer ->
                { Signature = signature; Documentation = documentation; Sections = sections; Footer = footer })
        }

    let semanticDiagnostic : Schema<SemanticDiagnostic> =
        schema<SemanticDiagnostic> {
            fieldAs "Severity" (fun (d: SemanticDiagnostic) -> d.Severity) { withSchema semanticDiagnosticSeverity }
            fieldAs "Message" (fun (d: SemanticDiagnostic) -> d.Message) { withSchema Schema.text }
            fieldAs "StartLine" (fun (d: SemanticDiagnostic) -> d.StartLine) { withSchema Schema.int }
            fieldAs "StartColumn" (fun (d: SemanticDiagnostic) -> d.StartColumn) { withSchema Schema.int }
            fieldAs "EndLine" (fun (d: SemanticDiagnostic) -> d.EndLine) { withSchema Schema.int }
            fieldAs "EndColumn" (fun (d: SemanticDiagnostic) -> d.EndColumn) { withSchema Schema.int }
            construct (fun severity message startLine startColumn endLine endColumn ->
                { Severity = severity
                  Message = message
                  StartLine = startLine
                  StartColumn = startColumn
                  EndLine = endLine
                  EndColumn = endColumn })
        }

    let semanticCodeBlock : Schema<SemanticCodeBlock> =
        schema<SemanticCodeBlock> {
            fieldAs "Id" (fun (b: SemanticCodeBlock) -> b.Id) { withSchema Schema.text }
            fieldAs "SourceHash" (fun (b: SemanticCodeBlock) -> b.SourceHash) { withSchema Schema.text }
            fieldAs "ContextHash" (fun (b: SemanticCodeBlock) -> b.ContextHash) { withSchema Schema.text }
            fieldAs "Lines" (fun (b: SemanticCodeBlock) -> b.Lines) { withSchema (Schema.listWith semanticLine) }
            fieldAs "Tooltips" (fun (b: SemanticCodeBlock) -> b.Tooltips) { withSchema (Schema.listWith semanticTooltip) }
            fieldAs "Diagnostics" (fun (b: SemanticCodeBlock) -> b.Diagnostics) { withSchema (Schema.listWith semanticDiagnostic) }
            construct (fun id sourceHash contextHash lines tooltips diagnostics ->
                { Id = id
                  SourceHash = sourceHash
                  ContextHash = contextHash
                  Lines = lines
                  Tooltips = tooltips
                  Diagnostics = diagnostics })
        }

    let semanticPage : Schema<SemanticPage> =
        schema<SemanticPage> {
            fieldAs "SourcePath" (fun (p: SemanticPage) -> p.SourcePath) { withSchema Schema.text }
            fieldAs "Blocks" (fun (p: SemanticPage) -> p.Blocks) { withSchema (Schema.listWith semanticCodeBlock) }
            construct (fun sourcePath blocks -> { SourcePath = sourcePath; Blocks = blocks })
        }

    let semanticDocumentationArtifact : Schema<SemanticDocumentationArtifact> =
        schema<SemanticDocumentationArtifact> {
            fieldAs "SchemaVersion" (fun (a: SemanticDocumentationArtifact) -> a.SchemaVersion) { withSchema Schema.int }
            fieldAs "Prelude" (fun (a: SemanticDocumentationArtifact) -> a.Prelude) { withSchema Schema.text }
            fieldAs "Pages" (fun (a: SemanticDocumentationArtifact) -> a.Pages) { withSchema (Schema.listWith semanticPage) }
            construct (fun schemaVersion prelude pages -> { SchemaVersion = schemaVersion; Prelude = prelude; Pages = pages })
        }
