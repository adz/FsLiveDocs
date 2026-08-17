namespace FsLiveDocs.Core

open System
open System.IO
open System.Collections.Concurrent
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// <summary>
/// Recovers how the source actually wrote each parameter, for parameters the compiler's typed
/// view cannot name.
/// </summary>
/// <remarks>
/// A parameter destructured in place (<c>let run token (ColdTask operation) = ...</c>) binds
/// <c>operation</c> inside a pattern rather than naming the parameter, so
/// <c>FSharpParameter.Name</c> is <c>None</c> and the documentation is left to invent a name.
/// The untyped syntax tree still holds the pattern, so the text the author wrote can be shown
/// instead of a synthetic placeholder.
/// </remarks>
module SourceParameters =

    let private checker = lazy FSharpChecker.Create()

    let private cache = ConcurrentDictionary<string, Map<int, string list>>()

    /// Slices the source text covered by a range, flattening a multi-line pattern onto one line.
    let private textOf (lines: string array) (range: range) =
        if range.StartLine < 1 || range.EndLine > lines.Length then
            None
        else
            let segment =
                [ for lineNumber in range.StartLine .. range.EndLine ->
                    let line = lines.[lineNumber - 1]
                    let startColumn = if lineNumber = range.StartLine then range.StartColumn else 0
                    let endColumn = if lineNumber = range.EndLine then range.EndColumn else line.Length
                    if startColumn > line.Length || endColumn > line.Length || endColumn < startColumn then ""
                    else line.Substring(startColumn, endColumn - startColumn) ]
                |> String.concat " "

            let collapsed = Text.RegularExpressions.Regex.Replace(segment.Trim(), @"\s+", " ")
            if String.IsNullOrWhiteSpace collapsed then None else Some collapsed

    /// Maps the line declaring a binding to the source text of each of its curried parameters.
    let private parseFile (path: string) =
        try
            let source = File.ReadAllText(path)
            let lines = source.Replace("\r\n", "\n").Split('\n')
            let options, _ =
                checker.Value.GetParsingOptionsFromCommandLineArgs([ path ])
            let parsed =
                checker.Value.ParseFile(path, SourceText.ofString source, options)
                |> Async.RunSynchronously

            let collected = ResizeArray<int * string list>()

            let recordBinding (SynBinding(headPat = headPat)) =
                match headPat with
                | SynPat.LongIdent(longDotId = identifier; argPats = SynArgPats.Pats patterns) when
                    not (List.isEmpty patterns)
                    ->
                    let line =
                        identifier.LongIdent
                        |> List.tryLast
                        |> Option.map (fun part -> part.idRange.StartLine)
                    match line with
                    | Some line ->
                        // Parentheses and type annotations belong to the declaration, not to the
                        // name of the argument: `(Deferred signal: Deferred<_,_>)` reads as
                        // `Deferred signal`.
                        let rec unwrap pattern =
                            match pattern with
                            | SynPat.Paren(pat = inner)
                            | SynPat.Typed(pat = inner) -> unwrap inner
                            | other -> other

                        let texts =
                            patterns
                            |> List.map (fun p -> textOf lines (unwrap p).Range |> Option.defaultValue "")
                        collected.Add(line, texts)
                    | None -> ()
                | _ -> ()

            let rec walkDeclarations declarations =
                for declaration in declarations do
                    match declaration with
                    | SynModuleDecl.Let(bindings = bindings) -> bindings |> List.iter recordBinding
                    | SynModuleDecl.NestedModule(decls = nested) -> walkDeclarations nested
                    | SynModuleDecl.Types(typeDefns = typeDefns) ->
                        for SynTypeDefn(typeRepr = typeRepr; members = extraMembers) in typeDefns do
                            // A class/interface body's own members live in the object-model
                            // representation; `extraMembers` holds only members added after the
                            // fact, such as a `type Foo with ...` augmentation.
                            let bodyMembers =
                                match typeRepr with
                                | SynTypeDefnRepr.ObjectModel(members = members) -> members
                                | _ -> []

                            for memberDefn in bodyMembers @ extraMembers do
                                match memberDefn with
                                | SynMemberDefn.Member(memberDefn = binding) -> recordBinding binding
                                | SynMemberDefn.LetBindings(bindings = bindings) -> bindings |> List.iter recordBinding
                                | _ -> ()
                    | _ -> ()

            match parsed.ParseTree with
            | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
                for SynModuleOrNamespace(decls = declarations) in modules do
                    walkDeclarations declarations
            | ParsedInput.SigFile _ -> ()

            collected |> Seq.distinctBy fst |> Map.ofSeq
        with _ ->
            // Source is unavailable or unparsable; callers fall back to a synthetic name.
            Map.empty

    /// <summary>
    /// The source text of each curried parameter of the binding declared at a location, if the
    /// declaring file can be read.
    /// </summary>
    let parameterTexts (file: string) (line: int) : string list =
        if String.IsNullOrWhiteSpace file || not (File.Exists file) then
            []
        else
            let byLine = cache.GetOrAdd(file, parseFile)
            byLine |> Map.tryFind line |> Option.defaultValue []
