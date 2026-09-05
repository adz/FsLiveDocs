namespace FsLiveDocs

open System

/// <summary>Marks a parameterless static method as setup for named XML documentation examples.</summary>
/// <param name="name">The unique scenario name referenced by an XML example.</param>
[<Sealed; AttributeUsage(AttributeTargets.Method, AllowMultiple = false)>]
type DocScenarioAttribute(name: string) =
    inherit Attribute()

    /// <summary>The unique scenario name referenced by an XML example.</summary>
    member _.Name = name
