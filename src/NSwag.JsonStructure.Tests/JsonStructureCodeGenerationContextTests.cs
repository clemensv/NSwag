using NJsonSchema;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.OpenApi;
using NSwag.JsonStructure.Parsing;
using Xunit;

namespace NSwag.JsonStructure.Tests;

public class JsonStructureCodeGenerationContextTests
{
    [Fact]
    public void Ordinary_schema_is_not_resolved()
    {
        var document = new OpenApiDocument();
        var context = new JsonStructureCodeGenerationContext(document, ["Person"]);
        Assert.False(context.TryResolvePlaceholder(new JsonSchema(), out _));
    }

    [Fact]
    public void Placeholder_resolves_root_and_avoids_definition_collision()
    {
        var structure = new JsonStructureParser().Parse("""{"name":"Person","type":"object","properties":{"id":{"type":"string"}}}""");
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Person", structure)]));
        var schema = new JsonSchema
        {
            ExtensionData = new Dictionary<string, object>
            {
                [JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName] = "#/components/schemas/Person"
            }
        };

        var context = new JsonStructureCodeGenerationContext(document, ["Person"]);

        Assert.True(context.TryResolvePlaceholder(schema, out var name));
        Assert.Equal("JsonStructure_Person", name);
    }
}
