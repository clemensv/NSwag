using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.OpenApi;
using Xunit;

namespace NSwag.Core.Yaml.Tests
{
    public class YamlDocumentTests
    {
        [Fact]
        public async Task When_yaml_with_description_is_loaded_then_document_is_not_null()
        {
            // Arrange
            var yaml = @"info:
  title: Foo
  version: 1.0.0
paths:
  /something:
    description: foo
    get:
      responses:
        200:
          description: get description";

            // Act
            var document = await OpenApiYamlDocument.FromYamlAsync(yaml);
            yaml = document.ToYaml();

            // Assert
            Assert.NotNull(document);
            Assert.Equal("foo", document.Paths.First().Value.Description);
            Assert.Contains("description: foo", yaml);
        }

        [Fact]
        public async Task When_yaml_with_custom_property_is_loaded_then_document_is_not_null()
        {
            // Arrange
            var yaml = @"swagger: '2.0'
info:
  title: foo
  version: '1.0'
paths:
  /bar:
    x-swagger-router-controller: bar
    get:
      responses:
        '200':
          description: baz";

            // Act
            var document = await OpenApiYamlDocument.FromYamlAsync(yaml);
            yaml = document.ToYaml();

            // Assert
            Assert.NotNull(document);
            Assert.Equal("bar", document.Paths.First().Value.ExtensionData["x-swagger-router-controller"]);
            Assert.Contains("x-swagger-router-controller: bar", yaml);
        }

        [Fact]
        public async Task When_yaml_with_custom_property_which_is_an_object_is_loaded_then_document_is_not_null()
        {
            // Arrange
            var yaml = @"swagger: '2.0'
info:
  title: foo
  version: '1.0'
paths:
  /bar:
    x-swagger-router-controller:
      bar: baz
    get:
      responses:
        '200':
          description: baz";

            // Act
            var document = await OpenApiYamlDocument.FromYamlAsync(yaml);
            yaml = document.ToYaml();

            // Assert
            Assert.NotNull(document);
            Assert.Equal(JObject.Parse(@"{""bar"": ""baz""}"), document.Paths.First().Value.ExtensionData["x-swagger-router-controller"]);
            Assert.Equal("baz", document.Paths.First().Value["get"].Responses["200"].Description);
            Assert.Contains("bar: baz", yaml);
        }

        [Fact]
        public async Task When_oas31_yaml_contains_json_structure_schema_then_it_is_preprocessed()
        {
            var yaml = @"openapi: 3.1.0
jsonSchemaDialect: https://json-structure.org/meta/core/v0/#
info:
  title: Structure
  version: 1.0.0
paths: {}
components:
  schemas:
    Telemetry:
      type: object
      properties:
        sequence:
          type: uint64";

            var document = await OpenApiYamlDocument.FromYamlAsync(yaml, "https://example.com/openapi.yaml");

            Assert.Equal("https://json-structure.org/meta/core/v0/#", document.JsonSchemaDialect);
            Assert.True(document.Components.Schemas["Telemetry"].ExtensionData.ContainsKey(JsonStructureDocumentPreprocessor.ExtensionName));
        }
    }
}