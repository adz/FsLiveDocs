namespace FsLiveDocs.Core

open System
open System.Text.RegularExpressions

/// Deterministic organization of factual API symbols into conceptual families and facets.
module ApiOrganizationModel =

    let empty = { Families = []; PackageSections = [] }

    let private facet origin dimension value =
        { Dimension = dimension; Value = value; Origin = origin }

    let private kindFacet kind =
        facet CompilerDerived "kind" ((string kind).ToLowerInvariant())

    let private shapeFacets (signature: string) =
        [ if Regex.IsMatch(signature, @"\bAsync\s*<|\bTask\s*<|\bValueTask\s*<") then
              facet CompilerDerived "shape" "async"
          if Regex.IsMatch(signature, @"\bResult\s*<|\bResult\s*\(") then
              facet CompilerDerived "shape" "result"
          if Regex.IsMatch(signature, @"\boption\b|\bOption\s*<") then
              facet CompilerDerived "shape" "option"
          if Regex.IsMatch(signature, @"\bseq\s*<|\blist\s*<|\barray\b|\bIEnumerable\s*<") then
              facet CompilerDerived "shape" "sequence" ]

    let private defaultSection (entity: EntityModel) =
        match entity.Kind with
        | EntityKind.Record -> "representation", "Representation", 10
        | EntityKind.Union -> "cases", "Union cases", 10
        | _ -> "operations", "Operations", 50

    let private baseId (entity: EntityModel) =
        // Generic arity and the CLR Module suffix are representation details. F# companion
        // modules and types share the remaining identity.
        let withoutArity = Regex.Replace(entity.Id, @"`\d+$", "")
        if entity.Kind = EntityKind.Module && withoutArity.EndsWith("Module", StringComparison.Ordinal)
        then withoutArity.Substring(0, withoutArity.Length - "Module".Length)
        else withoutArity

    let rec flattenEntities (entities: EntityModel list) : EntityModel list =
        [ for entity in entities do
              yield entity
              yield! flattenEntities entity.Entities ]

    let private makeFamily (entities: EntityModel list) =
        let canonical =
            entities
            |> List.tryFind (fun entity -> entity.Kind = EntityKind.Module)
            |> Option.defaultValue entities.Head
        let sectionTuples = entities |> List.map defaultSection |> List.distinct
        let sections =
            sectionTuples
            |> List.map (fun (id, title, order) ->
                { Id = id; Title = title; Summary = []; Order = order })
            |> List.sortBy (fun section -> section.Order, section.Title)
        let placements =
            [ for entity in entities do
                  let sectionId, _, _ = defaultSection entity
                  yield
                      { SymbolId = entity.Id
                        SectionId = Some sectionId
                        Facets = [ kindFacet entity.Kind ] }
                  for memberInfo in entity.Members do
                      yield
                          { SymbolId = memberInfo.Id
                            SectionId = Some sectionId
                            Facets = facet CompilerDerived "kind" "member" :: shapeFacets memberInfo.Signature } ]
        { Id = baseId canonical
          Name = canonical.Name
          EntityIds = entities |> List.map _.Id
          Sections = sections
          Placements = placements }

    /// Creates conservative defaults. Only compiler-confirmed symbol facts and exact companion
    /// identities are inferred; subjective task/capability/audience facets remain author-owned.
    let derive (entities: EntityModel list) =
        flattenEntities entities
        |> List.filter (fun entity -> entity.Kind <> EntityKind.Namespace)
        |> List.groupBy baseId
        |> List.map (snd >> makeFamily)
        |> fun families -> { Families = families; PackageSections = [] }

    let merge left right =
        let mergeFamily (oldFamily: ApiFamily) (newFamily: ApiFamily) =
            { newFamily with
                EntityIds = (oldFamily.EntityIds @ newFamily.EntityIds) |> List.distinct
                Sections = (oldFamily.Sections @ newFamily.Sections) |> List.distinctBy _.Id |> List.sortBy (fun s -> s.Order, s.Title)
                Placements =
                    (oldFamily.Placements @ newFamily.Placements)
                    |> List.groupBy _.SymbolId
                    |> List.map (fun (_, items) ->
                        let winner = List.last items
                        { winner with Facets = items |> List.collect _.Facets |> List.distinctBy (fun f -> f.Dimension, f.Value) }) }
        let families =
            (left.Families @ right.Families)
            |> List.groupBy _.Id
            |> List.map (fun (_, items) -> items |> List.reduce mergeFamily)
        { Families = families
          PackageSections = (left.PackageSections @ right.PackageSections) |> List.distinctBy (fun sectionInfo -> sectionInfo.PackageName, sectionInfo.Id) }

    let familyForEntity organization entityId =
        organization.Families |> List.tryFind (fun family -> family.EntityIds |> List.contains entityId)

    let private titleFromId (id: string) =
        id.Split([| '-'; '_'; ' ' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun part -> part.Substring(0, 1).ToUpperInvariant() + part.Substring(1))
        |> String.concat " "

    /// Applies authored symbol hints over conservative defaults. This is deliberately a pure
    /// operation so extraction, cache reads, capture, and tests all resolve precedence identically.
    let applyAuthoredHints (hints: (string * string option * ApiFacet list) list) organization =
        let hintsBySymbol = hints |> List.map (fun (symbolId, sectionId, facets) -> symbolId, (sectionId, facets)) |> Map.ofList
        let updateFamily family =
            let placements =
                family.Placements
                |> List.map (fun placement ->
                    match hintsBySymbol |> Map.tryFind placement.SymbolId with
                    | None -> placement
                    | Some(sectionId, facets) ->
                        { placement with
                            SectionId = sectionId |> Option.orElse placement.SectionId
                            Facets =
                                placement.Facets @ facets
                                |> List.distinctBy (fun facet -> facet.Dimension, facet.Value) })
            let authoredSectionIds =
                placements |> List.choose _.SectionId |> List.distinct
            let known = family.Sections |> List.map _.Id |> Set.ofList
            let added =
                authoredSectionIds
                |> List.filter (known.Contains >> not)
                |> List.mapi (fun index id ->
                    { Id = id; Title = titleFromId id; Summary = []; Order = 100 + index })
            { family with Sections = (family.Sections @ added) |> List.sortBy (fun s -> s.Order, s.Title); Placements = placements }
        { organization with Families = organization.Families |> List.map updateFamily }

    let authoredFacet dimension value = facet Authored dimension value

    type SectionDirective = {
        Id: string
        Title: string
        Summary: DocumentationNode list
        Order: int
        Symbols: string list
        Members: string list
        Facets: (string * string list) list
    }

    type PackageSectionDirective = {
        PackageName: string
        Id: string
        Title: string
        Summary: DocumentationNode list
        Order: int
        Entities: string list
    }

    type FamilyDirective = {
        FamilyId: string
        Name: string option
        Sections: SectionDirective list
        PackageSections: PackageSectionDirective list
    }

    /// Applies a docs/api front-matter directive. Exact symbol IDs win; member-name selectors
    /// are scoped to this family and rejected when ambiguous by leaving them unmatched.
    let applyDirective (directive: FamilyDirective) (organization: ApiOrganization) =
        let update (family: ApiFamily) =
            if family.Id <> directive.FamilyId && not (family.EntityIds |> List.contains directive.FamilyId) then family
            else
                let memberNameCounts =
                    family.Placements
                    |> List.map (fun placement -> placement.SymbolId.Split('.') |> Array.last)
                    |> List.countBy id |> Map.ofList
                let placementFor (sectionInfo: SectionDirective) (placement: ApiPlacement) =
                    let shortName = placement.SymbolId.Split('.') |> Array.last
                    let explicitlySelected = sectionInfo.Symbols |> List.contains placement.SymbolId
                    let selectedByName = sectionInfo.Members |> List.contains shortName && memberNameCounts |> Map.tryFind shortName = Some 1
                    if explicitlySelected || selectedByName then
                        let inherited =
                            sectionInfo.Facets
                            |> List.collect (fun (dimension, values) -> values |> List.map (fun value -> facet SectionDefault dimension value))
                        { placement with
                            SectionId = Some sectionInfo.Id
                            Facets = placement.Facets @ inherited |> List.distinctBy (fun item -> item.Dimension, item.Value) }
                    else placement
                { family with
                    Name = directive.Name |> Option.defaultValue family.Name
                    Sections =
                        (family.Sections @ (directive.Sections |> List.map (fun s -> { Id = s.Id; Title = s.Title; Summary = s.Summary; Order = s.Order })))
                        |> List.groupBy _.Id |> List.map (snd >> List.last) |> List.sortBy (fun s -> s.Order, s.Title)
                    Placements =
                        family.Placements
                        |> List.map (fun placement -> directive.Sections |> List.fold (fun current sectionInfo -> placementFor sectionInfo current) placement) }
        let packageSections =
            directive.PackageSections
            |> List.map (fun sectionInfo ->
                { PackageName = sectionInfo.PackageName
                  Id = sectionInfo.Id
                  Title = sectionInfo.Title
                  Summary = sectionInfo.Summary
                  Order = sectionInfo.Order
                  EntityIds = sectionInfo.Entities })
        { Families = organization.Families |> List.map update
          PackageSections =
              (organization.PackageSections @ packageSections)
              |> List.groupBy (fun sectionInfo -> sectionInfo.PackageName, sectionInfo.Id)
              |> List.map (snd >> List.last) }
