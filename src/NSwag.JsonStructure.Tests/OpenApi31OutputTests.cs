using NSwag.JsonStructure.OpenApi;
using Xunit;

namespace NSwag.JsonStructure.Tests;

public class OpenApi31OutputTests
{
    [Fact]
    public async Task Json_output_restores_json_structure_schema_as_openapi31()
    {
        var document = await OpenApiDocument.FromJsonAsync(JsonDocument, "https://example.test/openapi.json");

        var json = document.ToJson(OpenApiDocumentOutputType.OpenApi31JsonStructure);

        Assert.Contains(@"""openapi"": ""3.1.0""", json);
        Assert.Contains(@"""jsonSchemaDialect"": ""https://json-structure.org/meta/core/v0/#""", json);
        Assert.Contains(@"""type"": ""object""", json);
        Assert.DoesNotContain("x-json-structure", json);
    }

    [Fact]
    public async Task Yaml_output_restores_json_structure_schema_as_openapi31()
    {
        var document = await OpenApiDocument.FromJsonAsync(JsonDocument, "https://example.test/openapi.json");

        var yaml = document.ToYaml(OpenApiDocumentOutputType.OpenApi31JsonStructure);

        Assert.Contains("openapi: 3.1.0", yaml);
        Assert.Contains("jsonSchemaDialect: https://json-structure.org/meta/core/v0/#", yaml);
        Assert.Contains("type: object", yaml);
        Assert.DoesNotContain("x-json-structure", yaml);
    }

    [Fact]
    public async Task Mixed_document_is_rejected()
    {
        var document = await OpenApiDocument.FromJsonAsync(MixedJsonDocument, "https://example.test/openapi.json");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            document.ToJson(OpenApiDocumentOutputType.OpenApi31JsonStructure));

        Assert.Contains("all-JSON-Structure", exception.Message);
    }

    private const string JsonDocument = """
{
  "openapi": "3.1.0",
  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
  "info": { "title": "Structure", "version": "1.0.0" },
  "paths": {},
  "components": {
    "schemas": {
      "Pet": {
        "$id": "https://example.test/schemas/pet",
        "type": "object",
        "properties": {
          "name": { "type": "string" }
        }
      }
    }
  }
}
""";

    private const string MixedJsonDocument = """
{
  "openapi": "3.1.0",
  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
  "info": { "title": "Structure", "version": "1.0.0" },
  "paths": {},
  "components": {
    "schemas": {
      "Pet": { "$id": "https://example.test/schemas/pet", "type": "object" },
      "Ordinary": {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "string"
      }
    }
  }
}
""";
}
