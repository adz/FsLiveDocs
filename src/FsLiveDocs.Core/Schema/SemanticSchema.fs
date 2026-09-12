namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the renderer-neutral semantic classification persisted as a
/// `SemanticDocumentationArtifact` release asset. Wire-compatible with the pre-Reified
/// Newtonsoft format: see `DocumentationSchema` for the shared compatibility rules.
module SemanticSchema =

    let semanticTokenKind : Schema<SemanticTokenKind> =
        Schema.enum [
            EnumCase.create "PlainText" SemanticTokenKind.PlainText
            EnumCase.create "Keyword" SemanticTokenKind.Keyword
            EnumCase.create "Identifier" SemanticTokenKind.Identifier
            EnumCase.create "TypeName" SemanticTokenKind.TypeName
            EnumCase.create "Function" SemanticTokenKind.Function
            EnumCase.create "Property" SemanticTokenKind.Property
            EnumCase.create "UnionCase" SemanticTokenKind.UnionCase
            EnumCase.create "ActivePatternCase" SemanticTokenKind.ActivePatternCase
            EnumCase.create "Module" SemanticTokenKind.Module
            EnumCase.create "Namespace" SemanticTokenKind.Namespace
            EnumCase.create "Operator" SemanticTokenKind.Operator
            EnumCase.create "Number" SemanticTokenKind.Number
            EnumCase.create "String" SemanticTokenKind.String
            EnumCase.create "Comment" SemanticTokenKind.Comment
            EnumCase.create "Punctuation" SemanticTokenKind.Punctuation
            EnumCase.create "Preprocessor" SemanticTokenKind.Preprocessor
        ]

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
