namespace FsLiveDocs.Core

open System

/// The renderer-neutral meaning of one API documentation node.
type DocumentationNodeKind =
    | Text
    | Paragraph
    | InlineCode
    | CodeBlock
    | SymbolReference
    | ExternalLink
    | UnorderedList
    | OrderedList
    | ListItem
    | LineBreak
    | Markdown

/// An FsLiveDocs-owned API documentation node persisted without presentation markup.
type DocumentationNode = {
    Kind: DocumentationNodeKind
    Text: string option
    Target: string option
    Language: string option
    Children: DocumentationNode list
}

/// Helpers for constructing and consuming renderer-neutral documentation.
module Documentation =

    let text value =
        {
            Kind = DocumentationNodeKind.Text
            Text = Some value
            Target = None
            Language = None
            Children = []
        }

    let markdown value =
        {
            Kind = DocumentationNodeKind.Markdown
            Text = Some value
            Target = None
            Language = None
            Children = []
        }

    let rec plainText nodes =
        nodes
        |> List.map (fun node ->
            match node.Kind with
            | DocumentationNodeKind.LineBreak -> "\n"
            | _ ->
                let own = node.Text |> Option.defaultValue ""
                let children = plainText node.Children
                own + children)
        |> String.concat ""
        |> fun value ->
            value.Replace("\r\n", "\n").Replace("\r", "\n")
        |> fun value -> System.Text.RegularExpressions.Regex.Replace(value, @"[ \t\n]+", " ").Trim()

    let isEmpty nodes =
        nodes |> plainText |> String.IsNullOrWhiteSpace
