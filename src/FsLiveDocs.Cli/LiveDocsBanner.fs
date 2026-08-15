namespace FsLiveDocs.Cli

open System
open System.Text
open Spectre.Console
open Spectre.Console.Rendering

/// Animated Live Docs wordmark revealed by rising, fading blocks.
module internal LiveDocsBanner =
    let private width = 72
    let private runSeed = Guid.NewGuid().GetHashCode()
    // Generated from Spectre's default Figlet font for the exact text "Live Docs".
    let private wordmark = [|
        "  _       _                    ____                       "
        " | |     (_) __   __   ___    |  _ \\    ___     ___   ___ "
        " | |     | | \\ \\ / /  / _ \\   | | | |  / _ \\   / __| / __|"
        " | |___  | |  \\ V /  |  __/   | |_| | | (_) | | (__  \\__ \\"
        " |_____| |_|   \\_/    \\___|   |____/   \\___/   \\___| |___/"
    |]

    type private CellStyle =
        | Plain
        | Letter
        | Block of int

    let private lerp left right amount =
        byte (Math.Round(float left + (float right - float left) * amount))

    let private letterColor elapsed =
        let neutral = 70uy, 180uy, 235uy
        let dark = 24uy, 68uy, 92uy
        let bright = 150uy, 235uy, 255uy
        let interpolate (r1, g1, b1) (r2, g2, b2) amount =
            lerp r1 r2 amount, lerp g1 g2 amount, lerp b1 b2 amount
        let r, g, b =
            if elapsed < 1600.0 then neutral
            elif elapsed < 1950.0 then interpolate neutral dark ((elapsed - 1600.0) / 350.0)
            elif elapsed < 2350.0 then interpolate dark bright ((elapsed - 1950.0) / 400.0)
            elif elapsed < 2850.0 then interpolate bright neutral ((elapsed - 2350.0) / 500.0)
            else neutral
        $"#{r:X2}{g:X2}{b:X2}"

    let private canvas elapsed =
        let height = wordmark.Length
        let rowInterval = 90.0
        let scrollDuration = float height * rowInterval
        let visibleRows = min height (1 + int (elapsed / rowInterval))
        let effectElapsed = max 0.0 (elapsed - scrollDuration)
        let chars = Array.init height (fun _ -> Array.create width ' ')
        let styles = Array.init height (fun _ -> Array.create width Plain)

        let shade frame row column =
            let mutable value = uint32 (runSeed ^^^ (row * 73856093) ^^^ (column * 19349663) ^^^ (frame * 83492791))
            value <- (value ^^^ (value >>> 16)) * 0x7FEB352Du
            value <- (value ^^^ (value >>> 15)) * 0x846CA68Bu
            int ((value ^^^ (value >>> 16)) % 100u)
        let fadeFor sample = if sample < 72 then 0 elif sample < 87 then 1 elif sample < 96 then 2 else 3
        let blocks = [| '█'; '▓'; '▒'; '░' |]

        if elapsed < scrollDuration then
            // Build an unreadable wall one row at a time. A per-run seed and a changing frame
            // number make every line different on every invocation and keep the wall alive while
            // new rows scroll into view. Most cells remain solid; gradients are sparse texture.
            let frame = int (elapsed / rowInterval)
            for row in 0 .. visibleRows - 1 do
                for column in 0 .. width - 1 do
                    let fade = shade frame row column |> fadeFor
                    chars.[row].[column] <- blocks.[fade]
                    styles.[row].[column] <- Block fade
        else
            // Put the final wordmark behind the complete wall, then lift every wall cell upward.
            // This preserves the last scrolling frame and reveals the letters continuously instead
            // of jumping to a word-shaped mask before the reveal begins.
            for row in 0 .. wordmark.Length - 1 do
                for column in 0 .. min (width - 1) (wordmark.[row].Length - 1) do
                    let glyph = wordmark.[row].[column]
                    if glyph <> ' ' then
                        chars.[row].[column] <- glyph
                        styles.[row].[column] <- Letter

            let finalWallFrame = height - 1
            for row in 0 .. height - 1 do
                for column in 0 .. width - 1 do
                    let initialFade = shade finalWallFrame row column |> fadeFor
                    let launch = 80.0 + float (height - 1 - row) * 155.0 + float ((column * 17) % 9) * 10.0
                    let age = effectElapsed - launch
                    if age < 0.0 then
                        chars.[row].[column] <- blocks.[initialFade]
                        styles.[row].[column] <- Block initialFade
                    elif age < 620.0 then
                        let rise = 1 + int (age / 125.0)
                        let destination = row - rise
                        if destination >= 0 then
                            let fade = min 3 (initialFade + int (age / 190.0))
                            chars.[destination].[column] <- blocks.[fade]
                            styles.[destination].[column] <- Block fade

        let markup = StringBuilder()
        let color style =
            match style with
            | Plain -> "default"
            | Letter -> letterColor effectElapsed
            | Block 0 -> "#72D7FF"
            | Block 1 -> "#5AA9C8"
            | Block 2 -> "#477C91"
            | Block _ -> "#3B5965"

        for row in 0 .. visibleRows - 1 do
            if row > 0 then markup.AppendLine() |> ignore
            let mutable runStart = 0
            let mutable runStyle = styles.[row].[0]
            for column in 1 .. width do
                let changed = column = width || styles.[row].[column] <> runStyle
                if changed then
                    let text = System.String(chars.[row], runStart, column - runStart)
                    markup.Append('[').Append(color runStyle).Append(']')
                        .Append(Markup.Escape(text)).Append("[/]") |> ignore
                    if column < width then
                        runStart <- column
                        runStyle <- styles.[row].[column]
        markup.ToString()

    let render elapsed statusMarkup =
        Rows([|
            Markup(canvas elapsed) :> IRenderable
            Text(" ") :> IRenderable
            Markup(statusMarkup) :> IRenderable
        |])
