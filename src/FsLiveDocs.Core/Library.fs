namespace FsLiveDocs.Core

open System

[<AttributeUsage(AttributeTargets.Method, AllowMultiple = false)>]
type DocScenarioAttribute(name: string) =
    inherit Attribute()
    member _.Name = name

module Say =
    let hello name =
        printfn "Hello %s" name
