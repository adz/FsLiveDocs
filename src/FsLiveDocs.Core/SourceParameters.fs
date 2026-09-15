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

    /// <summary>A parameterised binding as the source declares it.</summary>
    type SourceBinding = {
        /// The binding's identifier, without any self-identifier prefix such as <c>this.</c>.
        Name: string
        /// Enclosing namespace, module and type names, outermost first.
        Containers: string list
        /// The range of the binding's identifier.
        Range: range
        /// The source text of each parameter, in flattened curried order.
        Parameters: string list
    }

    /// <summary>What is known about a compiled symbol whose source parameters are wanted.</summary>
    type BindingQuery = {
        File: string
        /// Logical and compiled names; either may match the source identifier.
        Names: string list
        /// Enclosing namespace, module and type names, outermost first; empty when unknown.
        Containers: string list
        /// <summary>
        /// The line the symbol reports. It is only a hint: an assembly-backed symbol's
        /// <c>DeclarationLocation</c> can point at the preceding XML documentation, or even at the
        /// enclosing type's or module's documentation, rather than at the binding itself.
        /// </summary>
        Line: int
        /// The number of flattened parameters the symbol exposes.
        Arity: int
    }

    let private checker = lazy FSharpChecker.Create()

    let private cache = ConcurrentDictionary<string, SourceBinding list>()

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

    /// <summary>
    /// Conditional-compilation symbols assumed present so a member declared inside a target-framework
    /// <c>#if</c> block (<c>#if NET8_0_OR_GREATER</c>) is not silently dropped from this standalone
    /// reparse. This has no access to the declaring project's actual <c>DefineConstants</c>, so it
    /// defines every common TFM/tooling symbol at once rather than guessing one; an extra branch
    /// included in the parse tree is harmless; a target line missing from it is not.
    /// </summary>
    let private assumedDefines =
        [ "NET"
          "NET8_0"; "NET8_0_OR_GREATER"
          "NET9_0"; "NET9_0_OR_GREATER"
          "NET10_0"; "NET10_0_OR_GREATER"
          "NETSTANDARD"; "NETSTANDARD2_0"; "NETSTANDARD2_0_OR_GREATER"
          "NETSTANDARD2_1"; "NETSTANDARD2_1_OR_GREATER"
          "FABLE_COMPILER" ]
        |> List.map (fun symbol -> $"--define:{symbol}")

    /// Every parameterised binding a file declares, with its identity and parameter texts.
    let private parseFile (path: string) : SourceBinding list =
        try
            let source = File.ReadAllText(path)
            let lines = source.Replace("\r\n", "\n").Split('\n')
            let options, _ =
                checker.Value.GetParsingOptionsFromCommandLineArgs([ path ], assumedDefines)
            let parsed =
                checker.Value.ParseFile(path, SourceText.ofString source, options)
                |> Async.RunSynchronously

            let collected = ResizeArray<SourceBinding>()

            let recordBinding (containers: string list) (SynBinding(headPat = headPat)) =
                match headPat with
                | SynPat.LongIdent(longDotId = identifier; argPats = SynArgPats.Pats patterns) when
                    not (List.isEmpty patterns)
                    ->
                    match List.tryLast identifier.LongIdent with
                    | Some nameIdent ->
                        // Parentheses and type annotations belong to the declaration, not to the
                        // name of the argument: `(Deferred signal: Deferred<_,_>)` reads as
                        // `Deferred signal`.
                        let rec unwrap pattern =
                            match pattern with
                            | SynPat.Paren(pat = inner)
                            | SynPat.Typed(pat = inner) -> unwrap inner
                            | other -> other

                        // A .NET-style member's multiple arguments parse as one tuple pattern
                        // (`Create(_, inner)`), not as separate curried patterns, so each tuple
                        // element is its own parameter and needs its own recovered text.
                        let textsFor pattern =
                            match unwrap pattern with
                            | SynPat.Tuple(elementPats = elements) ->
                                elements |> List.map (fun element -> textOf lines (unwrap element).Range |> Option.defaultValue "")
                            | other -> [ textOf lines other.Range |> Option.defaultValue "" ]

                        let texts = patterns |> List.collect textsFor
                        collected.Add
                            { Name = nameIdent.idText
                              Containers = containers
                              Range = nameIdent.idRange
                              Parameters = texts }
                    | None -> ()
                | _ -> ()

            let namesOf (ids: LongIdent) = ids |> List.map _.idText

            let rec walkDeclarations containers declarations =
                for declaration in declarations do
                    match declaration with
                    | SynModuleDecl.Let(bindings = bindings) -> bindings |> List.iter (recordBinding containers)
                    | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = moduleId); decls = nested) ->
                        walkDeclarations (containers @ namesOf moduleId) nested
                    | SynModuleDecl.Types(typeDefns = typeDefns) ->
                        for SynTypeDefn(typeInfo = SynComponentInfo(longId = typeId); typeRepr = typeRepr; members = extraMembers) in typeDefns do
                            let recordBinding = recordBinding (containers @ namesOf typeId)
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
                for SynModuleOrNamespace(longId = rootId; decls = declarations) in modules do
                    walkDeclarations (namesOf rootId) declarations
            | ParsedInput.SigFile _ -> ()

            List.ofSeq collected
        with _ ->
            // Source is unavailable or unparsable; callers fall back to a synthetic name.
            []

    /// <summary>Every parameterised binding the file declares, or none if it cannot be read.</summary>
    let bindings (file: string) : SourceBinding list =
        if String.IsNullOrWhiteSpace file || not (File.Exists file) then []
        else cache.GetOrAdd(file, parseFile)

    /// A module compiled with <c>ModuleSuffix</c> is named <c>FooModule</c> but written <c>Foo</c>.
    let private sameContainerName (left: string) (right: string) =
        let strip (name: string) =
            if name.Length > 6 && name.EndsWith("Module", StringComparison.Ordinal) then name.Substring(0, name.Length - 6)
            else name
        left = right || strip left = strip right

    /// Whether a source binding's containers can be the symbol's: the source may omit leading
    /// segments (a namespace opened elsewhere), so the shorter path must be a tail of the longer.
    let private containersAgree (query: string list) (source: string list) =
        let q, s = List.rev query, List.rev source
        let n = min q.Length s.Length
        List.forall2 sameContainerName (List.truncate n q) (List.truncate n s)

    /// <summary>
    /// Picks the source binding a compiled symbol was declared by, using its identity rather than
    /// trusting the reported line alone.
    /// </summary>
    /// <remarks>
    /// Candidates must share the symbol's name. Among those, bindings in agreeing containers and
    /// with a compatible parameter count are preferred; the reported line then breaks any remaining
    /// tie, favouring the binding it names exactly, then the nearest one declared after it (the
    /// line tends to fall on documentation above the binding), then the nearest one before.
    /// </remarks>
    let resolve (query: BindingQuery) (candidates: SourceBinding list) : SourceBinding option =
        let preferWhere predicate items =
            match List.filter predicate items with
            | [] -> items
            | preferred -> preferred

        let arityFits (binding: SourceBinding) =
            binding.Parameters.Length = query.Arity || (binding.Parameters.Length = 1 && query.Arity > 1)

        let named = candidates |> List.filter (fun b -> query.Names |> List.contains b.Name)
        if List.isEmpty named then
            None
        else
            named
            |> preferWhere (fun b -> List.isEmpty query.Containers || containersAgree query.Containers b.Containers)
            |> preferWhere (fun b -> b.Parameters.Length = query.Arity)
            |> preferWhere arityFits
            |> List.sortBy (fun b ->
                let delta = b.Range.StartLine - query.Line
                if delta = 0 then (0, 0)
                elif delta > 0 then (1, delta)
                else (2, -delta))
            |> List.tryHead

    /// <summary>
    /// The source text of each flattened parameter of the binding a compiled symbol was declared
    /// by, if the declaring file can be read and the binding identified.
    /// </summary>
    let parameterTextsFor (query: BindingQuery) : string list =
        bindings query.File
        |> resolve query
        |> Option.map _.Parameters
        |> Option.defaultValue []

    /// <summary>
    /// The source text of each curried parameter of the binding declared exactly at a line.
    /// </summary>
    let parameterTexts (file: string) (line: int) : string list =
        bindings file
        |> List.tryFind (fun b -> b.Range.StartLine = line)
        |> Option.map _.Parameters
        |> Option.defaultValue []
