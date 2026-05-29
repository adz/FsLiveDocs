namespace FsLiveDocs.Core

open System

module ExampleTranscript =
    let private normalizeIndent (text: string) =
        let lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
        let nonEmpty = lines |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        if nonEmpty.Length = 0 then ""
        else
            let minIndent =
                nonEmpty
                |> Array.map (fun line -> line.Length - line.TrimStart().Length)
                |> Array.fold min Int32.MaxValue

            lines
            |> Array.map (fun line ->
                if line.Length >= minIndent then line.Substring(minIndent)
                else line.TrimStart())
            |> String.concat "\n"
            |> fun value -> value.Trim()

    let private isFsiInputLine (line: string) =
        let trimmed = line.TrimStart()
        trimmed.StartsWith("> ") || trimmed.StartsWith("- ")

    let private stripFsiPrompt (line: string) =
        let trimmed = line.TrimStart()
        if trimmed.StartsWith("> ") then trimmed.Substring(2)
        elif trimmed.StartsWith("- ") then trimmed.Substring(2)
        else trimmed

    type Parsed = {
        DisplayText: string
        Script: string
        Interactions: string list
        ExpectedOutput: string option
    }

    let private splitSessionInteractions (lines: string array) =
        let interactions = ResizeArray<string>()
        let current = ResizeArray<string>()

        let flushCurrent () =
            if current.Count > 0 then
                interactions.Add(String.concat "\n" current)
                current.Clear()

        for line in lines do
            if isFsiInputLine line then
                let promptLine = stripFsiPrompt line
                if line.TrimStart().StartsWith("> ") then
                    flushCurrent()
                    current.Add(promptLine)
                else
                    current.Add(promptLine)
            else
                flushCurrent()

        flushCurrent()
        interactions |> Seq.toList

    let parse (raw: string) =
        let normalized = normalizeIndent raw
        let lines = normalized.Split('\n')
        let isSession = lines |> Array.exists isFsiInputLine

        if isSession then
            let interactions = splitSessionInteractions lines
            let script =
                interactions |> String.concat "\n\n"

            let output =
                lines
                |> Array.choose (fun line ->
                    if String.IsNullOrWhiteSpace line then None
                    elif isFsiInputLine line then None
                    else Some (line.TrimEnd()))
                |> String.concat "\n"
                |> fun value -> value.Trim()

            {
                DisplayText = normalized
                Script = script
                Interactions = interactions
                ExpectedOutput = if String.IsNullOrWhiteSpace output then None else Some output
            }
        else
            let parts = normalized.Split([| "// EXPECTED:" |], StringSplitOptions.None)
            let content = parts.[0].Trim()
            let expected = if parts.Length > 1 then Some (parts.[1].Trim()) else None
            {
                DisplayText = content
                Script = content
                Interactions = [ content ]
                ExpectedOutput = expected
            }
