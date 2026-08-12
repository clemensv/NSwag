# JSON Structure support

NSwag can read JSON Structure schemas embedded in OpenAPI 3.1 documents, generate C# and TypeScript contracts from them, and generate OpenAPI 3.1 documents from JSON Structure schemas. JSON Structure is opt-in. Existing JSON Schema behavior remains the default.

This feature is intended for developers and build engineers who use the NSwag CLI or the NSwag APIs. The JSON Structure integration is provided by the `NSwag.JsonStructure` assembly. Load it when you need JSON Structure parsing, generation, or OpenAPI 3.1 output.

## Supported dialects

NSwag recognizes these canonical JSON Structure meta-schema URIs exactly, including case and the trailing `/#`:

| Dialect | URI | Default behavior |
|---|---|---|
| `Core` | `https://json-structure.org/meta/core/v0/#` | Core structural types and keywords |
| `Extended` | `https://json-structure.org/meta/extended/v0/#` | Core plus the import add-in |
| `Validation` | `https://json-structure.org/meta/validation/v0/#` | Extended plus validation, conditional composition, alternate names, and units |

`Extended` is the default dialect for JSON Structure settings. An unknown URI in the `https://json-structure.org/` namespace is rejected; NSwag does not silently treat it as ordinary JSON Schema. Derived meta-schemas must be explicitly registered with their confirmed base dialect through `JsonStructureSettings.RegisterDerivedMetaSchema` (or added to the legacy `JsonStructureDerivedMetaSchemaAllowlist`, which uses the configured default dialect). URI matching is exact.

The `Extended` and `Validation` dialects offer the following add-ins through `$uses`: `JSONStructureImport`, `JSONStructureValidation`, `JSONStructureConditionalComposition`, `JSONStructureAlternateNames`, and `JSONStructureUnits`. `Validation` activates all of these add-ins by default. Conditional-composition keywords are validation-only and do not change generated type shapes.

## Conformance scope

NSwag implements the JSON Structure Core schema language with a rule-based
conformance validator. It covers the Core primitive and compound types,
namespaces, definitions, local references, `$root`, ordered `$extends`,
abstract types, ordered unions, choices, tuples, sets, maps, required
properties, constants, enumerations, and Core annotations. The validator
enforces the normative Core document, identifier, reference, composition, and
keyword constraints and is backed by the official Core examples and negative
cases.

Schema conformance and generated wire behavior are separate concerns. Generated
code preserves Core type distinctions and metadata, but serializer-specific
limits remain for runtime validation, arbitrary multi-member C# unions, and
unsupported binary compression/media encodings. These cases produce explicit
diagnostics rather than silent lossy output. `$import` and `$importdefs` are
add-in features, not Core features, and are policy-controlled and disabled by
default.

## Import security and offline defaults

JSON Structure `$import` and `$importdefs` are disabled by default. The default policy is offline and has these limits:

| Setting | Default |
|---|---|
| `AllowNetwork` | `false` |
| `AllowFileSystem` | `false` |
| `AllowedHosts` | Empty |
| `MaxDocumentSize` | `1048576` bytes (1 MiB) |
| `Timeout` | 30 seconds per import |
| `BaseDirectory` | Not set |

Enable only the source types your build requires. When network imports are enabled, use `AllowedHosts` to restrict contacted HTTP and HTTPS hosts. When file imports are enabled, set `BaseDirectory` when relative paths must be resolved from a known directory. These settings apply to JSON Structure imports, not to the CLI's normal loading of the primary OpenAPI input.

## Use JSON Structure from the CLI

Set `SchemaDialect` to `JsonStructure` on a supported command:

- `openapi2csclient`
- `openapi2tsclient`
- `aspnetcore2openapi`

The command-line names are case-insensitive in normal NSwag command processing. The corresponding settings are:

| CLI setting | Purpose |
|---|---|
| `SchemaDialect=JsonStructure` | Use JSON Structure schemas instead of NSwag's existing JSON Schema path |
| `JsonStructureDialect=Core\|Extended\|Validation` | Select the canonical dialect for generated schemas |
| `JsonStructureAllowNetwork=true` | Enable network imports |
| `JsonStructureAllowFileSystem=true` | Enable local-file imports |
| `JsonStructureAllowedHosts` | Allowlisted network hosts |
| `JsonStructureMaxDocumentSize=...` | Set the import size limit in bytes |
| `JsonStructureImportTimeout=...` | Set the per-import timeout in seconds |
| `JsonStructureBaseDirectory=...` | Set the base directory for local imports |
| `JsonStructureDerivedMetaSchemaAllowlist=...` | Register a derived meta-schema URI |
| `OutputType=OpenApi31JsonStructure` | Write JSON Structure schemas as OpenAPI 3.1 |

For example:

```text
nswag openapi2csclient /input:openapi-3.1.json /output:Contracts.cs /schemadialect:JsonStructure /jsonstructuredialect:Extended
```

Use the actual option spelling emitted by your NSwag build's command help when scripting a version that differs from the current command-line parser. The option names above are the command argument names exposed by the implementation.

## Configure `nswag.json`

The same settings are available as JSON properties on the command configuration. Use lower camel case in a JSON configuration file:

```json
{
  "runtime": "Net80",
  "codeGenerators": {
    "openApiToCSharpClient": {
      "input": "openapi-3.1.json",
      "output": "Contracts.cs",
      "schemaDialect": "JsonStructure",
      "jsonStructureDialect": "Extended",
      "jsonStructureAllowNetwork": false,
      "jsonStructureAllowFileSystem": false,
      "jsonStructureAllowedHosts": [],
      "jsonStructureMaxDocumentSize": 1048576,
      "jsonStructureImportTimeout": 30,
      "jsonStructureBaseDirectory": "contracts",
      "jsonStructureDerivedMetaSchemaAllowlist": []
    }
  }
}
```

For a document generator, put the settings on the relevant `aspNetCoreToOpenApi` command object. For document output, set `outputType` to `OpenApi31JsonStructure` on the command that writes the document. The exact input property remains command-specific; keep the existing `input`, `url`, or `fromDocument` shape used by that command.

## OpenAPI 3.1 input and output

### Input

JSON Structure Schema Objects are recognized when the input document is OpenAPI 3.1 and carries a JSON Structure `$schema`, or inherits a JSON Structure dialect from `jsonSchemaDialect`. JSON Structure processing is not enabled for Swagger 2.0 or OpenAPI 3.0 inputs.

NSwag lifts JSON Structure schemas before normal OpenAPI deserialization and retains their source JSON. This allows references, definitions, namespaces, and JSON Structure keywords to survive the OpenAPI object model.

### Output

`OpenApi31JsonStructure` writes an OpenAPI 3.1 JSON or YAML document, adds `openapi: 3.1.0`, and writes the common JSON Structure URI as `jsonSchemaDialect`. It removes the internal JSON Structure placeholders and restores the original schema JSON.

The document must contain only JSON Structure schemas, every lifted schema must be represented, and all lifted schemas must use one common dialect. A mixed document or a document with multiple dialects is rejected. OpenAPI 3.1 JSON Structure output deliberately has no Swagger 2.0 or OpenAPI 3.0 downgrade. JSON Structure keywords and types cannot be represented faithfully by those older schema dialects, so NSwag fails rather than silently losing information.

When extracting a Schema Object from OpenAPI context, NSwag applies the binding's
dialect precedence and materializes missing `$schema`, `$id`, and root `name`
values. `$id` is derived from the OpenAPI `$self` or retrieval URI plus the
Schema Object's JSON Pointer; extraction without either a base URI or an
explicit `$id` is rejected.

## Generated type shapes

The generators use the type-shaping information in JSON Structure. Names are sanitized for the target language, and collisions receive a numeric suffix.

| JSON Structure shape | C# | TypeScript |
|---|---|---|
| Object | `partial class` with properties; `abstract` and `$extends` are preserved | `interface` by default, or a class when the TypeScript settings request classes |
| Array | Configured array type, normally `T[]` or the configured collection | `T[]` |
| Set | `HashSet<T>` | `Set<T>` |
| Map | `Dictionary<string, T>` | `{ [key: string]: T }` |
| Tuple | Named `partial record struct` when declared; otherwise `ValueTuple<...>` | Named tuple type, such as `[string, number]` |
| Tagged choice | Abstract class with variants and discriminator handling | Union of variants, with the selector property included when present |
| Inline choice | Wrapper class with a `Value` property and a generated converter | Union of the alternatives; selector constraints are represented as intersections |
| Namespace | Nested C# namespaces | Nested TypeScript `namespace` blocks |

A choice discriminator is inferred from a variant property with `const`. A C# tagged choice with no usable discriminator is not made losslessly polymorphic by the generated serializers. Non-null unions with more than one member are generated as `object` in C# and as a TypeScript union; inspect the generated C# warning before relying on serialization.

### C# primitive mapping

The date, date-time, and time mappings also honor the configured C# generator settings.

| JSON Structure type | C# type |
|---|---|
| `null` | `void` |
| `boolean` | `bool` |
| `string` | `string` |
| `int8`, `uint8` | `sbyte`, `byte` |
| `int16`, `uint16` | `short`, `ushort` |
| `int32`, `integer`, `uint32` | `int`, `uint` |
| `int64`, `uint64` | `long`, `ulong` |
| `int128`, `uint128` | `System.Int128`, `System.UInt128` |
| `float8`, `float` | `float` |
| `double`, `number` | `double` |
| `decimal` | `decimal` |
| `binary` | `byte[]` |
| `date` | Configured `DateType` |
| `datetime` | Configured `DateTimeType` |
| `time` | Configured `TimeType` |
| `duration` | Configured `TimeSpanType` |
| `uuid` | `System.Guid` |
| `uri`, `jsonpointer` | `System.Uri`, `string` |
| `any` or unknown | `object` |

Nullable references and value types follow the C# generator's nullable settings. Arrays, maps, tuples, references, choices, and sets use the enclosing settings and resolved named types.

### TypeScript primitive mapping

| JSON Structure type | TypeScript type |
|---|---|
| `null` | `null` |
| `boolean` | `boolean` |
| `string`, `uuid`, `uri`, `jsonpointer` | `string` |
| `number`, `integer`, `int8`–`int32`, `uint8`–`uint32`, `float8`, `float`, `double` | `number` |
| `int64`, `uint64`, `int128`, `uint128` | `bigint` for TypeScript 4.3 or later; otherwise `string` |
| `decimal`, `duration` | `string` |
| `date`, `datetime`, `time` | The configured date type, such as `string`, `Date`, `moment.Moment`, `DateTime`, or `dayjs.Dayjs` |
| `binary` | `Uint8Array` for TypeScript 5.0 or later; otherwise `string` |
| `any` or unknown | `any` |

Nullable types are emitted as `T | null`. The generator marks optional object properties according to `MarkOptionalProperties` and the required sets in the source schema.

## Serializer differences

C# generation follows the selected `CSharpJsonLibrary`:

- **System.Text.Json** uses `JsonPolymorphic` and `JsonDerivedType` attributes for tagged choices when the variants have usable discriminators. It generates custom converters for inline choices and tuples, and a `JsonStructureConverters.GetOptions()` helper for types such as 64-bit integers, 128-bit integers, decimals, and durations.
- **Newtonsoft.Json** uses generated `JsonConverter` implementations for tuples, inline choices, and tagged choices with object variants and `const` discriminator properties. If a tagged choice does not meet those conditions, the generator emits a warning instead of claiming complete polymorphic support.

Use the generated serializer options or converters in the same way as the selected library's normal NSwag output. TypeScript generation emits types only; it does not provide a runtime serializer.

## Capability and limitation matrix

| Capability | Status | Notes |
|---|---|---|
| Parse canonical Core, Extended, and Validation dialects | Supported with restrictions | Core conformance is enforced by the rule-based validator; add-ins remain dialect- and policy-controlled |
| Read explicitly allowlisted derived dialects | Supported | The caller must register the URI |
| Parse Core definitions, references, objects, arrays, sets, maps, tuples, choices, and hierarchies | Supported at the model level | Core shapes are preserved; generated runtime validation and all wire semantics are not complete |
| Validate JSON Structure Core documents | Supported | Normative Core rules are enforced and covered by the official Core corpus; the implementation does not execute the meta-schema as a generic interpreter |
| Resolve local and imported definitions | Supported with policy | Imports are disabled by default and remain size- and timeout-limited |
| Generate JSON Structure schemas from ASP.NET Core types | Supported | Select `SchemaDialect=JsonStructure` |
| Generate C# contracts | Supported with restrictions | Use `openapi2csclient`; serializer and union limitations apply |
| Generate TypeScript contracts | Supported with restrictions | Use `openapi2tsclient`; TypeScript types are emitted, not a runtime serializer |
| Read JSON Structure in OpenAPI 3.1 | Supported | `jsonSchemaDialect` and schema-level dialects are handled |
| Read JSON Structure in Swagger 2.0 | Not supported | JSON Structure processing is intentionally limited to OpenAPI 3.1 |
| Read JSON Structure in OpenAPI 3.0 | Not supported | Use OpenAPI 3.1 for JSON Structure documents |
| Write OpenAPI 3.1 JSON Structure JSON or YAML | Supported with restrictions | All schemas must be JSON Structure and share one dialect |
| Write Swagger 2.0 or OpenAPI 3.0 from JSON Structure | Deliberately not supported | No downgrade is provided because it would lose JSON Structure semantics |
| Lossless normal round trip of loaded JSON Structure schemas | Supported | Source JSON is restored into normal NSwag output |
| Lossless round trip of mixed ordinary JSON Schema and JSON Structure as OpenAPI 3.1 JSON Structure | Not supported | `OpenApi31JsonStructure` rejects mixed documents |
| Runtime TypeScript serialization | Not provided | The generator emits TypeScript types, not serializers |
| Lossless C# serialization of arbitrary multi-member unions | Not supported | C# falls back to `object` and emits a warning |
| Newtonsoft polymorphism for choices without object `const` discriminators | Not supported | The generated warning identifies this limitation |

## API entry points

Use `JsonStructureSettings` for parsing and imports, `OpenApiDocumentGeneratorSettings.SchemaDialect` and `JsonStructureDialect` for generation, and `OpenApiDocumentOutputType.OpenApi31JsonStructure` for output. `OpenApiDocument.FromJsonAsync` accepts `OpenApiDocumentLoadSettings.JsonStructureSettings` when an application needs to pass an explicit import policy or derived-dialect allowlist.

All other NSwag commands and settings retain their existing JSON Schema and document-output defaults when `SchemaDialect` and `OutputType` are not set.