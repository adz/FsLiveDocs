namespace FsLiveDocs.TranscriptHost

open System.Text.Json

/// <summary>One documentation example: FSI interactions evaluated in order in a shared session.</summary>
/// <remarks>
/// The first <c>SetupCount</c> blocks prepare the session (references, opens, scenario setup). Their output is dropped
/// unless it reports a compiler error, so the expected transcript never has to repeat setup noise.
/// </remarks>
type TranscriptExample = { Blocks: string array; SetupCount: int }

/// <summary>How one worker request treats the FSI session.</summary>
/// <remarks>
/// Snapshot examples that shadow each other share one session. Independent Markdown examples must not see each other's
/// definitions, so they get a fresh session each while still sharing one worker process.
/// </remarks>
type TranscriptSessionPolicy =
    /// <summary>All examples share one FSI session; later definitions shadow earlier ones.</summary>
    | SharedSession = 0
    /// <summary>Each example runs in its own FSI session in the same worker process.</summary>
    | FreshSessionPerExample = 1

/// <summary>How long one example spent creating its FSI session and evaluating its blocks, in milliseconds.</summary>
type TranscriptExampleTiming = { SessionMs: float; EvalMs: float }

/// <summary>A request to evaluate examples in one worker process, in order.</summary>
type TranscriptRequest =
    { ProtocolVersion: int
      /// <summary>Omitted by older callers, which default to <see cref="TranscriptSessionPolicy.SharedSession" />.</summary>
      Sessions: TranscriptSessionPolicy
      Examples: TranscriptExample array }

/// <summary>The worker's reply: one normalized output and one timing per example, or an error that prevented evaluation.</summary>
/// <remarks><c>Error</c> is <c>null</c> when every example was evaluated.</remarks>
type TranscriptResponse =
    { ProtocolVersion: int
      Outputs: string array
      Timings: TranscriptExampleTiming array
      Error: string }

/// <summary>The JSON contract between FsLiveDocs.Runner and the transcript worker process.</summary>
/// <remarks>
/// The request travels on the worker's stdin and the response on its stdout. Code evaluated by a transcript writes to
/// the worker's stderr instead, so example output can never corrupt the response.
/// </remarks>
module Protocol =
    /// <summary>The protocol version both sides must agree on. A mismatch is rejected rather than interpreted.</summary>
    [<Literal>]
    let Version = 2

    /// <summary>The worker's file name, resolved beside the assembly that declares this protocol.</summary>
    [<Literal>]
    let WorkerFileName = "FsLiveDocs.TranscriptHost.dll"

    let serializeRequest (request: TranscriptRequest) = JsonSerializer.Serialize request
    let deserializeRequest (json: string) = JsonSerializer.Deserialize<TranscriptRequest> json
    let serializeResponse (response: TranscriptResponse) = JsonSerializer.Serialize response
    let deserializeResponse (json: string) = JsonSerializer.Deserialize<TranscriptResponse> json
