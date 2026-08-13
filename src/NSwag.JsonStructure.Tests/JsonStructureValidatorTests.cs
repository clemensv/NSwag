using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;
using NSwag.JsonStructure.Resolution;
using NSwag.JsonStructure.Validation;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>Tests for positioned JSON Structure meta-validation diagnostics.</summary>
    public class JsonStructureValidatorTests
    {
        private const string CoreSchema = JsonStructureDialects.CoreUri;

        private static readonly string[] MissingRequiredProperty = ["missing"];

        public static TheoryData<string> CorpusFiles
        {
            get
            {
                var data = new TheoryData<string>();
                foreach (var file in Directory.GetFiles(SchemaDirectory, "*.struct.json"))
                {
                    data.Add(Path.GetFileName(file));
                }

                return data;
            }
        }

        private static string SchemaDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas");

        [Theory]
        [MemberData(nameof(CorpusFiles))]
        public void Every_corpus_schema_validates_without_errors(string fileName)
        {
            var json = File.ReadAllText(Path.Combine(SchemaDirectory, fileName));
            var document = new JsonStructureParser().Parse(json);
            JsonStructureResolver.Resolve(document);

            var diagnostics = JsonStructureValidator.Validate(document);

            Assert.DoesNotContain(diagnostics, d => d.Severity == JsonStructureDiagnosticSeverity.Error);
        }

        [Fact]
        public void Root_requires_id_and_name_in_addition_to_schema()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "type": "object"
                }
                """);

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/$id", "must declare '$id'");
            AssertDiagnostic(diagnostics, "#/name", "must declare 'name'");
        }

        [Fact]
        public void Type_names_must_be_identifiers()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/test",
                  "name": "Test",
                  "definitions": {
                    "bad-name": { "type": "object" }
                  }
                }
                """);

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/definitions/bad-name", "must match [A-Za-z_][A-Za-z0-9_]*");
        }

        [Fact]
        public void Property_names_must_be_identifiers()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/test",
                  "name": "Test",
                  "type": "object",
                  "properties": {
                    "bad-name": { "type": "string" }
                  }
                }
                """);

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/properties/bad-name", "property name 'bad-name'");
        }

        [Fact]
        public void Array_and_set_types_require_items()
        {
            var document = DocumentWithRoot(new JsonStructureSchema
            {
                Pointer = "#",
                Kind = JsonStructureTypeKind.Array,
                Name = "Test"
            });

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/items", "must declare 'items'");
        }

        [Fact]
        public void Map_types_require_values()
        {
            var document = DocumentWithRoot(new JsonStructureSchema
            {
                Pointer = "#",
                Kind = JsonStructureTypeKind.Map,
                Name = "Test"
            });

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/values", "must declare 'values'");
        }

        [Fact]
        public void Tuple_types_require_properties_and_tuple_order()
        {
            var document = DocumentWithRoot(new JsonStructureSchema
            {
                Pointer = "#",
                Kind = JsonStructureTypeKind.Tuple,
                Name = "Test"
            });

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/properties", "must declare 'properties'");
            AssertDiagnostic(diagnostics, "#/tuple", "must declare its element order");
        }

        [Fact]
        public void Choice_types_require_choices()
        {
            var document = DocumentWithRoot(new JsonStructureSchema
            {
                Pointer = "#",
                Kind = JsonStructureTypeKind.Choice,
                Name = "Test"
            });

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/choices", "must declare its variants");
        }

        [Fact]
        public void Tagged_choices_must_not_declare_selector_without_extends()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/test",
                  "name": "Test",
                  "type": "choice",
                  "selector": "kind",
                  "choices": {
                    "text": { "type": "string" }
                  }
                }
                """);

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/selector", "tagged union must not declare a 'selector'");
        }

        [Fact]
        public void Inline_choices_require_selector_with_extends()
        {
            var document = DocumentWithRoot(new JsonStructureSchema
            {
                Pointer = "#",
                Kind = JsonStructureTypeKind.Choice,
                Name = "Test",
                Extends = "#/definitions/Base"
            });

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/selector", "must declare a 'selector'");
        }

        [Fact]
        public void Required_entries_must_reference_declared_properties()
        {
            var schema = new JsonStructureSchema
            {
                Pointer = "#",
                Kind = JsonStructureTypeKind.Object,
                Name = "Test"
            };
            schema.AddRequiredSet(MissingRequiredProperty);
            var document = DocumentWithRoot(schema);

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/required", "not declared in 'properties'");
        }

        [Fact]
        public void References_to_abstract_types_are_rejected()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/test",
                  "name": "Test",
                  "type": "object",
                  "properties": {
                    "value": { "type": { "$ref": "#/definitions/Base" } }
                  },
                  "definitions": {
                    "Base": {
                      "type": "object",
                      "abstract": true,
                      "properties": {
                        "id": { "type": "string" }
                      }
                    }
                  }
                }
                """);
            JsonStructureResolver.Resolve(document);

            var diagnostics = JsonStructureValidator.Validate(document);

            AssertDiagnostic(diagnostics, "#/properties/value/type/$ref", "abstract and cannot be instantiated directly");
        }

        [Fact]
        public void Inherited_property_collisions_are_rejected()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "$id": "https://example.com/test",
                  "name": "Test",
                  "definitions": {
                    "Base": { "type": "object", "abstract": true, "properties": { "id": { "type": "string" } } },
                    "Derived": { "type": "object", "$extends": "#/definitions/Base", "properties": { "id": { "type": "int32" } } }
                  }
                }
                """);
            JsonStructureResolver.Resolve(document);

            AssertDiagnostic(JsonStructureValidator.Validate(document), "#/definitions/Derived/properties/id", "collides with an inherited property");
        }

        [Fact]
        public void Multiple_errors_are_collected_in_one_pass()
        {
            var document = Parse("""
                {
                  "$schema": "https://json-structure.org/meta/core/v0/#",
                  "type": "object",
                  "properties": {
                    "bad-name": { "type": "string" }
                  }
                }
                """);

            var diagnostics = JsonStructureValidator.Validate(document)
                .Where(d => d.Severity == JsonStructureDiagnosticSeverity.Error)
                .ToList();

            Assert.True(diagnostics.Count >= 2);
            AssertDiagnostic(diagnostics, "#/$id", "must declare '$id'");
            AssertDiagnostic(diagnostics, "#/properties/bad-name", "property name 'bad-name'");
        }

        private static JsonStructureDocument Parse(string json)
        {
            return new JsonStructureParser().Parse(json);
        }

        private static JsonStructureDocument DocumentWithRoot(JsonStructureSchema schema)
        {
            var document = new JsonStructureDocument
            {
                SchemaUri = CoreSchema,
                Id = "https://example.com/test",
                Name = "Test",
                Dialect = JsonStructureDialect.Core,
                RootSchema = schema
            };
            return document;
        }

        private static void AssertDiagnostic(IEnumerable<JsonStructureDiagnostic> diagnostics, string pointer, string messagePart)
        {
            Assert.Contains(diagnostics, d => d.Pointer == pointer && d.Message.Contains(messagePart, StringComparison.Ordinal));
        }
    }
}
