namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the extracted-API package graph (entities, members, parameters,
/// examples) persisted as an `ApiModelArtifact` release asset. Wire-compatible with the
/// pre-Reified Newtonsoft format: see `DocumentationSchema` for the shared compatibility rules.
///
/// Every `fieldAs` lambda parameter carries an explicit type annotation: `FsLiveDocs.Core`
/// defines several record types with same-named fields (e.g. `Kind`, `Name`), and bare
/// `field _.X` is ambiguous once they are all in scope via `open FsLiveDocs.Core`.
module ApiSchema =

    let entityKind : Schema<EntityKind> =
        Schema.enum [
            EnumCase.create "Namespace" EntityKind.Namespace
            EnumCase.create "Module" EntityKind.Module
            EnumCase.create "Record" EntityKind.Record
            EnumCase.create "Union" EntityKind.Union
            EnumCase.create "Type" EntityKind.Type
        ]

    let parameterModel : Schema<ParameterModel> = DocumentationSchema.parameterModel
    let exampleModel : Schema<ExampleModel> = DocumentationSchema.exampleModel
    let sourceLink : Schema<SourceLink> = DocumentationSchema.sourceLink
    let documentationNodes : Schema<DocumentationNode list> = DocumentationSchema.documentationNodes

    let memberModel : Schema<MemberModel> =
        schema<MemberModel> {
            fieldAs "Id" (fun (m: MemberModel) -> m.Id) { withSchema Schema.text }
            fieldAs "Name" (fun (m: MemberModel) -> m.Name) { withSchema Schema.text }
            fieldAs "Signature" (fun (m: MemberModel) -> m.Signature) { withSchema Schema.text }
            fieldAs "Parameters" (fun (m: MemberModel) -> m.Parameters) { withSchema (Schema.listWith parameterModel) }
            fieldAs "ReturnType" (fun (m: MemberModel) -> m.ReturnType) { withSchema Schema.text }
            fieldAs "Summary" (fun (m: MemberModel) -> m.Summary) { withSchema documentationNodes }
            fieldAs "Remarks" (fun (m: MemberModel) -> m.Remarks) { withSchema documentationNodes }
            fieldAs "Examples" (fun (m: MemberModel) -> m.Examples) { withSchema (Schema.listWith exampleModel) }
            fieldAs "Location" (fun (m: MemberModel) -> m.Location) { withSchema sourceLink }
            construct (fun id name signature parameters returnType summary remarks examples location ->
                { Id = id
                  Name = name
                  Signature = signature
                  Parameters = parameters
                  ReturnType = returnType
                  Summary = summary
                  Remarks = remarks
                  Examples = examples
                  Location = location })
        }

    /// `EntityModel` nests itself via `Entities`, so the schema value must be built lazily
    /// and referred to through `Schema.defer` -- the standard Reified shape for a recursive record.
    let private entityModel' () =
        let rec lazySchema : Lazy<Schema<EntityModel>> =
            lazy
                (schema<EntityModel> {
                    fieldAs "Id" (fun (e: EntityModel) -> e.Id) { withSchema Schema.text }
                    fieldAs "Name" (fun (e: EntityModel) -> e.Name) { withSchema Schema.text }
                    fieldAs "Kind" (fun (e: EntityModel) -> e.Kind) { withSchema entityKind }
                    fieldAs "Summary" (fun (e: EntityModel) -> e.Summary) { withSchema documentationNodes }
                    fieldAs "Members" (fun (e: EntityModel) -> e.Members) { withSchema (Schema.listWith memberModel) }
                    fieldAs "Examples" (fun (e: EntityModel) -> e.Examples) { withSchema (Schema.listWith exampleModel) }
                    fieldAs "Entities" (fun (e: EntityModel) -> e.Entities) { withSchema (Schema.listWith (Schema.defer (fun () -> lazySchema.Value))) }
                    construct (fun id name kind summary members examples entities ->
                        { Id = id
                          Name = name
                          Kind = kind
                          Summary = summary
                          Members = members
                          Examples = examples
                          Entities = entities })
                })
        lazySchema.Value

    let entityModel : Schema<EntityModel> = entityModel' ()

    let scenarioModel : Schema<ScenarioModel> =
        schema<ScenarioModel> {
            fieldAs "Name" (fun (s: ScenarioModel) -> s.Name) { withSchema Schema.text }
            fieldAs "MethodId" (fun (s: ScenarioModel) -> s.MethodId) { withSchema Schema.text }
            construct (fun name methodId -> { Name = name; MethodId = methodId })
        }

    let packageInfo : Schema<PackageInfo> =
        schema<PackageInfo> {
            fieldAs "Name" (fun (p: PackageInfo) -> p.Name) { withSchema Schema.text }
            fieldAs "EntityIds" (fun (p: PackageInfo) -> p.EntityIds) { withSchema (Schema.listWith Schema.text) }
            construct (fun name entityIds -> { Name = name; EntityIds = entityIds })
        }

    let packageModel : Schema<PackageModel> =
        schema<PackageModel> {
            fieldAs "Version" (fun (p: PackageModel) -> p.Version) { withSchema Schema.text }
            fieldAs "Entities" (fun (p: PackageModel) -> p.Entities) { withSchema (Schema.listWith entityModel) }
            fieldAs "Scenarios" (fun (p: PackageModel) -> p.Scenarios) { withSchema (Schema.listWith scenarioModel) }
            fieldAs "Packages" (fun (p: PackageModel) -> p.Packages) { withSchema (Schema.listWith packageInfo) }
            construct (fun version entities scenarios packages ->
                { Version = version; Entities = entities; Scenarios = scenarios; Packages = packages })
        }

    let apiModelArtifact : Schema<ApiModelArtifact> =
        schema<ApiModelArtifact> {
            fieldAs "SchemaVersion" (fun (a: ApiModelArtifact) -> a.SchemaVersion) { withSchema Schema.``int`` }
            fieldAs "Package" (fun (a: ApiModelArtifact) -> a.Package) { withSchema packageModel }
            construct (fun schemaVersion package -> { SchemaVersion = schemaVersion; Package = package })
        }
