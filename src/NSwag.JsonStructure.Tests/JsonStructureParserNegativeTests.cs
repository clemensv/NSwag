using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>
    /// The parser is deliberately strict: a document it cannot represent faithfully must fail
    /// loudly rather than degrade into a lossy approximation. These tests pin that contract.
    /// </summary>
    public class JsonStructureParserNegativeTests
    {
        private const string CoreSchema = JsonStructureDialects.CoreUri;

        [Fact]
        public void Unknown_dialect_is_rejected_rather_than_treated_as_json_schema()
        {
            var json = $$"""
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "$id": "https://example.com/x",
                  "name": "X",
                  "type": "object"
                }
                """;

            Assert.Throws<JsonStructureException>(() => new JsonStructureParser().Parse(json));
        }

        [Fact]
        public void Near_miss_meta_schema_uri_is_rejected()
        {
            // Byte-for-byte matching is required by the binding; a trailing slash is a different URI.
            var json = $$"""
                {
                  "$schema": "{{CoreSchema}}/",
                  "$id": "https://example.com/x",
                  "name": "X",
                  "type": "object"
                }
                """;

            var exception = Assert.Throws<JsonStructureException>(() => new JsonStructureParser().Parse(json));
            Assert.Contains("$schema", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Unknown_type_name_is_rejected()
        {
            var exception = Assert.Throws<JsonStructureException>(
                () => Parse("""{ "type": "object", "properties": { "a": { "type": "varchar" } } }"""));

            Assert.Contains("varchar", exception.Message, StringComparison.Ordinal);
            Assert.Contains("/properties/a", exception.Pointer, StringComparison.Ordinal);
        }

        [Fact]
        public void Root_type_must_not_be_a_reference()
        {
            // The core draft forbids $ref in the root object's type.
            Assert.Throws<JsonStructureException>(
                () => Parse("""{ "type": { "$ref": "#/definitions/Other" } }"""));
        }

        [Fact]
        public void Type_and_root_are_mutually_exclusive()
        {
            Assert.Throws<JsonStructureException>(
                () => Parse("""{ "type": "object", "$root": "#/definitions/Thing" }"""));
        }

        [Fact]
        public void Non_object_document_is_rejected()
        {
            Assert.Throws<JsonStructureException>(() => new JsonStructureParser().Parse("[]"));
        }

        [Fact]
        public void Malformed_json_is_rejected_as_a_structure_error()
        {
            Assert.Throws<JsonStructureException>(() => new JsonStructureParser().Parse("{ not json"));
        }

        [Fact]
        public void Tuple_without_element_order_is_rejected()
        {
            // Without the "tuple" array the element order is undefined, so positional
            // serialization could not be generated.
            Assert.Throws<JsonStructureException>(
                () => Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "pair": { "type": "tuple", "properties": { "a": { "type": "string" } } }
                      }
                    }
                    """));
        }

        [Fact]
        public void Exception_carries_a_json_pointer_to_the_offending_node()
        {
            var exception = Assert.Throws<JsonStructureException>(
                () => Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "outer": {
                          "type": "object",
                          "properties": { "inner": { "type": "nope" } }
                        }
                      }
                    }
                    """));

            Assert.Equal("#/properties/outer/properties/inner/type", exception.Pointer);
        }

        private static JsonStructureDocument Parse(string body)
        {
            var envelope = $$"""
                {
                  "$schema": "{{CoreSchema}}",
                  "$id": "https://example.com/test",
                  "name": "Test",
                """;

            return new JsonStructureParser().Parse(envelope + body.TrimStart().TrimStart('{'));
        }
    }
}
