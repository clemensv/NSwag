//-----------------------------------------------------------------------
// <copyright file="JsonStructureParser.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.Model;

namespace NSwag.JsonStructure.Parsing
{
    /// <summary>Reads a JSON Structure schema document into a <see cref="JsonStructureDocument"/>.</summary>
    public class JsonStructureParser
    {
        private readonly JsonStructureDialects _dialects;

        /// <summary>Initializes a new instance of the <see cref="JsonStructureParser"/> class.</summary>
        public JsonStructureParser()
            : this(JsonStructureDialects.Default)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureParser"/> class.</summary>
        /// <param name="dialects">The dialect recognizer.</param>
        public JsonStructureParser(JsonStructureDialects dialects)
        {
            _dialects = dialects ?? throw new ArgumentNullException(nameof(dialects));
        }

        /// <summary>Gets or sets the dialect assumed when a document carries no <c>$schema</c>.</summary>
        /// <remarks>
        /// Schema Objects embedded in an OpenAPI document inherit the dialect from the document's
        /// <c>jsonSchemaDialect</c>, so the caller supplies it here.
        /// </remarks>
        public JsonStructureDialect DefaultDialect { get; set; } = JsonStructureDialect.None;

        /// <summary>Parses a JSON Structure document from JSON text.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The parsed document.</returns>
        public JsonStructureDocument Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentException("The JSON text must not be empty.", nameof(json));
            }

            return Parse(ParseJson(json));
        }

        private static JToken ParseJson(string json)
        {
            try
            {
                return JToken.Parse(json);
            }
            catch (Newtonsoft.Json.JsonException exception)
            {
                throw new JsonStructureException(
                    "The document is not well-formed JSON: " + exception.Message, "#", exception);
            }
        }

        /// <summary>Parses a JSON Structure document from a JSON token.</summary>
        /// <param name="token">The JSON token.</param>
        /// <returns>The parsed document.</returns>
        public JsonStructureDocument Parse(JToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token is not JObject root)
            {
                throw new JsonStructureException("The root of a JSON Structure document must be a JSON object.", "#");
            }

            var document = new JsonStructureDocument
            {
                SourceJson = root.DeepClone(),
                Id = (string)root[JsonStructureKeywords.Id],
                Name = (string)root[JsonStructureKeywords.Name],
                SchemaUri = (string)root[JsonStructureKeywords.Schema]
            };

            document.Dialect = ResolveDialect(document.SchemaUri);
            document.AddIns = JsonStructureDialects.GetDefaultAddIns(document.Dialect);
            ReadAddIns(root, document);

            var rootPointer = (string)root[JsonStructureKeywords.Root];
            var hasType = root[JsonStructureKeywords.Type] != null;

            if (rootPointer != null && hasType)
            {
                throw new JsonStructureException(
                    "The '$root' and 'type' keywords are mutually exclusive at the document root.", "#");
            }

            document.RootPointer = rootPointer;
            if (rootPointer != null)
            {
                ValidateLocalPointer(rootPointer, "#/$root");
            }

            if (root[JsonStructureKeywords.Definitions] is JObject definitions)
            {
                ReadNamespace(definitions, document.Definitions, document, "#/definitions");
            }

            if (hasType)
            {
                var rootSchema = ReadSchema(root, document, "#", isDocumentRoot: true);
                rootSchema.Name ??= document.Name;
                document.RootSchema = rootSchema;

                // A root type declaration is placed into the root namespace, as if it had been
                // declared under 'definitions'.
                if (rootSchema.Name != null && rootSchema.IsNamedType &&
                    !document.Definitions.TryGetType(rootSchema.Name, out _))
                {
                    document.Definitions.AddType(
                        new JsonStructureNamedType(rootSchema.Name, rootSchema, document.Definitions));
                }
            }

            return document;
        }

        private JsonStructureDialect ResolveDialect(string schemaUri)
        {
            if (schemaUri == null)
            {
                return DefaultDialect;
            }

            var dialect = _dialects.Resolve(schemaUri);
            if (dialect != JsonStructureDialect.None)
            {
                return dialect;
            }

            if (_dialects.IsUnrecognizedJsonStructureUri(schemaUri))
            {
                throw new JsonStructureException(
                    "The '$schema' meta-schema URI '" + schemaUri + "' is not a recognized JSON Structure dialect. " +
                    "Recognized dialects are: " + string.Join(", ", JsonStructureDialects.CanonicalUris) + ". " +
                    "Meta-schema URIs are compared byte-for-byte, so trailing slashes and case differences matter. " +
                    "A schema bearing an unknown dialect must not be processed as JSON Schema.", "#");
            }

            throw new JsonStructureException(
                "The '$schema' value '" + schemaUri + "' does not identify a JSON Structure dialect.", "#");
        }

        private static void ReadAddIns(JObject root, JsonStructureDocument document)
        {
            var offered = JsonStructureDialects.GetOfferedAddIns(document.Dialect);

            if (root[JsonStructureKeywords.Offers] is JToken offersToken)
            {
                if (offersToken is not JObject offers)
                {
                    throw new JsonStructureException("The '$offers' keyword must be a JSON object.", "#/$offers");
                }

                foreach (var property in offers.Properties())
                {
                    offered |= JsonStructureDialects.ResolveAddIn(property.Name);
                }
            }

            document.OfferedAddIns = offered;

            if (root[JsonStructureKeywords.Uses] == null)
            {
                return;
            }

            if (root[JsonStructureKeywords.Uses] is not JArray uses)
            {
                throw new JsonStructureException("The '$uses' keyword must be an array of add-in names.", "#/$uses");
            }

            var seen = JsonStructureAddIns.None;
            for (var i = 0; i < uses.Count; i++)
            {
                var entry = uses[i];
                var entryPointer = "#/$uses/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (entry is not JValue { Type: JTokenType.String })
                {
                    throw new JsonStructureException(
                        "Each '$uses' entry must be a JSON Structure add-in name string.", entryPointer);
                }

                var name = (string)entry;
                var addIn = JsonStructureDialects.ResolveAddIn(name);

                if (addIn == JsonStructureAddIns.None)
                {
                    throw new JsonStructureException(
                        "The '$uses' entry '" + name + "' is not a recognized JSON Structure add-in. " +
                        "Known add-ins are: JSONStructureValidation, JSONStructureAlternateNames, " +
                        "JSONStructureUnits, JSONStructureImport, JSONStructureConditionalComposition.",
                        entryPointer);
                }

                if ((offered & addIn) != addIn)
                {
                    throw new JsonStructureException(
                        "The add-in '" + name + "' is not offered by the '" + document.SchemaUri + "' meta-schema.",
                        entryPointer);
                }

                seen |= addIn;
            }

            document.AddIns |= seen;
        }

        private void ReadNamespace(
            JObject node, JsonStructureNamespace target, JsonStructureDocument document, string pointer)
        {
            foreach (var property in node.Properties())
            {
                if (property.Name == JsonStructureKeywords.Import ||
                    property.Name == JsonStructureKeywords.ImportDefs)
                {
                    continue;
                }

                if (property.Value is not JObject child)
                {
                    throw new JsonStructureException(
                        "A namespace entry must be a JSON object.", pointer + "/" + EscapePointerSegment(property.Name));
                }

                var childPointer = pointer + "/" + EscapePointerSegment(property.Name);

                if (IsTypeDeclaration(child))
                {
                    var schema = ReadSchema(child, document, childPointer, isDocumentRoot: false);
                    schema.Name ??= property.Name;
                    target.AddType(new JsonStructureNamedType(property.Name, schema, target));
                }
                else
                {
                    // Any object under 'definitions' that does not declare a 'type' is a namespace.
                    var childNamespace = new JsonStructureNamespace(property.Name, target);
                    target.AddNamespace(childNamespace);
                    ReadNamespace(child, childNamespace, document, childPointer);
                }
            }
        }

        private static bool IsTypeDeclaration(JObject node)
        {
            return node[JsonStructureKeywords.Type] != null;
        }

        private JsonStructureSchema ReadSchema(
            JObject node, JsonStructureDocument document, string pointer, bool isDocumentRoot)
        {
            var schema = new JsonStructureSchema
            {
                Pointer = pointer,
                SourceJson = node.DeepClone(),
                Name = (string)node[JsonStructureKeywords.Name],
                Description = (string)node[JsonStructureKeywords.Description],
                Selector = (string)node[JsonStructureKeywords.Selector],
            };

            ReadExtends(node, schema, pointer);

            if (node[JsonStructureKeywords.Abstract] is JValue { Type: JTokenType.Boolean } abstractValue)
            {
                schema.IsAbstract = (bool)abstractValue;
            }

            ReadType(node[JsonStructureKeywords.Type], schema, document, pointer, isDocumentRoot);
            if (node[JsonStructureKeywords.Type] == null && node[JsonStructureKeywords.Ref] is JValue { Type: JTokenType.String } directReference)
            {
                if (isDocumentRoot)
                {
                    throw new JsonStructureException("'$ref' must not be used at the document root.", pointer + "/$ref");
                }

                schema.Reference = (string)directReference;
                ValidateLocalPointer(schema.Reference, pointer + "/$ref");
            }

            ReadProperties(node, schema, document, pointer);
            ReadRequired(node, schema, pointer);
            ReadTupleOrder(node, schema, pointer);
            ReadChoices(node, schema, document, pointer);
            ReadItemsAndValues(node, schema, document, pointer);
            ReadAdditionalProperties(node, schema, document, pointer);
            ReadEnumeration(node, schema);
            ReadKeywords(node, schema, document);

            ApplyRequiredFlags(schema);

            return schema;
        }

        private void ReadType(
            JToken typeToken, JsonStructureSchema schema, JsonStructureDocument document, string pointer,
            bool isDocumentRoot)
        {
            switch (typeToken)
            {
                case null:
                    return;

                case JValue { Type: JTokenType.String } value:
                {
                    var name = (string)value;
                    if (!JsonStructureTypeKinds.TryParse(name, out var kind))
                    {
                        throw new JsonStructureException(
                            "'" + name + "' is not a JSON Structure type name.", pointer + "/type");
                    }

                    schema.Kind = kind;
                    return;
                }

                case JObject reference:
                {
                    var refValue = (string)reference[JsonStructureKeywords.Ref];
                    if (refValue == null || reference.Properties().Count() != 1)
                    {
                        throw new JsonStructureException(
                            "An object 'type' value must contain a single '$ref' property.", pointer + "/type");
                    }

                    if (isDocumentRoot)
                    {
                        throw new JsonStructureException(
                            "'$ref' must not be used inside the 'type' of the root object.", pointer + "/type");
                    }

                    schema.Reference = refValue;
                    ValidateLocalPointer(refValue, pointer + "/type/$ref");
                    ValidateLocalPointer(refValue, pointer + "/type/$ref");
                    return;
                }

                case JArray union:
                {
                    if (isDocumentRoot)
                    {
                        throw new JsonStructureException(
                            "A type union must not be declared at the document root; use '$root' instead.",
                            pointer + "/type");
                    }

                    ReadUnion(union, schema, document, pointer);
                    return;
                }

                default:
                    throw new JsonStructureException(
                        "The 'type' keyword must be a string, an array, or a '$ref' object.", pointer + "/type");
            }
        }

        private void ReadUnion(JArray union, JsonStructureSchema schema, JsonStructureDocument document, string pointer)
        {
            for (var i = 0; i < union.Count; i++)
            {
                var member = union[i];
                var memberPointer = pointer + "/type/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

                switch (member)
                {
                    case JValue { Type: JTokenType.String } value:
                    {
                        var name = (string)value;
                        if (!JsonStructureTypeKinds.TryParse(name, out var kind))
                        {
                            throw new JsonStructureException(
                                "'" + name + "' is not a JSON Structure type name.", memberPointer);
                        }

                        schema.AddUnionMember(new JsonStructureTypeReference(kind));
                        break;
                    }

                    case JObject obj when obj[JsonStructureKeywords.Ref] != null:
                        ValidateSingleRefObject(obj, memberPointer);
                        schema.AddUnionMember(
                            new JsonStructureTypeReference((string)obj[JsonStructureKeywords.Ref]));
                        break;

                    case JObject obj:
                    {
                        var inline = ReadSchema(obj, document, memberPointer, isDocumentRoot: false);

                        if (inline.Kind == JsonStructureTypeKind.Object)
                        {
                            throw new JsonStructureException(
                                "An 'object' type must not be declared inline inside a non-discriminated type union.",
                                memberPointer);
                        }

                        schema.AddUnionMember(new JsonStructureTypeReference(inline));
                        break;
                    }

                    default:
                        throw new JsonStructureException(
                            "A type union member must be a type name, a '$ref' object, or an inline compound type.",
                            memberPointer);
                }
            }
        }

        private static void ReadExtends(JObject node, JsonStructureSchema schema, string pointer)
        {
            var value = node[JsonStructureKeywords.Extends];
            if (value == null)
            {
                return;
            }

            if (value is JValue { Type: JTokenType.String } single)
            {
                var reference = (string)single;
                ValidateLocalPointer(reference, pointer + "/$extends");
                schema.AddExtends(reference);
                return;
            }

            if (value is JArray array)
            {
                if (array.Count == 0)
                {
                    throw new JsonStructureException("'$extends' must contain at least one local reference.", pointer + "/$extends");
                }

                foreach (var item in array)
                {
                    if (item is not JValue { Type: JTokenType.String } entry)
                    {
                        throw new JsonStructureException("Every '$extends' entry must be a local JSON Pointer.", pointer + "/$extends");
                    }

                    var reference = (string)entry;
                    ValidateLocalPointer(reference, pointer + "/$extends");
                    schema.AddExtends(reference);
                }

                return;
            }

            throw new JsonStructureException("'$extends' must be a local JSON Pointer or an array of local JSON Pointers.", pointer + "/$extends");
        }

        private static void ValidateSingleRefObject(JObject obj, string pointer)
        {
            if (obj.Properties().Count() != 1)
            {
                throw new JsonStructureException("A type union '$ref' object must contain only '$ref'.", pointer);
            }

            ValidateLocalPointer((string)obj[JsonStructureKeywords.Ref], pointer + "/$ref");
        }

        private static void ValidateLocalPointer(string value, string pointer)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '#' || (value.Length > 1 && value[1] != '/'))
            {
                throw new JsonStructureException("References must be local JSON Pointer fragments beginning with '#/'.", pointer);
            }

            var fragment = value.Substring(1);
            for (var i = 0; i < fragment.Length; i++)
            {
                if (fragment[i] == '%')
                {
                    if (i + 2 >= fragment.Length || !Uri.IsHexDigit(fragment[i + 1]) || !Uri.IsHexDigit(fragment[i + 2]))
                    {
                        throw new JsonStructureException("The JSON Pointer fragment contains an invalid percent escape.", pointer);
                    }
                    i += 2;
                }
            }

            var decoded = Uri.UnescapeDataString(fragment);
            foreach (var segment in decoded.Split('/').Skip(1))
            {
                for (var i = 0; i < segment.Length; i++)
                {
                    if (segment[i] == '~' && (i + 1 >= segment.Length || (segment[i + 1] != '0' && segment[i + 1] != '1')))
                    {
                        throw new JsonStructureException("The JSON Pointer contains an invalid '~' escape.", pointer);
                    }
                }
            }
        }

        private void ReadProperties(
            JObject node, JsonStructureSchema schema, JsonStructureDocument document, string pointer)
        {
            if (node[JsonStructureKeywords.Properties] is not JObject properties)
            {
                return;
            }

            foreach (var property in properties.Properties())
            {
                var propertyPointer = pointer + "/properties/" + EscapePointerSegment(property.Name);

                if (property.Value is not JObject propertySchema)
                {
                    throw new JsonStructureException("A property schema must be a JSON object.", propertyPointer);
                }

                schema.AddProperty(new JsonStructureProperty(
                    property.Name,
                    ReadSchema(propertySchema, document, propertyPointer, isDocumentRoot: false)));
            }
        }

        private static void ReadRequired(JObject node, JsonStructureSchema schema, string pointer)
        {
            if (node[JsonStructureKeywords.Required] is not JArray required)
            {
                return;
            }

            if (required.Count == 0)
            {
                return;
            }

            // 'required' is either a flat array of names or an array of alternative name arrays.
            if (required[0] is JArray)
            {
                foreach (var set in required)
                {
                    if (set is not JArray names)
                    {
                        throw new JsonStructureException(
                            "When 'required' declares alternative sets, every entry must be an array.",
                            pointer + "/required");
                    }

                    schema.AddRequiredSet(names.Select(n => (string)n).ToList());
                }
            }
            else
            {
                schema.AddRequiredSet(required.Select(n => (string)n).ToList());
            }

            foreach (var set in schema.RequiredSets)
            {
                foreach (var name in set)
                {
                    if (!schema.TryGetProperty(name, out _))
                    {
                        throw new JsonStructureException(
                            "The required property '" + name + "' is not declared in 'properties'.",
                            pointer + "/required");
                    }
                }
            }
        }

        private static void ReadTupleOrder(JObject node, JsonStructureSchema schema, string pointer)
        {
            if (node[JsonStructureKeywords.Tuple] is not JArray order)
            {
                if (schema.Kind == JsonStructureTypeKind.Tuple)
                {
                    throw new JsonStructureException(
                        "A 'tuple' type must declare its element order with the 'tuple' keyword.", pointer);
                }

                return;
            }

            foreach (var entry in order)
            {
                var name = (string)entry;

                if (!schema.TryGetProperty(name, out _))
                {
                    throw new JsonStructureException(
                        "The tuple element '" + name + "' is not declared in 'properties'.", pointer + "/tuple");
                }

                schema.AddTupleElement(name);
            }
        }

        private void ReadChoices(
            JObject node, JsonStructureSchema schema, JsonStructureDocument document, string pointer)
        {
            if (node[JsonStructureKeywords.Choices] is not JObject choices)
            {
                if (schema.Kind == JsonStructureTypeKind.Choice)
                {
                    throw new JsonStructureException(
                        "A 'choice' type must declare its variants with the 'choices' keyword.", pointer);
                }

                return;
            }

            foreach (var choice in choices.Properties())
            {
                var choicePointer = pointer + "/choices/" + EscapePointerSegment(choice.Name);

                if (choice.Value is not JObject choiceSchema)
                {
                    throw new JsonStructureException("A choice variant must be a JSON object.", choicePointer);
                }

                schema.AddChoice(new JsonStructureChoice(
                    choice.Name,
                    ReadSchema(choiceSchema, document, choicePointer, isDocumentRoot: false)));
            }

            if (schema.IsInlineUnion && schema.Selector == null)
            {
                throw new JsonStructureException(
                    "An inline union declared with '$extends' must declare a 'selector'.", pointer);
            }
        }

        private void ReadItemsAndValues(
            JObject node, JsonStructureSchema schema, JsonStructureDocument document, string pointer)
        {
            if (node[JsonStructureKeywords.Items] is JObject items)
            {
                schema.Items = ReadSchema(items, document, pointer + "/items", isDocumentRoot: false);
            }
            else if (schema.Kind is JsonStructureTypeKind.Array or JsonStructureTypeKind.Set)
            {
                throw new JsonStructureException(
                    "An '" + JsonStructureTypeKinds.GetName(schema.Kind) + "' type must declare 'items'.", pointer);
            }

            if (node[JsonStructureKeywords.Values] is JObject values)
            {
                schema.Values = ReadSchema(values, document, pointer + "/values", isDocumentRoot: false);
            }
            else if (schema.Kind == JsonStructureTypeKind.Map)
            {
                throw new JsonStructureException("A 'map' type must declare 'values'.", pointer);
            }
        }

        private void ReadAdditionalProperties(
            JObject node, JsonStructureSchema schema, JsonStructureDocument document, string pointer)
        {
            var additional = node[JsonStructureKeywords.AdditionalProperties];

            switch (additional)
            {
                case null:
                    return;

                case JValue { Type: JTokenType.Boolean } value:
                    schema.AdditionalPropertiesAllowed = (bool)value;
                    return;

                case JObject obj:
                    schema.AdditionalPropertiesAllowed = true;
                    schema.AdditionalProperties = ReadSchema(
                        obj, document, pointer + "/additionalProperties", isDocumentRoot: false);
                    return;

                default:
                    throw new JsonStructureException(
                        "'additionalProperties' must be a boolean or a schema.", pointer + "/additionalProperties");
            }
        }

        private static void ReadEnumeration(JObject node, JsonStructureSchema schema)
        {
            schema.Const = node[JsonStructureKeywords.Const];

            if (node[JsonStructureKeywords.Enum] is JArray enumeration)
            {
                foreach (var value in enumeration)
                {
                    schema.AddEnumerationValue(((JValue)value).Value);
                }
            }

            if (node[JsonStructureKeywords.Examples] is JArray examples)
            {
                schema.Examples = examples;
            }
        }

        private static void ReadKeywords(JObject node, JsonStructureSchema schema, JsonStructureDocument document)
        {
            foreach (var property in node.Properties())
            {
                if (JsonStructureKeywords.IsStructural(property.Name))
                {
                    continue;
                }

                if (JsonStructureKeywords.IsCoreAnnotation(property.Name))
                {
                    schema.SetAnnotation(property.Name, property.Value);
                }
                else if (JsonStructureKeywords.TryGetAddIn(property.Name, out var addIn) &&
                    document.IsAddInActive(addIn))
                {
                    schema.SetAnnotation(property.Name, property.Value);
                }
                else
                {
                    schema.SetExtensionData(property.Name, property.Value);
                }
            }
        }

        private static void ApplyRequiredFlags(JsonStructureSchema schema)
        {
            // All declared elements of a tuple are implicitly required.
            if (schema.Kind == JsonStructureTypeKind.Tuple)
            {
                foreach (var property in schema.Properties)
                {
                    property.IsRequired = true;
                }

                return;
            }

            if (schema.RequiredSets.Count == 0)
            {
                return;
            }

            foreach (var property in schema.Properties)
            {
                var inAll = schema.RequiredSets.All(set => set.Contains(property.Name, StringComparer.Ordinal));
                var inAny = schema.RequiredSets.Any(set => set.Contains(property.Name, StringComparer.Ordinal));

                property.IsRequired = inAll;
                property.IsConditionallyRequired = inAny && !inAll;
            }
        }

        private static string EscapePointerSegment(string segment)
        {
            return segment.Replace("~", "~0").Replace("/", "~1");
        }
    }
}
