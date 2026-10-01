module FsLiveDocs.TranscriptHost.Program

open System
open System.IO
open System.Text

[<EntryPoint>]
let main _ =
    let utf8 = UTF8Encoding(false)
    let protocolOut = new StreamWriter(Console.OpenStandardOutput(), utf8)
    // Evaluated code may print. Send that to stderr so it can never corrupt the response on stdout.
    Console.SetOut(Console.Error)

    let response =
        try
            let requestText = (new StreamReader(Console.OpenStandardInput(), utf8)).ReadToEnd()
            let request = Protocol.deserializeRequest requestText

            if request.ProtocolVersion <> Protocol.Version then
                { ProtocolVersion = Protocol.Version
                  Outputs = [||]
                  Timings = [||]
                  Error = $"Unsupported transcript protocol version {request.ProtocolVersion}; this worker speaks {Protocol.Version}." }
            else
                let freshSessions = request.Sessions = TranscriptSessionPolicy.FreshSessionPerExample
                let outputs, timings = Evaluation.run freshSessions request.Examples
                { ProtocolVersion = Protocol.Version
                  Outputs = outputs
                  Timings = timings
                  Error = null }
        with error ->
            { ProtocolVersion = Protocol.Version; Outputs = [||]; Timings = [||]; Error = error.ToString() }

    protocolOut.Write(Protocol.serializeResponse response)
    protocolOut.Flush()
    if isNull response.Error then 0 else 1
