namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the renderer-neutral documentation-node graph shared by every persisted
/// artifact (API entities, member docs, semantic tooltips). Wire-compatible with the pre-Reified
/// Newtonsoft format: PascalCase field names (every field uses `fieldAs`, not the camelCase-by-
/// default `field`), fieldless unions as bare JSON strings, options as null-or-unwrapped-value.
module DocumentationSchema =

    let documentationNodeKind : Schema<DocumentationNodeKind> =
        Schema.enum [
            EnumCase.create "Text" DocumentationNodeKind.Text
            EnumCase.create "Paragraph" DocumentationNodeKind.Paragraph
            EnumCase.create "InlineCode" DocumentationNodeKind.InlineCode
            EnumCase.create "CodeBlock" DocumentationNodeKind.CodeBlock
            EnumCase.create "SymbolReference" DocumentationNodeKind.SymbolReference
            EnumCase.create "ExternalLink" DocumentationNodeKind.ExternalLink
            EnumCase.create "UnorderedList" DocumentationNodeKind.UnorderedList
            EnumCase.create "OrderedList" DocumentationNodeKind.OrderedList
            EnumCase.create "ListItem" DocumentationNodeKind.ListItem
            EnumCase.create "LineBreak" DocumentationNodeKind.LineBreak
            EnumCase.create "Markdown" DocumentationNodeKind.Markdown
        ]

    /// `DocumentationNode` nests itself via `Children`, so the schema value must be built lazily
    /// and referred to through `Schema.defer` -- the standard Reified shape for a recursive record.
    let private documentationNode' () =
        let rec lazySchema : Lazy<Schema<DocumentationNode>> =
            lazy
                (schema<DocumentationNode> {
                    fieldAs "Kind" (fun (n: DocumentationNode) -> n.Kind) { withSchema documentationNodeKind }
                    fieldAs "Text" (fun (n: DocumentationNode) -> n.Text) { withSchema (Schema.option Schema.text) }
                    fieldAs "Target" (fun (n: DocumentationNode) -> n.Target) { withSchema (Schema.option Schema.text) }
                    fieldAs "Language" (fun (n: DocumentationNode) -> n.Language) { withSchema (Schema.option Schema.text) }
                    fieldAs "Children" (fun (n: DocumentationNode) -> n.Children) { withSchema (Schema.listWith (Schema.defer (fun () -> lazySchema.Value))) }
                    construct (fun kind text target language children ->
                        { Kind = kind; Text = text; Target = target; Language = language; Children = children })
                })
        lazySchema.Value

    let documentationNode : Schema<DocumentationNode> = documentationNode' ()
    let documentationNodes : Schema<DocumentationNode list> = Schema.listWith documentationNode

    let sourceLink : Schema<SourceLink> =
        schema<SourceLink> {
            fieldAs "File" (fun (s: SourceLink) -> s.File) { withSchema Schema.text }
            fieldAs "Line" (fun (s: SourceLink) -> s.Line) { withSchema Schema.int }
            construct (fun file line -> { File = file; Line = line })
        }

    let exampleModel : Schema<ExampleModel> =
        schema<ExampleModel> {
            fieldAs "Name" (fun (e: ExampleModel) -> e.Name) { withSchema Schema.text }
            fieldAs "Content" (fun (e: ExampleModel) -> e.Content) { withSchema Schema.text }
            fieldAs "ExpectedOutput" (fun (e: ExampleModel) -> e.ExpectedOutput) { withSchema (Schema.option Schema.text) }
            fieldAs "Scenario" (fun (e: ExampleModel) -> e.Scenario) { withSchema (Schema.option Schema.text) }
            fieldAs "IsSnapshotTest" (fun (e: ExampleModel) -> e.IsSnapshotTest) { withSchema Schema.bool }
            fieldAs "NoCheckReason" (fun (e: ExampleModel) -> e.NoCheckReason) { withSchema (Schema.option Schema.text) }
            construct (fun name content expectedOutput scenario isSnapshotTest noCheckReason ->
                { Name = name
                  Content = content
                  ExpectedOutput = expectedOutput
                  Scenario = scenario
                  IsSnapshotTest = isSnapshotTest
                  NoCheckReason = noCheckReason })
        }

    let parameterModel : Schema<ParameterModel> =
        schema<ParameterModel> {
            fieldAs "Name" (fun (p: ParameterModel) -> p.Name) { withSchema Schema.text }
            fieldAs "Type" (fun (p: ParameterModel) -> p.Type) { withSchema Schema.text }
            fieldAs "Description" (fun (p: ParameterModel) -> p.Description) { withSchema documentationNodes }
            construct (fun name type_ description -> { Name = name; Type = type_; Description = description })
        }
