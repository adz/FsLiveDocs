namespace FsLiveDocs.Core

open System

// <snippet:DocScenarioAttributeUsage>
/// <summary>Attribute used to mark a function as a DocTest setup scenario.</summary>
/// <remarks>
/// A scenario provides the necessary context, such as database connections or
/// dependency injection, required for a code example to execute successfully.
/// </remarks>
/// <example name="DocScenarioUsage">
/// > DocScenarioAttribute("AuthenticatedUser").Name;;
/// val it: string = "AuthenticatedUser"
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
    /// > greet();;
    /// val it: string = "Hello Ada"
    /// </example>
    let greet () =
        $"Hello {currentUser}"
// </snippet:DocScenarioPattern>

module TranscriptSamples =
    // <snippet:MapTranscriptExample>
    /// <summary>A transcript example that demonstrates multiline FSI input.</summary>
    /// <example name="MapTranscript">
    /// > let m = Map [("a", "1")];;
    /// val m: Map&lt;string,string&gt; = map [("a", "1")]
    ///
    /// > m |> Map.tryFind "b";;
    /// val it: string option = None
    ///
    /// > m |> Map.tryFind "a";;
    /// val it: string option = Some "1"
    ///
    /// > let updatedA = m |> Map.add "b" "2"
    /// - updatedA |> Map.tryFind "b";;
    /// val updatedA: Map&lt;string,string&gt; = map [("a", "1"); ("b", "2")]
    /// val it: string option = Some "2"
    /// </example>
    let mapTranscriptExample () = ()
    // </snippet:MapTranscriptExample>
