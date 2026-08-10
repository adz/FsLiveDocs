namespace FsLiveDocs.Core

// <snippet:DocScenarioPattern>
module DocScenarioSamples =
    // <snippet:DocScenarioAttributeUsage>
    let mutable currentUser = "anonymous"

    /// <summary>Loads deterministic setup for the named documentation scenario.</summary>
    /// <example name="DocScenarioUsage" data-livedocs="snapshot">
    /// > FsLiveDocs.Core.DocScenarioAttribute("AuthenticatedUser").Name;;
    /// val it: string = "AuthenticatedUser"
    /// </example>
    [<DocScenario("with-user")>]
    let loadUser () =
        currentUser <- "Ada"
    // </snippet:DocScenarioAttributeUsage>

    /// <example name="UserGreeting" scenario="with-user">
    /// > FsLiveDocs.Core.DocScenarioSamples.greet();;
    /// val it: string = "Hello Ada"
    /// </example>
    let greet () =
        $"Hello {currentUser}"
// </snippet:DocScenarioPattern>

module TranscriptSamples =
    // <snippet:MapTranscriptExample>
    /// <summary>A transcript example that demonstrates multiline FSI input.</summary>
    /// <example name="MapTranscript" data-livedocs="snapshot">
    /// > let m = Map [("a", "1")];;
    /// val m: Map&lt;string,string&gt; = map [("a", "1")]
    ///
    /// > m |> Map.tryFind "b";;
    /// val it: string option = None
    ///
    /// > m |> Map.tryFind "a";;
    /// val it: string option = Some "1"
    ///
    /// > let updatedA = m |> Map.add "b" "2";;
    /// val updatedA: Map&lt;string,string&gt; = map [("a", "1"); ("b", "2")]
    ///
    /// > updatedA |> Map.tryFind "b";;
    /// val it: string option = Some "2"
    /// </example>
    let mapTranscriptExample () = ()
    // </snippet:MapTranscriptExample>
