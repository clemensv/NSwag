using NJsonSchema;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>
    /// Documents how NJsonSchema behaves when it is handed JSON Structure schemas.
    /// These tests exist to justify the raw-JSON preprocessor: JSON Structure schemas
    /// must be lifted out of an OpenAPI document *before* NJsonSchema deserializes it,
    /// because NJsonSchema either throws or silently loses the type information.
    /// </summary>
    public class NJsonSchemaToleranceSpikeTests
    {
        [Fact]
        public async Task NJsonSchema_cannot_represent_a_json_structure_type_reference()
        {
            // JSON Structure expresses a type reference as an object inside "type",
            // whereas JSON Schema's "type" is a string or an array of strings.
            const string json = """
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/person",
                  "name": "Person",
                  "type": "object",
                  "properties": {
                    "address": { "type": { "$ref": "#/definitions/Address" } }
                  },
                  "definitions": {
                    "Address": {
                      "name": "Address",
                      "type": "object",
                      "properties": { "street": { "type": "string" } }
                    }
                  }
                }
                """;

            var exception = await Record.ExceptionAsync(() => JsonSchema.FromJsonAsync(json));

            if (exception is null)
            {
                var schema = await JsonSchema.FromJsonAsync(json);
                var address = schema.Properties["address"];

                // If NJsonSchema does not throw, it must at least have lost the reference:
                // there is no way for it to model "type": { "$ref": ... }.
                Assert.True(
                    address.Reference is null && address.Type == JsonObjectType.None,
                    "NJsonSchema unexpectedly understood a JSON Structure type reference.");
            }
        }

        [Fact]
        public async Task NJsonSchema_cannot_represent_alternative_required_sets()
        {
            // JSON Structure allows "required" to hold mutually exclusive alternative
            // sets of property names; JSON Schema only allows a flat array of names.
            const string json = """
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/contact",
                  "name": "Contact",
                  "type": "object",
                  "properties": {
                    "email": { "type": "string" },
                    "phone": { "type": "string" }
                  },
                  "required": [["email"], ["phone"]]
                }
                """;

            var exception = await Record.ExceptionAsync(() => JsonSchema.FromJsonAsync(json));

            if (exception is null)
            {
                var schema = await JsonSchema.FromJsonAsync(json);
                Assert.DoesNotContain("email", schema.RequiredProperties);
                Assert.DoesNotContain("phone", schema.RequiredProperties);
            }
        }

        [Fact]
        public async Task NJsonSchema_does_not_understand_json_structure_compound_types()
        {
            // "set", "map" and "tuple" are not JSON Schema types. NJsonSchema either
            // throws or degrades them to JsonObjectType.None, losing the semantics.
            foreach (var structureType in new[] { "set", "map", "tuple" })
            {
                var json = $$"""
                    {
                      "$schema": "https://json-structure.org/meta/core/v0/#",
                      "$id": "https://example.com/value",
                      "name": "Value",
                      "type": "{{structureType}}"
                    }
                    """;

                var exception = await Record.ExceptionAsync(() => JsonSchema.FromJsonAsync(json));

                if (exception is null)
                {
                    var schema = await JsonSchema.FromJsonAsync(json);
                    Assert.Equal(JsonObjectType.None, schema.Type);
                }
            }
        }

        [Fact]
        public async Task NJsonSchema_keeps_unknown_json_structure_keywords_in_extension_data()
        {
            // The keywords that *do* survive land in ExtensionData. That is not enough to
            // reconstruct a JSON Structure type graph, but it confirms the placeholder
            // approach can smuggle a correlation key through NJsonSchema untouched.
            const string json = """
                {
                  "type": "object",
                  "x-json-structure": true,
                  "x-json-structure-key": "components/schemas/Person"
                }
                """;

            var schema = await JsonSchema.FromJsonAsync(json);

            Assert.NotNull(schema.ExtensionData);
            Assert.True(schema.ExtensionData.ContainsKey("x-json-structure"));
            Assert.Equal("components/schemas/Person", schema.ExtensionData["x-json-structure-key"]);
        }
    }
}
