---
title: API families, sections, and facets
---

# API families, sections, and facets

API extraction records compiler symbols as facts. API organization adds a renderer-neutral layer that arranges those symbols for readers without changing their identities.

Organization consists of families, sections, placements, and facets. It is stored in release capsules, so historical sites do not need the old source or compiler.

## Families

A family is one conceptual API page composed from one or more entities. Each entity keeps its compiler ID, source location, members, cross-reference target, and anchor.

FsLiveDocs automatically combines an F# type and companion module when their compiler identities differ only by generic arity or the compiler's `Module` suffix.

For example, these entities form the `Example.Order` family:

```text
namespace Example

type Order = private { Subtotal: decimal }

module Order =
    let create subtotal = { Subtotal = subtotal }
```

The generated page presents the representation and operations together. Links to the type, module, and individual members still resolve to exact symbols.

The inference is conservative. Similar names in different containers do not form a family. Namespace entities remain structural containers rather than family members.

## Default organization

A project needs no organization configuration. Extraction creates one family per concrete entity, then combines compiler-confirmed companion entities.

Default primary sections are:

| Entity kind | Section ID | Title | Order |
| --- | --- | --- | ---: |
| Record | `representation` | Representation | 10 |
| Union | `cases` | Union cases | 10 |
| Module or other type | `operations` | Operations | 50 |

Every symbol remains visible. Explicit configuration changes placement; it never acts as an allow-list.

## XML organization metadata

Use XML metadata when placement is an intrinsic fact about one declaration:

```fsharp
/// <summary>Creates a validated order.</summary>
/// <group ref="construction" />
/// <facet name="task" value="create" />
/// <facet name="audience" value="common" />
let create subtotal = {| Subtotal = subtotal |}
```

`group` selects one primary section. Use a stable lowercase ID in `ref`:

```xml
<group ref="construction" />
```

A text form is also accepted:

```xml
<group>Construction</group>
```

The text form derives the ID by lower-casing the text and replacing non-alphanumeric runs with `-`. Prefer `ref` when an API page declares the section.

A declaration may carry several facets:

```xml
<facet name="capability" value="validation" />
<facet name="capability" value="pricing" />
```

XML metadata works on entities and members. It is extracted from the compiled XML documentation file, so the project must emit XML documentation.

## API-page front matter

Use `docs/api/<entity-id>.md` for family-wide organization. The file still supplies the family's long-form introduction.

```yaml
---
title: Order
api:
  family: Example.Order
  name: Order
  sections:
    - id: construction
      title: Construction
      order: 10
      summary: Create values while preserving Order invariants.
      members: [create, tryCreate]
      facets:
        task: [create]
        audience: [common]
    - id: pricing
      title: Pricing
      order: 20
      symbols:
        - Example.Order.subtotal
        - Example.Order.total
      facets:
        capability: [pricing]
---
```

### Family fields

| Field | Required | Meaning |
| --- | --- | --- |
| `family` | no | Family or entity ID. Defaults to the Markdown filename. |
| `name` | no | Display name for the family. |
| `sections` | no | Ordered section declarations. |

The filename still identifies the entity whose summary receives the Markdown body. `api.family` selects the organization family to modify.

### Section fields

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Stable section identity. |
| `title` | no | Display title. Defaults to the ID. |
| `order` | no | Integer sort order. Defaults to `100`. |
| `summary` | no | Markdown introduction rendered before the members. |
| `members` | no | Member names scoped to this family. |
| `symbols` | no | Exact compiler symbol IDs. |
| `facets` | no | Facets inherited by selected members. |

A section may use both `members` and `symbols`. Exact symbol IDs are appropriate for overloads or names that occur more than once in a family.

A `members` selector applies only when that short name is unique in the family. Ambiguous names remain in their previous section; use `symbols` to place them exactly.

Section IDs are stable data. Changing a title does not change the section anchor or placement identity.

## Package sections

`api.sections` groups members within one family. `api.packageSections` groups entities across a package landing page, the global API overview, and sidebar navigation.

```yaml
api:
  packageSections:
    - package: Example.Core
      id: domain
      title: Domain APIs
      order: 10
      summary: Core values and operations.
      entities:
        - Example.Order
        - Example.Customer
```

| Field | Required | Meaning |
| --- | --- | --- |
| `package` | yes | Exact package name from the documented project. |
| `id` | yes | Stable section identity within that package. |
| `title` | no | Display title. Defaults to the ID. |
| `order` | no | Integer sort order. Defaults to `100`. |
| `summary` | no | Markdown introduction on the package landing page. |
| `entities` | no | Exact entity IDs assigned to the section. |

Configured package sections render in order. Each becomes an expander in the API sidebar.

Package organization is exhaustive once enabled. If any package entity is unassigned, the build fails and lists every missing ID. This prevents a vague catch-all from becoming the largest section.

Package section IDs must be unique within a package. Capsule validation rejects unknown package names and unknown entity IDs.

## Summary and sidebar representation

An entity page's Summary table repeats the family sections and lists each member under its primary section. This provides a compact grouped index before the detailed member cards.

The sidebar expands an entity into links to its populated sections. Section links target the stable family-section anchor on that entity route.

Package groups expand into their configured package sections. Each package section then expands into its entities, which can expand again into member sections.

## Placement precedence

FsLiveDocs resolves organization in this order, from strongest to weakest:

1. API-page section selection by exact `symbols` ID.
2. API-page section selection by unambiguous `members` name.
3. XML `<group>` metadata.
4. Compiler-confirmed companion-family inference.
5. Default section selection by entity kind.

Front matter is applied after extraction, so its placements override XML and generated defaults. Symbols omitted from front matter retain their earlier placement.

A symbol has one primary section. It can have any number of facets.

## Facets

A facet is a dimension and value attached to a symbol:

```text
task:create
audience:common
capability:validation
shape:option
```

Facets classify an API without changing its primary reading order. The renderer displays them on the exact member and preserves machine-readable `data-api-facet` attributes.

### Authored facets

Authors own dimensions that express design intent:

- `task`
- `audience`
- `capability`
- `lifecycle`

Values are library vocabulary. FsLiveDocs does not impose a fixed list of tasks or capabilities.

Custom dimensions are valid:

```xml
<facet name="protocol" value="oauth2" />
```

A facet written directly on a symbol has `Authored` provenance. A facet supplied by a section has `SectionDefault` provenance.

### Compiler-derived facets

FsLiveDocs derives objective facts and marks them `CompilerDerived`.

Every entity receives a `kind` facet matching its entity kind. Every member receives `kind:member`.

Signature inspection can add these `shape` values:

| Facet | Recognized signature shape |
| --- | --- |
| `shape:async` | `Async<_>`, `Task<_>`, or `ValueTask<_>` |
| `shape:result` | `Result<_,_>` or tuple-style `Result` text |
| `shape:option` | F# `option` or `Option<_>` |
| `shape:sequence` | `seq<_>`, `list<_>`, arrays, or `IEnumerable<_>` |

Shape facets describe the signature. They do not claim that an operation is common, safe, advanced, or preferred.

### Provenance

Persisted facet origins are:

| Origin | Meaning |
| --- | --- |
| `Authored` | Declared on the symbol. |
| `SectionDefault` | Inherited from an authored section. |
| `CompilerDerived` | Derived from compiler facts or signature shape. |
| `HistoryDerived` | Reserved for facts computed from release comparison. |

`HistoryDerived` is part of the persisted model, but the current release does not synthesize cross-release facets automatically.

## Rendering and links

Every entity route in a multi-entity family renders the family experience. Existing entity URLs therefore continue to work.

Member IDs remain page anchors. An `xref:` to a member resolves to its entity route and exact member anchor.

Rendered sections carry `data-api-section`. Rendered facet badges carry `data-api-facet`, and organized symbols carry `data-api-symbol`.

Presentation details such as cards, columns, badge styles, and collapsed state are not persisted in capsules.

## Persisted representation

API schema 5 stores organization beside the symbol graph:

- families contain stable IDs, display names, entity IDs, sections, and placements;
- package sections contain package names, stable IDs, titles, summaries, ordering, and entity IDs;
- sections contain IDs, titles, structured summaries, and order;
- placements reference symbol IDs and contain a primary section plus facets;
- facets contain dimension, value, and origin.

Schema 3 and 4 capsules migrate deterministically. Migration derives conservative families and default sections only from the stored graph.

Migration does not retroactively apply current Markdown, XML metadata, or speculative relationships to an old release.

## Validation

Capsule validation rejects:

- duplicate family IDs;
- empty family IDs;
- duplicate section IDs within a family;
- unknown family entity IDs;
- duplicate placements for one symbol;
- placements that reference unknown symbols;
- placements that reference unknown sections;
- empty facet dimensions or values.

Unknown or ambiguous short member selectors do not hide symbols. The symbols retain their prior placement, normally a generated default section.

## Choosing an authoring level

Use the least configuration that communicates the API:

1. Start with automatic families and default sections.
2. Add API-page sections when the default catalogue is too flat.
3. Add section summaries when readers need purpose or invariants before signatures.
4. Add section facets for broad task, audience, capability, or lifecycle classification.
5. Add XML metadata for exceptional declarations whose placement should travel with the code.
6. Use exact symbol IDs only for overloads, ambiguity, or deliberate cross-entity composition.

This order keeps small APIs inexpensive while allowing heavily curated reference pages where they matter.
