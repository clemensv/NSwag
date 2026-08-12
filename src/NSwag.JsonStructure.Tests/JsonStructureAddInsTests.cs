using NSwag.JsonStructure.Parsing;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>Tests JSON Structure add-in activation and add-in keyword routing.</summary>
    public class JsonStructureAddInsTests
    {
        /// <summary>Verifies that <c>$uses</c> activates an add-in offered by the selected dialect.</summary>
        [Fact]
        public void Uses_activates_offered_add_in()
        {
            var document = ParseWithSchema(
                JsonStructureDialects.ExtendedUri,
                """
                  "$uses": ["JSONStructureAlternateNames"],
                  "type": "object",
                  "properties": {
                    "name": {
                      "type": "string",
                      "altnames": { "json": "full_name" }
                    }
                  }
                }
                """);

            var property = document.RootSchema.Properties.Single(p => p.Name == "name");

            Assert.True(document.IsAddInActive(JsonStructureAddIns.AlternateNames));
            Assert.Contains(JsonStructureKeywords.AlternateNames, property.Schema.Annotations.Keys);
        }

        /// <summary>Verifies that <c>$uses</c> cannot activate an add-in not offered by the dialect.</summary>
        [Fact]
        public void Uses_rejects_add_in_not_offered_by_dialect()
        {
            var exception = Assert.Throws<JsonStructureException>(
                () => ParseWithSchema(
                    JsonStructureDialects.CoreUri,
                    """
                      "$uses": ["JSONStructureValidation"],
                      "type": "string",
                      "minLength": 1
                    }
                    """));

            Assert.Contains("not offered", exception.Message, StringComparison.Ordinal);
            Assert.Equal("#/$uses/0", exception.Pointer);
        }

        /// <summary>Verifies that <c>$uses</c> rejects unknown add-in names with an actionable diagnostic.</summary>
        [Fact]
        public void Uses_rejects_unknown_add_in_name()
        {
            var exception = Assert.Throws<JsonStructureException>(
                () => ParseWithSchema(
                    JsonStructureDialects.ExtendedUri,
                    """
                      "$uses": ["JSONStructureBananas"],
                      "type": "string"
                    }
                    """));

            Assert.Contains("JSONStructureBananas", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Known add-ins", exception.Message, StringComparison.Ordinal);
            Assert.Equal("#/$uses/0", exception.Pointer);
        }

        /// <summary>Verifies the add-ins that are active by default for canonical dialects.</summary>
        [Fact]
        public void Dialects_have_specified_default_add_ins()
        {
            const JsonStructureAddIns allAddIns = JsonStructureAddIns.Import
                | JsonStructureAddIns.Validation
                | JsonStructureAddIns.ConditionalComposition
                | JsonStructureAddIns.AlternateNames
                | JsonStructureAddIns.Units;

            Assert.Equal(JsonStructureAddIns.None, JsonStructureDialects.GetDefaultAddIns(JsonStructureDialect.Core));
            Assert.Equal(JsonStructureAddIns.Import, JsonStructureDialects.GetDefaultAddIns(JsonStructureDialect.Extended));
            Assert.Equal(allAddIns, JsonStructureDialects.GetDefaultAddIns(JsonStructureDialect.Validation));
        }

        /// <summary>Verifies validation keywords are annotations only when the validation add-in is active.</summary>
        [Fact]
        public void Validation_keyword_routing_depends_on_active_add_in()
        {
            var active = ParseWithSchema(
                JsonStructureDialects.ExtendedUri,
                """
                  "$uses": ["JSONStructureValidation"],
                  "type": "string",
                  "minLength": 1
                }
                """);

            var inactive = ParseWithSchema(
                JsonStructureDialects.ExtendedUri,
                """
                  "type": "string",
                  "minLength": 1
                }
                """);

            Assert.Contains("minLength", active.RootSchema.Annotations.Keys);
            Assert.DoesNotContain("minLength", inactive.RootSchema.Annotations.Keys);
            Assert.Contains("minLength", inactive.RootSchema.ExtensionData.Keys);
        }

        /// <summary>Verifies conditional-composition keywords are recorded as validation-only metadata.</summary>
        [Fact]
        public void Conditional_composition_keywords_are_validation_only_and_non_type_shaping()
        {
            var document = ParseWithSchema(
                JsonStructureDialects.ExtendedUri,
                """
                  "$uses": ["JSONStructureConditionalComposition"],
                  "type": "object",
                  "properties": {},
                  "oneOf": [
                    { "type": "object", "properties": {} },
                    { "type": "object", "properties": {} }
                  ]
                }
                """);

            Assert.True(document.IsAddInActive(JsonStructureAddIns.ConditionalComposition));
            Assert.Contains("oneOf", document.RootSchema.Annotations.Keys);
            Assert.True(JsonStructureKeywords.IsValidationOnly("oneOf"));
            Assert.False(JsonStructureKeywords.IsTypeShaping("oneOf"));
        }

        private static Model.JsonStructureDocument ParseWithSchema(string schemaUri, string body)
        {
            var envelope = $$"""
                {
                  "$schema": "{{schemaUri}}",
                  "$id": "https://example.com/add-ins",
                  "name": "AddInTest",
                """;

            return new JsonStructureParser().Parse(envelope + body.TrimStart());
        }
    }
}
