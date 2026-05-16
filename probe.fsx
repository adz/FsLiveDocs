#r "nuget: FSharp.Compiler.Service"
open FSharp.Compiler.Symbols
open FSharp.Compiler.Xml
let t = typeof<FSharpXmlDoc>
printfn "FSharpXmlDoc cases:"
for case in Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(t) do
    printfn "  %s" case.Name
let t2 = typeof<XmlDoc>
printfn "XmlDoc cases:"
for case in Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(t2) do
    printfn "  %s" case.Name
