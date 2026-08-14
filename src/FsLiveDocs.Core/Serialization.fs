namespace FsLiveDocs.Core

open System
open Newtonsoft.Json

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
        let case, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(value, value.GetType())
        writer.WriteValue(case.Name)
    override _.ReadJson(reader, objectType, existingValue, serializer) =
        if reader.TokenType = JsonToken.String then
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

module Serialization =
    let jsonSettings =
        let settings = JsonSerializerSettings()
        settings.Converters.Add(FSharpListConverter())
        settings.Converters.Add(FSharpOptionConverter())
        settings.Converters.Add(FSharpUnionConverter())
        settings
