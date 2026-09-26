namespace FsLiveDocs.TranscriptHost

open System.Text.Json

/// <summary>One documentation example: FSI interactions evaluated in order in a shared session.</summary>
/// <remarks>
/// The first <c>SetupCount</c> blocks prepare the session (references, opens, scenario setup). Their output is dropped
/// unless it reports a compiler error, so the expected transcript never has to repeat setup noise.
/// </remarks>
type TranscriptExample = { Blocks: string array; SetupCount: int }

/// <summary>A request to evaluate examples in one fresh FSI session, in order.</summary>
type TranscriptRequest = { ProtocolVersion: int; Examples: TranscriptExample array }

/// <summary>The worker's reply: one normalized output per example, or an error that prevented evaluation.</summary>
/// <remarks><c>Error</c> is <c>null</c> when every example was evaluated.</remarks>
type TranscriptResponse = { ProtocolVersion: int; Outputs: string array; Error: string }

/// <summary>The JSON contract between FsLiveDocs.Runner and the transcript worker process.</summary>
/// <remarks>
/// The request travels on the worker's stdin and the response on its stdout. Code evaluated by a transcript writes to
/// the worker's stderr instead, so example output can never corrupt the response.
/// </remarks>
module Protocol =
    /// <summary>The protocol version both sides must agree on. A mismatch is rejected rather than interpreted.</summary>
    [<Literal>]
    let Version = 1

    /// <summary>The worker's file name, resolved beside the assembly that declares this protocol.</summary>
    [<Literal>]
    let WorkerFileName = "FsLiveDocs.TranscriptHost.dll"

    let serializeRequest (request: TranscriptRequest) = JsonSerializer.Serialize request
    let deserializeRequest (json: string) = JsonSerializer.Deserialize<TranscriptRequest> json
    let serializeResponse (response: TranscriptResponse) = JsonSerializer.Serialize response
    let deserializeResponse (json: string) = JsonSerializer.Deserialize<TranscriptResponse> json
