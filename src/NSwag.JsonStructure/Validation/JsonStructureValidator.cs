//-----------------------------------------------------------------------
// <copyright file="JsonStructureValidator.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Resolution;

namespace NSwag.JsonStructure.Validation
{
    /// <summary>Validates parsed JSON Structure documents against the core, extended and validation meta-schema rules.</summary>
    public static class JsonStructureValidator
    {
        /// <summary>Validates a parsed JSON Structure document and returns every diagnostic found.</summary>
        /// <param name="document">The parsed document.</param>
        /// <returns>The collected diagnostics.</returns>
        public static IReadOnlyList<JsonStructureDiagnostic> Validate(JsonStructureDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var diagnostics = new List<JsonStructureDiagnostic>();

            ValidateDocumentRoot(document, diagnostics);
            ValidateSourceDocument(document, diagnostics);
            ValidateNamespace(document, document.Definitions, diagnostics);

            if (document.RootSchema != null)
            {
                ValidateSchema(document, document.RootSchema, diagnostics);
            }

            ValidateRootReference(document, diagnostics);
            ValidateInheritance(document, diagnostics);

            return diagnostics;
        }

        /// <summary>Validates a document and throws when the first error is found.</summary>
        /// <param name="document">The parsed document.</param>
        /// <exception cref="JsonStructureException">The first validation error.</exception>
        public static void ThrowIfInvalid(JsonStructureDocument document)
        {
            foreach (var diagnostic in Validate(document))
            {
                if (diagnostic.Severity == JsonStructureDiagnosticSeverity.Error)
                {
                    throw new JsonStructureException(diagnostic.Message, diagnostic.Pointer);
                }
            }
        }

        private static void ValidateDocumentRoot(JsonStructureDocument document, List<JsonStructureDiagnostic> diagnostics)
        {
            // SchemaDocument.required in meta/core/v0/index.json currently lists only "$schema".
            // The prose core specification's Document Structure and $id sections also require
            // "$id" and "name". Strictness wins for this known prose-vs-meta-schema mismatch.
            RequireRootString(document.SchemaUri, JsonStructureKeywords.Schema, diagnostics);
            RequireRootString(document.Id, JsonStructureKeywords.Id, diagnostics);
            RequireRootString(document.Name, JsonStructureKeywords.Name, diagnostics);

            if (!IsAbsoluteUri(document.SchemaUri))
            {
                diagnostics.Add(Error("#/$schema", "The '$schema' value must be an absolute URI."));
            }

            if (!IsAbsoluteUri(document.Id))
            {
                diagnostics.Add(Error("#/$id", "The '$id' value must be an absolute URI."));
            }

            if (document.SchemaUri != null &&
                string.Equals(document.SchemaUri, JsonStructureDialects.CoreUri, StringComparison.Ordinal) == false &&
                document.Dialect == JsonStructureDialect.Core)
            {
                diagnostics.Add(Error("#/$schema", "The '$schema' value does not identify the Core meta-schema."));
            }

            // SchemaDocument permits "$root", "definitions" and root "type"; core prose makes
            // "$root" and root "type" mutually exclusive.
            if (document.RootPointer != null && document.RootSchema != null)
            {
                diagnostics.Add(Error("#", "The '$root' and 'type' keywords are mutually exclusive at the document root."));
            }

            if (document.RootSchema != null && !IsIdentifier(document.RootSchema.Name))
            {
                diagnostics.Add(Error("#/name", "The root type name '" + document.RootSchema.Name + "' must match [A-Za-z_][A-Za-z0-9_]*."));
            }
        }

            private static void ValidateSourceDocument(JsonStructureDocument document, List<JsonStructureDiagnostic> diagnostics)
            {
                if (document.SourceJson is not JObject root)
                {
                    return;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in root.Properties())
                {
                    if (!seen.Add(property.Name))
                    {
                        diagnostics.Add(Error("#/" + EscapePointerSegment(property.Name), "The keyword '" + property.Name + "' must not occur more than once."));
                    }
                }

                foreach (var property in root.Properties())
                {
                    if (property.Name is JsonStructureKeywords.Schema or JsonStructureKeywords.Id or JsonStructureKeywords.Root or
                        JsonStructureKeywords.Definitions or JsonStructureKeywords.Offers or JsonStructureKeywords.Uses or JsonStructureKeywords.Name or
                        JsonStructureKeywords.Type)
                    {
                        continue;
                    }

                    if (IsReservedKeyword(property.Name) &&
                        !JsonStructureKeywords.IsStructural(property.Name) &&
                        !JsonStructureKeywords.IsCoreAnnotation(property.Name) &&
                        !JsonStructureKeywords.TryGetAddIn(property.Name, out _))
                    {
                        diagnostics.Add(Error("#/" + EscapePointerSegment(property.Name), "The reserved keyword '" + property.Name + "' is not valid as a custom keyword."));
                    }
                }

                if (root[JsonStructureKeywords.Name] is JValue name && (name.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)name)))
                {
                    diagnostics.Add(Error("#/name", "The root 'name' must be a non-empty string."));
                }

                if (root[JsonStructureKeywords.Definitions] is JObject definitions)
                {
                    ValidateDefinitionNamespace(definitions, "#/definitions", diagnostics);
                }

                if (root[JsonStructureKeywords.Type] is not null)
                {
                    ValidateSourceSchema(root, "#", diagnostics, true);
                }

                ValidateSourceReferences(root, diagnostics);
            }

            private static void ValidateDefinitionNamespace(JObject node, string pointer, List<JsonStructureDiagnostic> diagnostics)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in node.Properties())
                {
                    var childPointer = pointer + "/" + EscapePointerSegment(property.Name);
                    if (!names.Add(property.Name))
                    {
                        diagnostics.Add(Error(childPointer, "A definition or namespace name must be unique within its namespace."));
                    }

                    if (property.Value is not JObject child)
                    {
                        diagnostics.Add(Error(childPointer, "A definition or namespace must be a JSON object."));
                        continue;
                    }

                    if (child[JsonStructureKeywords.Type] != null)
                    {
                        ValidateSourceSchema(child, childPointer, diagnostics, false);
                    }
                    else
                    {
                        ValidateDefinitionNamespace(child, childPointer, diagnostics);
                    }
                }
            }

            private static void ValidateSourceSchema(JObject node, string pointer, List<JsonStructureDiagnostic> diagnostics, bool documentRoot)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in node.Properties())
                {
                    if (!seen.Add(property.Name))
                    {
                        diagnostics.Add(Error(pointer + "/" + EscapePointerSegment(property.Name), "The keyword '" + property.Name + "' must not occur more than once."));
                    }
                    else if (IsReservedKeyword(property.Name) &&
                        !JsonStructureKeywords.IsStructural(property.Name) &&
                        !JsonStructureKeywords.IsCoreAnnotation(property.Name) &&
                        !JsonStructureKeywords.TryGetAddIn(property.Name, out _))
                    {
                        diagnostics.Add(Error(pointer + "/" + EscapePointerSegment(property.Name),
                            "The reserved keyword '" + property.Name + "' is not valid as a custom keyword."));
                    }
                }

                var type = node[JsonStructureKeywords.Type];
                if (type == null && node[JsonStructureKeywords.Ref] == null)
                {
                    diagnostics.Add(Error(pointer + "/type", "Every schema element must declare 'type'."));
                    return;
                }

                if (type == null)
                {
                    return;
                }

                var typeName = type.Type == JTokenType.String ? (string)type : null;
                var kind = JsonStructureTypeKind.None;
                var isPrimitive = typeName != null && JsonStructureTypeKinds.TryParse(typeName, out kind) &&
                    IsPrimitive(kind);
                var isObject = string.Equals(typeName, "object", StringComparison.Ordinal);
                var isTuple = string.Equals(typeName, "tuple", StringComparison.Ordinal);
                var isArray = string.Equals(typeName, "array", StringComparison.Ordinal);
                var isSet = string.Equals(typeName, "set", StringComparison.Ordinal);
                var isMap = string.Equals(typeName, "map", StringComparison.Ordinal);
                var isChoice = string.Equals(typeName, "choice", StringComparison.Ordinal);
                var isUnion = type is JArray;
                var isReference = type is JObject;

                if (node[JsonStructureKeywords.Name] is JToken nameToken &&
                    (nameToken.Type != JTokenType.String || !IsIdentifier((string)nameToken)))
                {
                    diagnostics.Add(Error(pointer + "/name", "A type name must match [A-Za-z_][A-Za-z0-9_]*."));
                }

                if (isUnion && node[JsonStructureKeywords.Enum] != null)
                {
                    diagnostics.Add(Error(pointer + "/enum", "The 'enum' keyword is not permitted with a type union."));
                }

                ValidatePrimitiveConstraints(node, pointer, diagnostics, isPrimitive, isUnion, kind);
                ValidateCompoundKeyword(node, pointer, diagnostics, isObject, isTuple, isArray, isSet, isMap, isChoice);

                if ((isObject || isTuple) && !documentRoot && node[JsonStructureKeywords.Properties] is not JObject)
                {
                    diagnostics.Add(Error(pointer + "/properties", "An object or tuple type must declare a 'properties' object."));
                }
                if ((isArray || isSet) && node[JsonStructureKeywords.Items] is not JObject)
                {
                    diagnostics.Add(Error(pointer + "/items", "An array or set type must declare 'items'."));
                }
                if (isMap && node[JsonStructureKeywords.Values] is not JObject)
                {
                    diagnostics.Add(Error(pointer + "/values", "A map type must declare 'values'."));
                }
                if (isTuple && node[JsonStructureKeywords.Tuple] is not JArray)
                {
                    diagnostics.Add(Error(pointer + "/tuple", "A tuple type must declare its element order with 'tuple'."));
                }
                if (isChoice && node[JsonStructureKeywords.Choices] is not JObject)
                {
                    diagnostics.Add(Error(pointer + "/choices", "A choice type must declare a 'choices' object."));
                }

                if ((isObject || isTuple) && node[JsonStructureKeywords.Properties] is JObject properties)
                {
                    if (properties.Properties().Any() == false)
                    {
                        diagnostics.Add(Error(pointer + "/properties", "An object or tuple type must declare at least one property."));
                    }

                    var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in properties.Properties())
                    {
                        var propertyPointer = pointer + "/properties/" + EscapePointerSegment(property.Name);
                        if (!propertyNames.Add(property.Name))
                        {
                            diagnostics.Add(Error(propertyPointer, "Property names must be unique."));
                        }
                        if (!IsIdentifier(property.Name))
                        {
                            diagnostics.Add(Error(propertyPointer, "The property name '" + property.Name + "' is not a permitted identifier or is reserved."));
                        }
                        if (property.Value is JObject child)
                        {
                            ValidateSourceSchema(child, propertyPointer, diagnostics, false);
                        }
                        else
                        {
                            diagnostics.Add(Error(propertyPointer, "A property value must be a schema object."));
                        }
                    }
                }

                if (node[JsonStructureKeywords.Items] is JObject items)
                {
                    ValidateSourceSchema(items, pointer + "/items", diagnostics, false);
                }
                if (node[JsonStructureKeywords.Values] is JObject values)
                {
                    ValidateSourceSchema(values, pointer + "/values", diagnostics, false);
                }
                if (node[JsonStructureKeywords.AdditionalProperties] is JObject additional)
                {
                    ValidateSourceSchema(additional, pointer + "/additionalProperties", diagnostics, false);
                }
                if (node[JsonStructureKeywords.Choices] is JObject choices)
                {
                    var choiceNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var choice in choices.Properties())
                    {
                        var choicePointer = pointer + "/choices/" + EscapePointerSegment(choice.Name);
                        if (!choiceNames.Add(choice.Name))
                        {
                            diagnostics.Add(Error(choicePointer, "Choice names must be unique."));
                        }
                        if (!IsIdentifier(choice.Name))
                        {
                            diagnostics.Add(Error(choicePointer, "The choice name '" + choice.Name + "' is not a permitted identifier or is reserved."));
                        }
                        if (choice.Value is JObject child)
                        {
                            ValidateSourceSchema(child, choicePointer, diagnostics, false);
                        }
                        else
                        {
                            diagnostics.Add(Error(choicePointer, "A choice value must be a schema object."));
                        }
                    }
                }

                if (node[JsonStructureKeywords.Type] is JArray union)
                {
                    for (var i = 0; i < union.Count; i++)
                    {
                        if (union[i] is not JValue { Type: JTokenType.String } &&
                            union[i] is not JObject)
                        {
                            diagnostics.Add(Error(pointer + "/type/" + i, "A type union member must be a primitive type name or a '$ref' object."));
                        }
                        if (union[i] is JObject inline && inline[JsonStructureKeywords.Ref] == null)
                        {
                            if (string.Equals((string)inline[JsonStructureKeywords.Type], "object", StringComparison.Ordinal))
                            {
                                diagnostics.Add(Error(pointer + "/type/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    "An object type must not be declared inline inside a non-discriminated type union."));
                            }
                            ValidateSourceSchema(inline, pointer + "/type/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), diagnostics, false);
                        }
                    }
                }
            }

            private static void ValidateCompoundKeyword(
                JObject node, string pointer, List<JsonStructureDiagnostic> diagnostics,
                bool isObject, bool isTuple, bool isArray, bool isSet, bool isMap, bool isChoice)
            {
                CheckOnly(node, pointer, JsonStructureKeywords.Properties, isObject || isTuple, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.Required, isObject, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.Items, isArray || isSet, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.Values, isMap, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.Tuple, isTuple, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.Choices, isChoice, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.Selector, isChoice, diagnostics);
                CheckOnly(node, pointer, JsonStructureKeywords.AdditionalProperties, isObject, diagnostics);

                if (node[JsonStructureKeywords.Type] is JValue && node[JsonStructureKeywords.Extends] != null &&
                    !(isObject || isTuple || isChoice))
                {
                    diagnostics.Add(Error(pointer + "/$extends", "The '$extends' keyword is only permitted on object, tuple, or inline choice types."));
                }

                if (node[JsonStructureKeywords.Abstract] != null)
                {
                    if (node[JsonStructureKeywords.Abstract].Type != JTokenType.Boolean)
                    {
                        diagnostics.Add(Error(pointer + "/abstract", "The 'abstract' keyword must be a boolean."));
                    }
                    if (!isObject && !isTuple)
                    {
                        diagnostics.Add(Error(pointer + "/abstract", "The 'abstract' keyword may only be used on object and tuple types."));
                    }
                    if (isObject && node[JsonStructureKeywords.Abstract].Value<bool>() &&
                        node[JsonStructureKeywords.AdditionalProperties] != null)
                    {
                        diagnostics.Add(Error(pointer + "/additionalProperties", "Abstract types must not declare 'additionalProperties'."));
                    }
                }

                if (node[JsonStructureKeywords.Extends] is JToken extends)
                {
                    if (extends.Type != JTokenType.String && extends.Type != JTokenType.Array)
                    {
                        diagnostics.Add(Error(pointer + "/$extends", "The '$extends' value must be a JSON Pointer string or an array of JSON Pointer strings."));
                    }
                    else if (extends is JArray array)
                    {
                        for (var i = 0; i < array.Count; i++)
                        {
                            if (array[i].Type != JTokenType.String || !IsLocalPointer((string)array[i]))
                            {
                                diagnostics.Add(Error(pointer + "/$extends/" + i, "Each '$extends' entry must be a local JSON Pointer."));
                            }
                        }
                    }
                    else if (!IsLocalPointer((string)extends))
                    {
                        diagnostics.Add(Error(pointer + "/$extends", "The '$extends' value must be a local JSON Pointer."));
                    }
                }

                if (isChoice)
                {
                    var hasExtends = node[JsonStructureKeywords.Extends] != null;
                    var hasSelector = node[JsonStructureKeywords.Selector] != null;
                    if (hasExtends != hasSelector)
                    {
                        diagnostics.Add(Error(pointer + "/selector", "Inline choices must declare both '$extends' and 'selector'; tagged choices must declare neither."));
                    }
                    if (node[JsonStructureKeywords.Selector] is JValue selector &&
                        (selector.Type != JTokenType.String || string.IsNullOrEmpty((string)selector)))
                    {
                        diagnostics.Add(Error(pointer + "/selector", "The 'selector' value must be a non-empty string."));
                    }
                }
            }

            private static void CheckOnly(JObject node, string pointer, string keyword, bool allowed, List<JsonStructureDiagnostic> diagnostics)
            {
                if (!allowed && node[keyword] != null)
                {
                    diagnostics.Add(Error(pointer + "/" + EscapePointerSegment(keyword), "The '" + keyword + "' keyword is not legal for this type."));
                }
            }

            private static void ValidatePrimitiveConstraints(
                JObject node, string pointer, List<JsonStructureDiagnostic> diagnostics, bool primitive, bool union,
                JsonStructureTypeKind kind)
            {
                if (node[JsonStructureKeywords.Const] != null && (!primitive || union))
                {
                    diagnostics.Add(Error(pointer + "/const", "The 'const' keyword is only permitted on primitive types."));
                }
                else if (node[JsonStructureKeywords.Const] is JToken constant && !IsValueCompatible(constant, kind))
                {
                    diagnostics.Add(Error(pointer + "/const", "The 'const' value does not match the declared primitive type."));
                }
                if (node[JsonStructureKeywords.Enum] is JToken enumeration)
                {
                    if (!primitive || union || enumeration is not JArray)
                    {
                        diagnostics.Add(Error(pointer + "/enum", "The 'enum' keyword is only permitted on primitive types and must be an array."));
                    }
                    else
                    {
                        var values = (JArray)enumeration;
                        for (var i = 0; i < values.Count; i++)
                        {
                            if (values[i] is not JValue)
                            {
                                diagnostics.Add(Error(pointer + "/enum/" + i, "Enum values must be JSON primitive values."));
                            }
                            if (values.Take(i).Any(v => JToken.DeepEquals(v, values[i])))
                            {
                                diagnostics.Add(Error(pointer + "/enum/" + i, "Enum values must be unique."));
                            }
                            else if (!IsValueCompatible(values[i], kind))
                            {
                                diagnostics.Add(Error(pointer + "/enum/" + i, "The enum value does not match the declared primitive type."));
                            }
                        }
                        if (node[JsonStructureKeywords.Const] != null && !values.Any(v => JToken.DeepEquals(v, node[JsonStructureKeywords.Const])))
                        {
                            diagnostics.Add(Error(pointer + "/const", "The 'const' value must be one of the enum values."));
                        }
                    }
                }
            }

            private static bool IsAbsoluteUri(string value)
            {
                return Uri.TryCreate(value, UriKind.Absolute, out _);
            }

            private static bool IsPrimitive(JsonStructureTypeKind kind)
            {
                return !JsonStructureTypeKinds.IsCompound(kind) && kind != JsonStructureTypeKind.Any &&
                    kind != JsonStructureTypeKind.None;
            }

            private static bool IsValueCompatible(JToken value, JsonStructureTypeKind kind)
            {
                if (value is not JValue primitive)
                {
                    return false;
                }

                return kind switch
                {
                    JsonStructureTypeKind.Null => primitive.Type == JTokenType.Null,
                    JsonStructureTypeKind.Boolean => primitive.Type == JTokenType.Boolean,
                    JsonStructureTypeKind.String or JsonStructureTypeKind.Binary or JsonStructureTypeKind.Date
                        or JsonStructureTypeKind.DateTime or JsonStructureTypeKind.Time or JsonStructureTypeKind.Duration
                        or JsonStructureTypeKind.Uuid or JsonStructureTypeKind.Uri or JsonStructureTypeKind.JsonPointer
                        => primitive.Type == JTokenType.String,
                    JsonStructureTypeKind.Number or JsonStructureTypeKind.Float8 or JsonStructureTypeKind.Float
                        or JsonStructureTypeKind.Double or JsonStructureTypeKind.Decimal
                        => primitive.Type == JTokenType.Integer || primitive.Type == JTokenType.Float,
                    JsonStructureTypeKind.Integer or JsonStructureTypeKind.Int8 or JsonStructureTypeKind.UInt8
                        or JsonStructureTypeKind.Int16 or JsonStructureTypeKind.UInt16 or JsonStructureTypeKind.Int32
                        or JsonStructureTypeKind.UInt32 or JsonStructureTypeKind.Int64 or JsonStructureTypeKind.UInt64
                        or JsonStructureTypeKind.Int128 or JsonStructureTypeKind.UInt128
                        => primitive.Type == JTokenType.Integer,
                    _ => false
                };
            }

            private static bool IsLocalPointer(string value)
            {
                return !string.IsNullOrEmpty(value) && value.StartsWith("#/", StringComparison.Ordinal);
            }

            private static bool IsReservedKeyword(string value)
            {
                return value is "definitions" or "$extends" or "$id" or "$ref" or "$root" or "$schema" or "$uses" or "$offers" or
                    "abstract" or "additionalProperties" or "choices" or "const" or "default" or "description" or "enum" or
                    "examples" or "format" or "items" or "maxLength" or "name" or "precision" or "properties" or "required" or
                    "scale" or "selector" or "type" or "values";
            }

        private static void RequireRootString(string value, string keyword, List<JsonStructureDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(value))
            {
                diagnostics.Add(Error("#/" + EscapePointerSegment(keyword), "The root object must declare '" + keyword + "'."));
            }
        }

        private static void ValidateNamespace(
            JsonStructureDocument document,
            JsonStructureNamespace ns,
            List<JsonStructureDiagnostic> diagnostics)
        {
            foreach (var type in ns.Types)
            {
                // definitions values are Namespace.values -> CompoundType in meta/core/v0/index.json;
                // names come from the map key when no explicit name keyword is present.
                if (!IsIdentifier(type.Name))
                {
                    diagnostics.Add(Error(type.Pointer, "The type name '" + type.Name + "' must match [A-Za-z_][A-Za-z0-9_]*."));
                }

                if (type.Schema.Name != null && !IsIdentifier(type.Schema.Name))
                {
                    diagnostics.Add(Error(type.Pointer + "/name", "The type name '" + type.Schema.Name + "' must match [A-Za-z_][A-Za-z0-9_]*."));
                }

                ValidateSchema(document, type.Schema, diagnostics);
            }

            foreach (var child in ns.Namespaces)
            {
                ValidateNamespace(document, child, diagnostics);
            }
        }

        private static void ValidateSchema(
            JsonStructureDocument document,
            JsonStructureSchema schema,
            List<JsonStructureDiagnostic> diagnostics)
        {
            if (schema == null)
            {
                return;
            }

            ValidateCompoundRequirements(schema, diagnostics);
            ValidateChoice(schema, diagnostics);
            ValidateRequiredProperties(schema, diagnostics);
            ValidateAbstractDeclaration(schema, diagnostics);
            ValidateAbstractReference(document, schema, diagnostics);

            foreach (var property in schema.Properties)
            {
                // Property.values in meta/core/v0/index.json are keyed by property names; core
                // Identifier Rules require every property name to match this identifier pattern.
                if (!IsIdentifier(property.Name))
                {
                    diagnostics.Add(Error(schema.Pointer + "/properties/" + EscapePointerSegment(property.Name),
                        "The property name '" + property.Name + "' must match [A-Za-z_][A-Za-z0-9_]*."));
                }

                ValidateSchema(document, property.Schema, diagnostics);
            }

            for (var i = 0; i < schema.Choices.Count; i++)
            {
                var choice = schema.Choices[i];
                if (!IsIdentifier(choice.Name))
                {
                    diagnostics.Add(Error(schema.Pointer + "/choices/" + EscapePointerSegment(choice.Name),
                        "The choice name '" + choice.Name + "' must match [A-Za-z_][A-Za-z0-9_]*."));
                }

                ValidateSchema(document, choice.Schema, diagnostics);
            }

            for (var i = 0; i < schema.Union.Count; i++)
            {
                var member = schema.Union[i];
                if (member.InlineSchema != null)
                {
                    ValidateSchema(document, member.InlineSchema, diagnostics);
                }

                if (member.IsReference)
                {
                    ValidateAbstractReference(document, member, schema.Pointer + "/type/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/$ref", diagnostics);
                }
            }

            ValidateSchema(document, schema.Items, diagnostics);
            ValidateSchema(document, schema.Values, diagnostics);
            ValidateSchema(document, schema.AdditionalProperties, diagnostics);
        }

        private static void ValidateCompoundRequirements(JsonStructureSchema schema, List<JsonStructureDiagnostic> diagnostics)
        {
            // ArrayType.required and SetType.required in meta/core/v0/index.json require "type" and "items".
            if ((schema.Kind == JsonStructureTypeKind.Array || schema.Kind == JsonStructureTypeKind.Set) && schema.Items == null)
            {
                diagnostics.Add(Error(schema.Pointer + "/items", "An '" + JsonStructureTypeKinds.GetName(schema.Kind) + "' type must declare 'items'."));
            }

            // MapType.required in meta/core/v0/index.json requires "type" and "values".
            if (schema.Kind == JsonStructureTypeKind.Map && schema.Values == null)
            {
                diagnostics.Add(Error(schema.Pointer + "/values", "A 'map' type must declare 'values'."));
            }

            // TupleType.required in meta/core/v0/index.json requires "type", "properties" and "tuple".
            if (schema.Kind == JsonStructureTypeKind.Tuple)
            {
                if (schema.Properties.Count == 0)
                {
                    diagnostics.Add(Error(schema.Pointer + "/properties", "A 'tuple' type must declare 'properties'."));
                }

                if (schema.TupleOrder.Count == 0)
                {
                    diagnostics.Add(Error(schema.Pointer + "/tuple", "A 'tuple' type must declare its element order with the 'tuple' keyword."));
                }
            }

            // ChoiceType.required in meta/core/v0/index.json requires "type" and "choices".
            if (schema.Kind == JsonStructureTypeKind.Choice && schema.Choices.Count == 0)
            {
                diagnostics.Add(Error(schema.Pointer + "/choices", "A 'choice' type must declare its variants with the 'choices' keyword."));
            }
        }

        private static void ValidateChoice(JsonStructureSchema schema, List<JsonStructureDiagnostic> diagnostics)
        {
            if (schema.Kind != JsonStructureTypeKind.Choice)
            {
                return;
            }

            // ChoiceType in meta/core/v0/index.json allows "$extends" and "selector"; the core
            // inline-union prose requires them to appear together. Without both, the choice is tagged.
            if (schema.Extends != null && schema.Selector == null)
            {
                diagnostics.Add(Error(schema.Pointer + "/selector", "An inline union declared with '$extends' must declare a 'selector'."));
            }

            if (schema.Extends == null && schema.Selector != null)
            {
                diagnostics.Add(Error(schema.Pointer + "/selector", "A tagged union must not declare a 'selector'; declare '$extends' as well for an inline union."));
            }

            if (schema.Selector != null && !IsIdentifier(schema.Selector))
            {
                diagnostics.Add(Error(schema.Pointer + "/selector", "The choice selector must be an identifier."));
            }
        }

        private static void ValidateRequiredProperties(JsonStructureSchema schema, List<JsonStructureDiagnostic> diagnostics)
        {
            // ObjectType.required in meta/core/v0/index.json defines required as property-name arrays.
            foreach (var set in schema.RequiredSets)
            {
                foreach (var name in set)
                {
                    if (!schema.TryGetProperty(name, out _))
                    {
                        diagnostics.Add(Error(schema.Pointer + "/required", "The required property '" + name + "' is not declared in 'properties'."));
                    }
                }
            }
        }

        private static void ValidateAbstractDeclaration(JsonStructureSchema schema, List<JsonStructureDiagnostic> diagnostics)
        {
            if (!schema.IsAbstract)
            {
                return;
            }

            // The core abstract keyword prose restricts abstract declarations to object and tuple
            // schemas, and forbids additionalProperties because abstract types imply it.
            if (schema.Kind != JsonStructureTypeKind.Object && schema.Kind != JsonStructureTypeKind.Tuple)
            {
                diagnostics.Add(Error(schema.Pointer + "/abstract", "The 'abstract' keyword may only be used on object and tuple types."));
            }

            if (schema.AdditionalPropertiesAllowed != null || schema.AdditionalProperties != null)
            {
                diagnostics.Add(Error(schema.Pointer + "/additionalProperties", "Abstract types must not declare 'additionalProperties'."));
            }
        }

        private static void ValidateAbstractReference(
            JsonStructureDocument document,
            JsonStructureSchema schema,
            List<JsonStructureDiagnostic> diagnostics)
        {
            if (schema.Reference != null)
            {
                var pointer = schema.Pointer + "/type/$ref";
                ValidateAbstractReference(document, schema.Reference, schema.ResolvedReference, pointer, diagnostics);
            }
        }

        private static void ValidateAbstractReference(
            JsonStructureDocument document,
            JsonStructureTypeReference reference,
            string pointer,
            List<JsonStructureDiagnostic> diagnostics)
        {
            ValidateAbstractReference(document, reference.Reference, reference.ResolvedReference, pointer, diagnostics);
        }

        private static void ValidateAbstractReference(
            JsonStructureDocument document,
            string reference,
            JsonStructureNamedType resolvedReference,
            string pointer,
            List<JsonStructureDiagnostic> diagnostics)
        {
            var resolved = resolvedReference ?? JsonStructureResolver.ResolvePointer(document, reference);
            if (resolved != null && resolved.Schema.IsAbstract)
            {
                // The core abstract keyword prose says abstract types cannot be instantiated directly
                // and MUST NOT be referenced via $ref. $extends is validated by the resolver instead.
                diagnostics.Add(Error(pointer, "The '$ref' target '" + reference + "' is abstract and cannot be instantiated directly."));
            }
        }

        private static void ValidateInheritance(JsonStructureDocument document, List<JsonStructureDiagnostic> diagnostics)
        {
            foreach (var type in document.GetAllTypes())
            {
                foreach (var baseType in type.Schema.ResolvedExtendsTypes)
                {
                    if (!baseType.Schema.IsAbstract)
                    {
                        diagnostics.Add(Error(type.Schema.Pointer + "/$extends",
                            "Every '$extends' target must be abstract; '" + baseType.FullName + "' is concrete."));
                    }

                    foreach (var inherited in GetAllInheritedProperties(baseType))
                    {
                        if (type.Schema.TryGetProperty(inherited.Name, out _))
                        {
                            diagnostics.Add(Error(type.Schema.Pointer + "/properties/" + EscapePointerSegment(inherited.Name),
                                "The property '" + inherited.Name + "' collides with an inherited property."));
                        }
                    }
                }
            }
        }

        private static IEnumerable<JsonStructureProperty> GetAllInheritedProperties(JsonStructureNamedType type)
        {
            foreach (var baseType in type.Schema.ResolvedExtendsTypes)
            {
                foreach (var property in baseType.Schema.Properties)
                {
                    yield return property;
                }

                foreach (var property in GetAllInheritedProperties(baseType))
                {
                    yield return property;
                }
            }

            foreach (var property in type.Schema.Properties)
            {
                yield return property;
            }
        }

        private static void ValidateRootReference(JsonStructureDocument document, List<JsonStructureDiagnostic> diagnostics)
        {
            if (document.RootPointer == null)
            {
                if (document.RootSchema != null && document.RootSchema.IsAbstract)
                {
                    diagnostics.Add(Error("#/abstract", "The root type is abstract and cannot be instantiated directly."));
                }

                return;
            }

            var rootType = JsonStructureResolver.ResolvePointer(document, document.RootPointer);
            if (rootType != null && rootType.Schema.IsAbstract)
            {
                diagnostics.Add(Error("#/$root", "The '$root' target '" + document.RootPointer + "' is abstract and cannot be instantiated directly."));
            }
        }

        private static void ValidateSourceReferences(JToken source, List<JsonStructureDiagnostic> diagnostics)
        {
            if (source == null)
            {
                return;
            }

            ValidateSourceReferences(source, "#", false, diagnostics);
        }

        private static void ValidateSourceReferences(
            JToken token,
            string pointer,
            bool isTypeValue,
            List<JsonStructureDiagnostic> diagnostics)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                if (obj[JsonStructureKeywords.Ref] != null && !isTypeValue && !IsSchemaReferencePointer(pointer))
                {
                    diagnostics.Add(Error(pointer + "/$ref", "The '$ref' keyword is only legal inside a 'type' value."));
                }

                if (isTypeValue && obj[JsonStructureKeywords.Ref] != null && obj.Properties().Count() != 1)
                {
                    diagnostics.Add(Error(pointer, "An object 'type' value containing '$ref' must contain no other properties."));
                }

                foreach (var property in obj.Properties())
                {
                    ValidateSourceReferences(
                        property.Value,
                        pointer + "/" + EscapePointerSegment(property.Name),
                        string.Equals(property.Name, JsonStructureKeywords.Type, StringComparison.Ordinal),
                        diagnostics);
                }

                return;
            }

            var array = token as JArray;
            if (array != null)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    ValidateSourceReferences(array[i], pointer + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), isTypeValue, diagnostics);
                }
            }
        }

        private static JsonStructureDiagnostic Error(string pointer, string message)
        {
            return new JsonStructureDiagnostic(JsonStructureDiagnosticSeverity.Error, pointer, message);
        }

        private static bool IsSchemaReferencePointer(string pointer)
        {
            return pointer.Contains("/properties/", StringComparison.Ordinal) ||
                pointer.Contains("/choices/", StringComparison.Ordinal) ||
                pointer.EndsWith("/items", StringComparison.Ordinal) ||
                pointer.EndsWith("/values", StringComparison.Ordinal) ||
                pointer.EndsWith("/additionalProperties", StringComparison.Ordinal);
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (!IsIdentifierStart(value[0]))
            {
                return false;
            }

            for (var i = 1; i < value.Length; i++)
            {
                if (!IsIdentifierPart(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdentifierStart(char value)
        {
            return value == '_' || (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsIdentifierPart(char value)
        {
            return IsIdentifierStart(value) || (value >= '0' && value <= '9');
        }

        private static string EscapePointerSegment(string segment)
        {
            return segment.Replace("~", "~0").Replace("/", "~1");
        }
    }
}
