//-----------------------------------------------------------------------
// <copyright file="JsonStructureDocumentPreprocessor.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;

namespace NSwag.JsonStructure.OpenApi
{
    /// <summary>
    /// Lifts JSON Structure Schema Objects out of a raw OpenAPI JSON document before NSwag deserializes it.
    /// </summary>
    public class JsonStructureDocumentPreprocessor
    {
        /// <summary>The extension flag placed on JSON Structure placeholders.</summary>
        public const string ExtensionName = "x-json-structure";

        /// <summary>The extension property containing the lifted-schema correlation key.</summary>
        public const string CorrelationKeyExtensionName = "x-json-structure-key";

        private const string ComponentsSchemasPointer = "/components/schemas";

        private readonly JsonStructureDialects _dialects;

        /// <summary>Initializes a new instance of the <see cref="JsonStructureDocumentPreprocessor"/> class.</summary>
        public JsonStructureDocumentPreprocessor()
            : this(JsonStructureDialects.Default)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureDocumentPreprocessor"/> class.</summary>
        /// <param name="dialects">The dialect recognizer.</param>
        public JsonStructureDocumentPreprocessor(JsonStructureDialects dialects)
        {
            if (dialects == null)
            {
                throw new ArgumentNullException(nameof(dialects));
            }

            _dialects = dialects;
        }

        /// <summary>Initializes a preprocessor from JSON Structure settings.</summary>
        /// <param name="settings">The JSON Structure settings.</param>
        public JsonStructureDocumentPreprocessor(JsonStructureSettings settings)
            : this((settings ?? throw new ArgumentNullException(nameof(settings))).CreateDialects())
        {
        }

        /// <summary>
        /// Preprocesses a raw OpenAPI document and replaces JSON Structure Schema Objects with inert placeholders.
        /// </summary>
        /// <param name="document">The raw OpenAPI document.</param>
        /// <param name="documentUri">The retrieval URI or application-specific base URI for default <c>$id</c> construction.</param>
        /// <returns>The preprocessed document and lifted schema map.</returns>
        /// <exception cref="JsonStructureException">A JSON Structure Schema Object is malformed or cannot be extracted.</exception>
        public JsonStructurePreprocessResult Preprocess(JObject document, string documentUri = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var clone = (JObject)document.DeepClone();
            var context = new PreprocessContext(
                GetDocumentDefaultDialectUri(clone),
                GetBaseUri(clone, documentUri),
                GetExistingComponentSchemaNames(clone));

            Traverse(clone, string.Empty, false, context);
            AddStubSchemas(clone, context);

            return new JsonStructurePreprocessResult(clone, context.LiftedSchemas);
        }

        /// <summary>Preprocesses a document using explicit JSON Structure settings.</summary>
        public static JsonStructurePreprocessResult Preprocess(JObject document, string documentUri, JsonStructureSettings settings)
        {
            return new JsonStructureDocumentPreprocessor(settings).Preprocess(document, documentUri);
        }

        private static string GetDocumentDefaultDialectUri(JObject document)
        {
            return (string)document["jsonSchemaDialect"];
        }

        private static IEnumerable<string> GetExistingComponentSchemaNames(JObject document)
        {
            if (document["components"] is JObject components && components["schemas"] is JObject schemas)
            {
                return schemas.Properties().Select(p => p.Name).ToList();
            }

            return Enumerable.Empty<string>();
        }

        private static string GetBaseUri(JObject document, string documentUri)
        {
            var self = (string)document["$self"];
            if (!string.IsNullOrEmpty(self))
            {
                return StripFragment(self);
            }

            return string.IsNullOrEmpty(documentUri) ? null : StripFragment(documentUri);
        }

        private static string StripFragment(string uri)
        {
            var hash = uri.IndexOf('#');
            return hash < 0 ? uri : uri.Substring(0, hash);
        }

        private void Traverse(JToken token, string pointer, bool isSchemaPosition, PreprocessContext context)
        {
            if (token is JObject obj)
            {
                if (isSchemaPosition && TryLiftSchema(obj, pointer, context))
                {
                    return;
                }

                TraverseObject(obj, pointer, isSchemaPosition, context);
            }
            else if (token is JArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    Traverse(array[i], pointer + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), false, context);
                }
            }
        }

        private void TraverseObject(JObject obj, string pointer, bool isSchemaPosition, PreprocessContext context)
        {
            foreach (var property in obj.Properties().ToList())
            {
                var childPointer = pointer + "/" + EscapePointerSegment(property.Name);

                if (pointer == ComponentsSchemasPointer)
                {
                    Traverse(property.Value, childPointer, true, context);
                    continue;
                }

                if (isSchemaPosition && IsSchemaMapProperty(property.Name) && property.Value is JObject map)
                {
                    foreach (var mapProperty in map.Properties().ToList())
                    {
                        Traverse(
                            mapProperty.Value,
                            childPointer + "/" + EscapePointerSegment(mapProperty.Name),
                            true,
                            context);
                    }

                    continue;
                }

                if (IsSchemaArrayProperty(property.Name) && property.Value is JArray schemaArray)
                {
                    for (var i = 0; i < schemaArray.Count; i++)
                    {
                        Traverse(
                            schemaArray[i],
                            childPointer + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            isSchemaPosition,
                            context);
                    }

                    continue;
                }

                var childIsSchema = property.Name == "schema" ||
                    (isSchemaPosition && IsDirectSchemaProperty(property.Name));

                Traverse(property.Value, childPointer, childIsSchema, context);
            }
        }

        private bool TryLiftSchema(JObject schema, string pointer, PreprocessContext context)
        {
            if (IsOpenApiReferenceObject(schema))
            {
                return false;
            }

            var explicitSchemaUri = (string)schema["$schema"];
            var effectiveSchemaUri = explicitSchemaUri ?? context.DocumentDefaultDialectUri;
            var dialect = _dialects.Resolve(effectiveSchemaUri);

            if (dialect == JsonStructureDialect.None)
            {
                if (_dialects.IsUnrecognizedJsonStructureUri(effectiveSchemaUri))
                {
                    throw new JsonStructureException(
                        "The Schema Object at '" + ToDisplayPointer(pointer) + "' declares unknown JSON Structure dialect '" +
                        effectiveSchemaUri + "'. Recognized dialects are: " +
                        string.Join(", ", JsonStructureDialects.CanonicalUris) + ".",
                        ToExceptionPointer(pointer));
                }

                return false;
            }

            var materialized = MaterializeDefaults(schema, pointer, effectiveSchemaUri, dialect, context);
            JsonStructureDocument document;
            try
            {
                document = new JsonStructureParser(_dialects)
                {
                    DefaultDialect = dialect
                }.Parse(materialized);
            }
            catch (JsonStructureException exception)
            {
                throw new JsonStructureException(
                    "The JSON Structure Schema Object at '" + ToDisplayPointer(pointer) + "' is invalid: " + exception.Message,
                    CombinePointers(pointer, exception.Pointer),
                    exception);
            }

            document.DocumentPath = document.Id;
            document.SourceJson = schema.DeepClone();

            var correlationKey = ToExceptionPointer(pointer);
            context.LiftedSchemas.Add(correlationKey, document);

            var stubName = context.CreateStubName(document.Name, pointer);
            context.Stubs.Add(new StubSchema(stubName, correlationKey));
            ReplaceWithReference(schema, stubName, correlationKey);

            return true;
        }

        private static JObject MaterializeDefaults(
            JObject schema,
            string pointer,
            string effectiveSchemaUri,
            JsonStructureDialect dialect,
            PreprocessContext context)
        {
            var materialized = (JObject)schema.DeepClone();

            if (materialized["$schema"] == null)
            {
                materialized.AddFirst(new JProperty("$schema", effectiveSchemaUri));
            }

            if (materialized["$id"] == null)
            {
                if (string.IsNullOrEmpty(context.BaseUri))
                {
                    throw new JsonStructureException(
                        "The JSON Structure Schema Object at '" + ToDisplayPointer(pointer) +
                        "' does not declare '$id' and no OpenAPI '$self' or retrieval URI was supplied for default '$id' construction.",
                        ToExceptionPointer(pointer));
                }

                materialized.Add(new JProperty("$id", context.BaseUri + "#" + pointer));
            }

            if (materialized["type"] != null && materialized["name"] == null)
            {
                materialized.Add(new JProperty("name", CreateNameFromPointer(pointer)));
            }

            return materialized;
        }

        private static void ReplaceWithReference(JObject schema, string stubName, string correlationKey)
        {
            schema.RemoveAll();
            schema.Add("$ref", "#/components/schemas/" + EscapePointerSegment(stubName));
            schema.Add(ExtensionName, true);
            schema.Add(CorrelationKeyExtensionName, correlationKey);
        }

        private static void AddStubSchemas(JObject document, PreprocessContext context)
        {
            if (context.Stubs.Count == 0)
            {
                return;
            }

            if (document["components"] is not JObject components)
            {
                components = new JObject();
                document["components"] = components;
            }

            if (components["schemas"] is not JObject schemas)
            {
                schemas = new JObject();
                components["schemas"] = schemas;
            }

            foreach (var stub in context.Stubs)
            {
                schemas[stub.Name] = new JObject
                {
                    [ExtensionName] = true,
                    [CorrelationKeyExtensionName] = stub.CorrelationKey
                };
            }
        }

        private static bool IsOpenApiReferenceObject(JObject schema)
        {
            return schema["$ref"] != null && schema["type"] == null && schema["$schema"] == null;
        }

        private static bool IsDirectSchemaProperty(string propertyName)
        {
            return propertyName == "items" ||
                propertyName == "additionalProperties" ||
                propertyName == "unevaluatedProperties" ||
                propertyName == "contains" ||
                propertyName == "propertyNames" ||
                propertyName == "not" ||
                propertyName == "if" ||
                propertyName == "then" ||
                propertyName == "else";
        }

        private static bool IsSchemaMapProperty(string propertyName)
        {
            return propertyName == "properties" ||
                propertyName == "patternProperties" ||
                propertyName == "$defs" ||
                propertyName == "definitions" ||
                propertyName == "dependentSchemas";
        }

        private static bool IsSchemaArrayProperty(string propertyName)
        {
            return propertyName == "allOf" ||
                propertyName == "anyOf" ||
                propertyName == "oneOf" ||
                propertyName == "prefixItems";
        }

        private static string CombinePointers(string rootPointer, string childPointer)
        {
            if (string.IsNullOrEmpty(childPointer) || childPointer == "#")
            {
                return ToExceptionPointer(rootPointer);
            }

            if (childPointer.StartsWith("#/", StringComparison.Ordinal))
            {
                return ToExceptionPointer(rootPointer) + childPointer.Substring(1);
            }

            return ToExceptionPointer(rootPointer);
        }

        private static string ToDisplayPointer(string pointer)
        {
            return string.IsNullOrEmpty(pointer) ? "/" : pointer;
        }

        private static string ToExceptionPointer(string pointer)
        {
            return string.IsNullOrEmpty(pointer) ? "#" : "#" + pointer;
        }

        private static string EscapePointerSegment(string segment)
        {
            return segment.Replace("~", "~0").Replace("/", "~1");
        }

        private static string CreateNameFromPointer(string pointer)
        {
            var raw = pointer;
            var slash = raw.LastIndexOf('/');
            if (slash >= 0 && slash < raw.Length - 1)
            {
                raw = raw.Substring(slash + 1);
            }

            raw = raw.Replace("~1", "/").Replace("~0", "~");

            var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            var name = new string(chars).Trim('_');
            return string.IsNullOrEmpty(name) ? "JsonStructureSchema" : name;
        }

        private sealed class PreprocessContext
        {
            private readonly HashSet<string> _stubNames = new HashSet<string>(StringComparer.Ordinal);

            public PreprocessContext(
                string documentDefaultDialectUri,
                string baseUri,
                IEnumerable<string> existingComponentSchemaNames)
            {
                DocumentDefaultDialectUri = documentDefaultDialectUri;
                BaseUri = baseUri;

                foreach (var name in existingComponentSchemaNames)
                {
                    _stubNames.Add(name);
                }
            }

            public string DocumentDefaultDialectUri { get; }

            public string BaseUri { get; }

            public Dictionary<string, JsonStructureDocument> LiftedSchemas { get; } =
                new Dictionary<string, JsonStructureDocument>(StringComparer.Ordinal);

            public List<StubSchema> Stubs { get; } = new List<StubSchema>();

            public string CreateStubName(string name, string pointer)
            {
                var baseName = "__JsonStructure_" + CreateNameFromPointer(name ?? pointer);
                var candidate = baseName;
                var index = 2;

                while (!_stubNames.Add(candidate))
                {
                    candidate = baseName + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    index++;
                }

                return candidate;
            }
        }

        private sealed class StubSchema
        {
            public StubSchema(string name, string correlationKey)
            {
                Name = name;
                CorrelationKey = correlationKey;
            }

            public string Name { get; }

            public string CorrelationKey { get; }
        }
    }
}
