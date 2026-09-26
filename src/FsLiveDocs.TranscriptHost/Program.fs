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
                  Error = $"Unsupported transcript protocol version {request.ProtocolVersion}; this worker speaks {Protocol.Version}." }
            else
                { ProtocolVersion = Protocol.Version
                  Outputs = Evaluation.run request.Examples
                  Error = null }
        with error ->
            { ProtocolVersion = Protocol.Version; Outputs = [||]; Error = error.ToString() }

    protocolOut.Write(Protocol.serializeResponse response)
    protocolOut.Flush()
    if isNull response.Error then 0 else 1
