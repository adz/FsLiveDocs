namespace FsLiveDocs.Core

open System
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type FSharpListConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) =
        objectType.IsGenericType && objectType.GetGenericTypeDefinition() = typedefof<list<_>>
    override _.WriteJson(writer, value, serializer) =
        let list = value :?> System.Collections.IEnumerable
        list |> Seq.cast<obj> |> Seq.toArray |> fun items -> serializer.Serialize(writer, items)
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        let elementType = objectType.GetGenericArguments().[0]
        let listType = typedefof<ResizeArray<_>>.MakeGenericType(elementType)
        let list = serializer.Deserialize(reader, listType) :?> System.Collections.IEnumerable
        let methodInfo = typedefof<list<_>>.Assembly.GetType("Microsoft.FSharp.Collections.ListModule").GetMethod("OfSeq").MakeGenericMethod(elementType)
        methodInfo.Invoke(null, [| list |])

type FSharpOptionConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) =
        objectType.IsGenericType && objectType.GetGenericTypeDefinition() = typedefof<option<_>>
    override _.WriteJson(writer, value, serializer) =
        let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(value.GetType())
        let case, fields = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(value, value.GetType())
        if case.Name = "None" then
            writer.WriteNull()
        else
            serializer.Serialize(writer, fields.[0])
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        let innerType = objectType.GetGenericArguments().[0]
        if reader.TokenType = JsonToken.Null then
            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(objectType)
            let noneCase = cases |> Array.find (fun c -> c.Name = "None")
            Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(noneCase, [||])
        else
            let value = serializer.Deserialize(reader, innerType)
            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(objectType)
            let someCase = cases |> Array.find (fun c -> c.Name = "Some")
            Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| value |])

type FSharpUnionConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) =
        Microsoft.FSharp.Reflection.FSharpType.IsUnion(objectType) &&
        not (objectType.IsGenericType && (objectType.GetGenericTypeDefinition() = typedefof<option<_>> || objectType.GetGenericTypeDefinition() = typedefof<list<_>>))
    override _.WriteJson(writer, value, serializer) =
        match value with
        | :? CommentsProvider as provider ->
            match provider with
            | NoComments -> writer.WriteNull()
            | Custom html -> serializer.Serialize(writer, {| kind = "custom"; html = html |})
            | Giscus settings -> serializer.Serialize(writer, {| kind = "giscus"; repo = settings.Repo; repoId = settings.RepoId; category = settings.Category; categoryId = settings.CategoryId; theme = settings.Theme |})
        | _ ->
            let case, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(value, value.GetType())
            writer.WriteValue(case.Name)
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        if objectType = typeof<CommentsProvider> then
            if reader.TokenType = JsonToken.Null then NoComments :> obj
            else
                let value = JObject.Load(reader)
                let required name =
                    match value.GetValue(name, StringComparison.OrdinalIgnoreCase) with
                    | null -> invalidOp $"commentsProvider.{name} is required."
                    | token -> token.ToObject<string>()
                match required "kind" with
                | kind when kind.Equals("custom", StringComparison.OrdinalIgnoreCase) -> Custom(required "html") :> obj
                | kind when kind.Equals("giscus", StringComparison.OrdinalIgnoreCase) ->
                    let theme = match value.GetValue("theme", StringComparison.OrdinalIgnoreCase) with | null -> None | token -> token.ToObject<string>() |> Option.ofObj
                    Giscus { Repo = required "repo"; RepoId = required "repoId"; Category = required "category"; CategoryId = required "categoryId"; Theme = theme } :> obj
                | kind -> invalidOp $"Unsupported commentsProvider kind '{kind}'."
        elif reader.TokenType = JsonToken.String then
            let name = reader.Value :?> string
            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(objectType)
            match cases |> Array.tryFind (fun c -> c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) with
            | Some case -> Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||])
            | None when objectType = typeof<SemanticTokenKind> ->
                let plainText = cases |> Array.find (fun case -> case.Name = nameof PlainText)
                Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(plainText, [||])
            | None -> failwithf "Unknown union case %s for type %s" name objectType.Name
        else
            failwithf "Expected string when reading union, got %O" reader.TokenType

/// Persist the configured comment provider as explicit, portable configuration rather than
/// relying on the generic union converter (which intentionally only supports fieldless cases).
type CommentsProviderConverter() =
    inherit JsonConverter()
    override _.CanConvert(objectType) = objectType = typeof<CommentsProvider>
    override _.WriteJson(writer, value, serializer) =
        match value :?> CommentsProvider with
        | NoComments -> writer.WriteNull()
        | Custom html ->
            writer.WriteStartObject()
            writer.WritePropertyName("kind")
            writer.WriteValue("custom")
            writer.WritePropertyName("html")
            writer.WriteValue(html)
            writer.WriteEndObject()
        | Giscus settings ->
            writer.WriteStartObject()
            writer.WritePropertyName("kind")
            writer.WriteValue("giscus")
            writer.WritePropertyName("repo")
            writer.WriteValue(settings.Repo)
            writer.WritePropertyName("repoId")
            writer.WriteValue(settings.RepoId)
            writer.WritePropertyName("category")
            writer.WriteValue(settings.Category)
            writer.WritePropertyName("categoryId")
            writer.WriteValue(settings.CategoryId)
            writer.WritePropertyName("theme")
            serializer.Serialize(writer, settings.Theme)
            writer.WriteEndObject()
    override _.ReadJson(reader, _, _, serializer) =
        if reader.TokenType = JsonToken.Null then NoComments
        else
            let value = JObject.Load(reader)
            let stringValue name =
                match value.GetValue(name, StringComparison.OrdinalIgnoreCase) with
                | null -> invalidOp $"commentsProvider.{name} is required."
                | token when String.IsNullOrWhiteSpace(token.ToString()) -> invalidOp $"commentsProvider.{name} must not be empty."
                | token -> token.ToObject<string>()
            match stringValue "kind" with
            | kind when kind.Equals("custom", StringComparison.OrdinalIgnoreCase) -> Custom(stringValue "html")
            | kind when kind.Equals("giscus", StringComparison.OrdinalIgnoreCase) ->
                let theme =
                    match value.GetValue("theme", StringComparison.OrdinalIgnoreCase) with
                    | null -> None
                    | token -> token.ToObject<string>() |> Option.ofObj
                Giscus { Repo = stringValue "repo"; RepoId = stringValue "repoId"; Category = stringValue "category"; CategoryId = stringValue "categoryId"; Theme = theme }
            | kind -> invalidOp $"Unsupported commentsProvider kind '{kind}'."

module Serialization =
    let jsonSettings =
        let settings = JsonSerializerSettings()
        settings.Converters.Add(FSharpListConverter())
        settings.Converters.Add(FSharpOptionConverter())
        settings.Converters.Add(FSharpUnionConverter())
        settings
