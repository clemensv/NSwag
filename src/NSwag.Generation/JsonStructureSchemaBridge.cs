using NJsonSchema;
using NJsonSchema.Generation;
using NSwag.JsonStructure.OpenApi;
using Namotion.Reflection;

namespace NSwag.Generation
{
    internal static class JsonStructureSchemaBridge
    {
        public static JsonSchema Create(OpenApiDocumentGeneratorSettings settings, OpenApiDocument document, ContextualType type)
        {
            var structure = new JsonStructureSchemaGenerator(settings).Generate(type);
            var key = "#/x-nswag-json-structure/" + Guid.NewGuid().ToString("N");
            var schema = new JsonSchema
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["x-json-structure"] = true,
                    ["x-json-structure-key"] = key
                }
            };
            JsonStructureDocumentExtensions.RegisterGeneratedSchema(document, key, structure);
            return schema;
        }
    }
}
