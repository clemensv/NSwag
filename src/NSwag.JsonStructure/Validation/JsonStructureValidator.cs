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
            ValidateNamespace(document, document.Definitions, diagnostics);

            if (document.RootSchema != null)
            {
                ValidateSchema(document, document.RootSchema, diagnostics);
            }

            ValidateRootReference(document, diagnostics);

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
                ValidateAbstractReference(document, schema.Reference, schema.ResolvedReference, schema.Pointer + "/type/$ref", diagnostics);
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
                if (obj[JsonStructureKeywords.Ref] != null && !isTypeValue)
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
                    ValidateSourceReferences(array[i], pointer + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), false, diagnostics);
                }
            }
        }

        private static JsonStructureDiagnostic Error(string pointer, string message)
        {
            return new JsonStructureDiagnostic(JsonStructureDiagnosticSeverity.Error, pointer, message);
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
