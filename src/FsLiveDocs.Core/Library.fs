namespace FsLiveDocs.Core

open System

// <snippet:DocScenarioAttributeUsage>
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
// </snippet:DocScenarioAttributeUsage>

// <snippet:DocScenarioPattern>
module DocScenarioSamples =
    let mutable currentUser = "anonymous"

    [<DocScenario("with-user")>]
    let loadUser () =
        currentUser <- "Ada"

    /// <example name="UserGreeting" scenario="with-user">
    /// printfn "Hello %s" currentUser
    /// // EXPECTED: Hello Ada
    /// </example>
    let greet () =
        printfn "Hello %s" currentUser
// </snippet:DocScenarioPattern>
