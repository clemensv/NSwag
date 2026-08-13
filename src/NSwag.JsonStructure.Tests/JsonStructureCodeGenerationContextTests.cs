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
    public void Placeholder_resolves_to_resource_qualified_root()
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
        Assert.Equal("Person_2.Person", name);
    }

    [Fact]
    public void Distinct_resources_keep_identically_named_types_in_separate_scopes()
    {
        var pet = new JsonStructureParser().Parse("""
            {
              "name": "Pet",
              "type": "object",
              "properties": { "name": { "type": "string" } }
            }
            """);
        var response = new JsonStructureParser().Parse("""
            {
              "name": "PetListResponse",
              "type": "object",
              "properties": {
                "pets": {
                  "type": "array",
                  "items": { "type": { "$ref": "#/definitions/Pet" } }
                }
              },
              "definitions": {
                "Pet": {
                  "type": "object",
                  "properties": { "tags": { "type": "set", "items": { "type": "string" } } }
                }
              }
            }
            """);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
        [
            new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Pet", pet),
            new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/PetListResponse", response)
        ]));

        var context = new JsonStructureCodeGenerationContext(document, []);
        var models = context.Models.Select(entry => entry.Model).ToList();
        var standalonePet = models.Single(model => model.ScopeName == "Pet").RootType;
        var responseModel = models.Single(model => model.ScopeName == "PetListResponse");
        var nestedPet = responseModel.Types.Single(type => type.Name == "Pet");
        var arrayPet = responseModel.RootType.Properties.Single(property => property.Name == "pets")
            .Type.ElementType.NamedType;

        Assert.Equal("Pet.Pet", context.GetName(standalonePet));
        Assert.Equal("PetListResponse.PetListResponse", context.GetName(responseModel.RootType));
        Assert.Equal("PetListResponse.Pet", context.GetName(nestedPet));
        Assert.Same(nestedPet, arrayPet);
        Assert.DoesNotContain("_2", context.GetName(nestedPet));
    }

    [Fact]
    public void Duplicate_resource_scope_names_are_rejected()
    {
        var first = new JsonStructureParser().Parse("""{"name":"Duplicate","type":"object"}""");
        var second = new JsonStructureParser().Parse("""{"name":"Duplicate","type":"object"}""");
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
        [
            new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/First", first),
            new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Second", second)
        ]));

        var exception = Assert.Throws<JsonStructureException>(
            () => new JsonStructureCodeGenerationContext(document, []));

        Assert.Contains("resource name 'Duplicate' is duplicated", exception.Message);
    }
}
