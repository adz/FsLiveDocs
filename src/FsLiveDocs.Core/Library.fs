namespace FsLiveDocs.Core

open System

/// <summary>Attribute used to mark a function as a DocTest setup scenario.</summary>
/// <remarks>
/// A scenario provides the necessary context, such as database connections or 
/// dependency injection, required for a code example to execute successfully.
/// </remarks>
/// <example name="DocScenarioUsage">
/// let attr = DocScenarioAttribute("AuthenticatedUser")
/// printfn "%s" attr.Name
/// // EXPECTED: AuthenticatedUser
/// </example>
[<AttributeUsage(AttributeTargets.Method, AllowMultiple = false)>]
type DocScenarioAttribute(name: string) =
    inherit Attribute()
    /// <summary>The unique name of the scenario.</summary>
    member _.Name = name

/// <summary>Internal utility module for basic sanity checks.</summary>
module Say =
    /// <summary>Prints a friendly greeting to the console.</summary>
    /// <param name="name">The name of the person to greet.</param>
    /// <example name="HelloExample">
/// Say.hello "F#"
/// // EXPECTED: Hello F#
/// </example>
    let hello name =
        printfn "Hello %s" name
