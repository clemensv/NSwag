# OpenAPI Binding — Samples

Ten complete OpenAPI Description documents demonstrating the binding defined
by [`draft-vasters-json-structure-oas-binding.md`](../draft-vasters-json-structure-oas-binding.md).
Every Schema Object in every sample is a **JSON Structure** schema, selected
either through the OpenAPI Object's `jsonSchemaDialect` field or through an
explicit `$schema` on a resource-root Schema Object, exactly as the binding
specifies. None of these documents introduce new OAS keywords; they only use
the existing `$schema` / `jsonSchemaDialect` dialect-selection seam.

Several samples are adaptations of long-standing, canonical example APIs
published by the OpenAPI Initiative (Petstore, Petstore Expanded, USPTO Data
Set API, API with Examples, Link Example, Callback Example, Non-oAuth
Scopes, and TicTacToe); their request/response shapes are recreated here
with JSON Structure Schema Objects in place of JSON Schema. Two samples
(Webhook Example and Cross-Schema Library Import) are original, written to
exercise binding-specific mechanics that the canonical examples don't cover.

| # | Folder | Adapted from | Demonstrates |
|---|--------|---------------|--------------|
| 1 | [`01-petstore/`](01-petstore/) | OAI "Petstore" | Baseline usage: JSON Structure Core meta-schema selected once via `jsonSchemaDialect`; no per-schema `$schema` needed. Object inheritance kept document-local (no cross-`components.schemas` `$extends`). |
| 2 | [`02-petstore-expanded/`](02-petstore-expanded/) | OAI "Petstore Expanded" | The recommended self-describing pattern: every `components.schemas` entry carries its own `$schema`. Per-schema opt-in via `$uses` for `JSONStructureUnits` (pet `weight` in kg) and `JSONStructureValidation` (`minLength`, `pattern`). |
| 3 | [`03-uspto-data-set-api/`](03-uspto-data-set-api/) | OAI "USPTO Data Set API" | The `map` type (dynamic per-record fields), the `set` type (caller-supplied field-exclusion list), and `JSONStructureValidation` numeric/string bounds (`minimum`, `maximum`, `minLength`). |
| 4 | [`04-api-with-examples/`](04-api-with-examples/) | OAI "API with Examples" | Multiple named OAS `examples` attached to one JSON Structure schema; the core `enum` keyword (available under every dialect, including plain Core). |
| 5 | [`05-link-example/`](05-link-example/) | OAI "Link Example" | OAS Link Objects working unchanged alongside JSON Structure Schema Objects; the core `uuid` type. |
| 6 | [`06-callback-example/`](06-callback-example/) | OAI "Callback Example" | A JSON Structure schema as an OAS Callback Object body; `JSONStructureUnits` (`ucumUnit`, `symbol`) on a sensor reading; the core `datetime` type. |
| 7 | [`07-webhook-example/`](07-webhook-example/) | Original | OAS 3.1 top-level `webhooks`; the `choice` type as a tagged discriminated union (`petCreated` / `petDeleted`) — deliberately *not* OAS `oneOf`/`discriminator`. |
| 8 | [`08-non-oauth-scopes/`](08-non-oauth-scopes/) | OAI "Non-oAuth Scopes" | A vendor extension (`x-apikeyScopes`) documenting API-key scopes; `JSONStructureAlternateNames` (`altnames`) for compact wire aliases and localized (`lang:en`/`lang:de`) display labels. |
| 9 | [`09-tictactoe/`](09-tictactoe/) | OAI "TicTacToe" | The `tuple` type, including a tuple-of-tuples (a 3×3 board made of three 3-cell row tuples); the core `enum` keyword for cell marks. |
| 10 | [`10-schema-library-import/`](10-schema-library-import/) | Original | `JSONStructureImport`'s `$import` (whole external document) and `$importdefs` (definitions only) against a shared library file (`common-types.json`); `JSONStructureConditionalComposition`'s `if`/`then` for a conditionally-required property. |

## Reference-model notes that shaped these samples

These samples distinguish two layers of `$ref`, per the binding's
["Reference Model"](../draft-vasters-json-structure-oas-binding.md#reference-model)
section:

* **The OAS reference layer.** A sibling-key `$ref` (e.g.
  `{ "$ref": "#/components/schemas/Pet" }`) standing in for an *entire*
  Schema Object position — a response body, a request body, a parameter's
  `schema` — is an OAS-level construct, resolved by OAS tooling. It is used
  freely throughout these samples wherever a whole schema is being reused at
  such a position (e.g. `GET /pets/{petId}` responding with
  `$ref: "#/components/schemas/Pet"`).
* **The JSON Structure reference layer.** JSON Structure's own `$ref`/
  `$extends` only ever appear as the value of a `type` attribute (or, for
  `$extends`, as a schema-level keyword), and MUST stay **document-local**:
  they can only reach `#/definitions/...` within the *same* schema resource.
  Critically, this means a bare OAS-style `$ref` **cannot** be used as the
  value of a `properties.*`, `items`, or `choices.*` entry *inside* a JSON
  Structure Schema Object that is itself a `components.schemas` entry (or
  any other resource-root Schema Object) — because per
  ["Materializing Defaults for Standalone Validation"](../draft-vasters-json-structure-oas-binding.md#materializing-defaults),
  that Schema Object MUST be independently valid JSON Structure when
  extracted, and JSON Structure's own `$ref` mechanism does not recognize a
  bare `$ref` sibling as a schema at all.

  This was not just spec reasoning: it was verified against the actual
  [`sdk/python`](../../sdk/python) `json-structure` package's
  `SchemaValidator`. Every sample originally used a bare
  `{"$ref": "#/components/schemas/X"}` inside `properties`/`items`/`choices`
  for internal type reuse (e.g. `Pets.items`, `Row.properties.col1`,
  `Repository.properties.owner`), and the SDK correctly rejected every one
  of them with `SCHEMA_REF_NOT_IN_TYPE` ("'$ref' is only permitted inside
  the 'type' attribute. Use `{ "type": { "$ref": "..." } }` instead of
  `{ "$ref": "..." }`"). All of them have been fixed by duplicating the
  small reused type as a local `definitions` entry on the schema that needs
  it, and referencing it with JSON Structure's own
  `{"type": {"$ref": "#/definitions/X"}}` (see `Pets`, `DataSetList`,
  `VersionList`, `Repository`, `PetEvent`, and `Board` for examples). For
  reuse of a type defined in a *different file*, use `$import`/`$importdefs`
  instead (sample 10) — never a bare cross-`components.schemas` `$ref`
  nested inside another schema's `properties`/`items`/`choices`.

## Validating

Each file's YAML/JSON syntax was checked with a standard parser (Python's
`yaml.safe_load` / `json.load`). Beyond that, every `components.schemas`
entry in every sample was validated for real against the JSON Structure
Core meta-schema using the actual reference implementation,
[`sdk/python`](../../sdk/python) (the `json-structure` PyPI package):

```python
from json_structure import SchemaValidator

# entry is one components.schemas value, with $schema/$id materialized
# per "Materializing Defaults for Standalone Validation"; import_map
# supplies local files for any $import/$importdefs target.
validator = SchemaValidator(allow_import=True, extended=True, import_map=import_map)
errors = validator.validate(entry, source_text)
```

Every `components.schemas` entry across all 10 samples now validates with
**zero errors**. Two other defects surfaced only by actually running the
SDK (not visible from spec prose alone) were fixed along the way: sample
10's `if`/`then` conditional-composition blocks needed an explicit `type`
(the spec's own inline examples omit it, but the SDK requires it on every
schema object, conditional branches included), and two OAS-level response
arrays that are extracted and reused as full response bodies
(`02-petstore-expanded`'s `PetList`, `05-link-example`'s `RepositoryList`)
needed an explicit `name`, since a standalone-validated root schema of any
type requires one.

