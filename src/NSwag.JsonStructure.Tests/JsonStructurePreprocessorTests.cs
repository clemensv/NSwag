using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NSwag;
using NSwag.JsonStructure.OpenApi;
using Xunit;
using YamlDotNet.Serialization;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>Tests the raw OpenAPI JSON preprocessor for JSON Structure Schema Objects.</summary>
    public class JsonStructurePreprocessorTests
    {
        public static TheoryData<string> SampleOpenApiDocuments
        {
            get
            {
                var data = new TheoryData<string>();
                foreach (var file in Directory.GetFiles(SampleDirectory, "openapi.yaml", SearchOption.AllDirectories))
                {
                    data.Add(Path.GetRelativePath(SampleDirectory, file));
                }

                return data;
            }
        }

        private static string DocumentDirectory => Path.Combine(AppContext.BaseDirectory, "Documents");

        private static string SampleDirectory => Path.Combine(DocumentDirectory, "oas-binding-samples");

        [Theory]
        [MemberData(nameof(SampleOpenApiDocuments))]
        public void Every_oas_binding_sample_preprocesses_and_lifts_schemas(string relativePath)
        {
            var filePath = Path.Combine(SampleDirectory, relativePath);
            var document = LoadYamlObject(filePath);

            var result = new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/" + relativePath.Replace('\\', '/'));

            Assert.NotEmpty(result.LiftedSchemas);
            Assert.All(result.LiftedSchemas, pair =>
            {
                Assert.NotNull(pair.Value.SourceJson);
                Assert.NotEqual(JsonStructureDialect.None, pair.Value.Dialect);
                Assert.False(string.IsNullOrEmpty(pair.Value.Id));
            });
        }

        [Fact]
        public void Plain_openapi_3_document_passes_through_unchanged()
        {
            var document = JObject.Parse("""
                {
                  "openapi": "3.0.3",
                  "info": { "title": "Plain", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "Pet": {
                        "type": "object",
                        "required": ["id"],
                        "properties": {
                          "id": { "type": "integer", "format": "int64" },
                          "name": { "type": "string" }
                        }
                      }
                    }
                  }
                }
                """);
            var expected = (JObject)document.DeepClone();

            var result = new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/openapi.json");

            Assert.Empty(result.LiftedSchemas);
            Assert.True(JToken.DeepEquals(expected, result.Document), result.Document.ToString());
        }

        [Fact]
        public void Document_level_dialect_applies_to_nested_schema_object_and_nested_override_wins()
        {
            var document = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Dialects", "version": "1.0.0" },
                  "paths": {
                    "/values": {
                      "get": {
                        "parameters": [
                          {
                            "name": "limit",
                            "in": "query",
                            "schema": { "type": "int32" }
                          }
                        ],
                        "responses": { "204": { "description": "No content" } }
                      }
                    }
                  },
                  "components": {
                    "schemas": {
                      "Overridden": {
                        "$schema": "https://json-structure.org/meta/validation/v0/#",
                        "type": "object"
                      }
                    }
                  }
                }
                """);

            var result = new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/openapi.yaml");

            Assert.Equal(JsonStructureDialect.Core, result.LiftedSchemas["#/paths/~1values/get/parameters/0/schema"].Dialect);
            Assert.Equal(JsonStructureDialect.Validation, result.LiftedSchemas["#/components/schemas/Overridden"].Dialect);
        }

        [Fact]
        public void Derived_meta_schema_requires_explicit_registration_and_preserves_base_dialect()
        {
            var document = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/company/v1/#",
                  "info": { "title": "Derived", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "Telemetry": { "type": "object", "properties": { "id": { "type": "uint64" } } }
                    }
                  }
                }
                """);

            Assert.Throws<JsonStructureException>(() =>
                new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/api.yaml"));

            var settings = new JsonStructureSettings();
            settings.RegisterDerivedMetaSchema("https://json-structure.org/meta/company/v1/#", JsonStructureDialect.Core);
            var result = new JsonStructureDocumentPreprocessor(settings).Preprocess(document, "https://example.com/api.yaml");

            Assert.Equal(JsonStructureDialect.Core, result.LiftedSchemas["#/components/schemas/Telemetry"].Dialect);
        }

        [Fact]
        public void Default_id_is_base_uri_with_schema_pointer_fragment()
        {
            var document = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Ids", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "TelemetryMessage": {
                        "type": "object",
                        "properties": { "sequence": { "type": "uint64" } }
                      }
                    }
                  }
                }
                """);

            var result = new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/api/openapi.yaml");

            var lifted = result.LiftedSchemas["#/components/schemas/TelemetryMessage"];
            Assert.Equal("https://example.com/api/openapi.yaml#/components/schemas/TelemetryMessage", lifted.Id);
            Assert.Equal("TelemetryMessage", lifted.Name);
            Assert.Equal(JsonStructureDialects.CoreUri, lifted.SchemaUri);
            Assert.Null(lifted.SourceJson["name"]);
        }

        [Fact]
        public async Task Placeholder_stub_carries_correlation_key_and_survives_njsonschema_deserialization()
        {
            var document = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Stubs", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "Pet": {
                        "name": "Pet",
                        "type": "object",
                        "properties": { "id": { "type": "int64" } }
                      }
                    }
                  }
                }
                """);

            var result = new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/openapi.yaml");
            var placeholder = (JObject)result.Document["components"]["schemas"]["Pet"];
            var stubRef = (string)placeholder["$ref"];
            var stubName = stubRef.Substring("#/components/schemas/".Length).Replace("~1", "/").Replace("~0", "~");
            var stub = (JObject)result.Document["components"]["schemas"][stubName];

            Assert.True((bool)stub[JsonStructureDocumentPreprocessor.ExtensionName]);
            Assert.Equal("#/components/schemas/Pet", (string)stub[JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName]);

            var schema = await JsonSchema.FromJsonAsync(stub.ToString());

            Assert.NotNull(schema.ExtensionData);
            Assert.True(schema.ExtensionData.ContainsKey(JsonStructureDocumentPreprocessor.ExtensionName));
            Assert.Equal("#/components/schemas/Pet", schema.ExtensionData[JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName]);
        }

        [Fact]
        public void Unknown_json_structure_dialect_reports_offending_location()
        {
            var document = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "info": { "title": "Bad", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "BadSchema": {
                        "$schema": "https://json-structure.org/meta/core/v999/#",
                        "type": "object"
                      }
                    }
                  }
                }
                """);

            var exception = Assert.Throws<JsonStructureException>(() =>
                new JsonStructureDocumentPreprocessor().Preprocess(document, "https://example.com/openapi.yaml"));

            Assert.Equal("#/components/schemas/BadSchema", exception.Pointer);
            Assert.Contains("/components/schemas/BadSchema", exception.Message, StringComparison.Ordinal);
            Assert.Contains("unknown JSON Structure dialect", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OpenApi_31_json_schema_is_deserialized_without_json_structure_lifting()
        {
            var document = await OpenApiDocument.FromJsonAsync("""
                {
                  "openapi": "3.1.0",
                  "info": { "title": "Plain", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "Pet": { "type": "object", "properties": { "id": { "type": "integer" } } }
                    }
                  }
                }
                """);

            Assert.Equal("3.1.0", document.OpenApi);
            Assert.Equal("object", document.Components.Schemas["Pet"].Type.ToString().ToLowerInvariant());
            Assert.False(document.Components.Schemas["Pet"].ExtensionData?.ContainsKey(JsonStructureDocumentPreprocessor.ExtensionName) == true);
        }

        [Fact]
        public async Task OpenApi_31_json_structure_schema_is_preprocessed_before_deserialization()
        {
            var document = await OpenApiDocument.FromJsonAsync("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Structure", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "Telemetry": { "type": "object", "properties": { "sequence": { "type": "uint64" } } }
                    }
                  }
                }
                """, "https://example.com/openapi.json");

            Assert.Equal("https://json-structure.org/meta/core/v0/#", document.JsonSchemaDialect);
            Assert.True(document.Components.Schemas["Telemetry"].ExtensionData.ContainsKey(JsonStructureDocumentPreprocessor.ExtensionName));
            Assert.Equal("#/components/schemas/Telemetry",
                document.Components.Schemas["Telemetry"].ExtensionData[JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName]);
        }

        [Fact]
        public async Task OpenApi_31_json_structure_dialect_is_inherited_and_nested_override_wins()
        {
            var document = await OpenApiDocument.FromJsonAsync("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Dialects", "version": "1.0.0" },
                  "paths": {},
                  "components": {
                    "schemas": {
                      "Inherited": { "type": "object" },
                      "Overridden": {
                        "$schema": "https://json-structure.org/meta/validation/v0/#",
                        "type": "object"
                      }
                    }
                  }
                }
                """, "https://example.com/openapi.json");

            Assert.True(document.Components.Schemas["Inherited"].ExtensionData.ContainsKey(JsonStructureDocumentPreprocessor.ExtensionName));
            Assert.True(document.Components.Schemas["Overridden"].ExtensionData.ContainsKey(JsonStructureDocumentPreprocessor.ExtensionName));
        }

        [Fact]
        public async Task Swagger_2_and_openapi_30_continue_to_use_existing_deserialization()
        {
            var swagger = await OpenApiDocument.FromJsonAsync("""
                {
                  "swagger": "2.0",
                  "info": { "title": "Swagger", "version": "1.0.0" },
                  "paths": {}
                }
                """);
            var openApi = await OpenApiDocument.FromJsonAsync("""
                {
                  "openapi": "3.0.3",
                  "info": { "title": "OpenAPI", "version": "1.0.0" },
                  "paths": {}
                }
                """);

            Assert.Equal(SchemaType.Swagger2, swagger.SchemaType);
            Assert.Equal(SchemaType.OpenApi3, openApi.SchemaType);
            Assert.Null(openApi.JsonSchemaDialect);
        }

        [Fact]
        public void Sample_corpus_is_not_empty()
        {
            Assert.NotEmpty(Directory.GetFiles(SampleDirectory, "openapi.yaml", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Lifted_documents_can_be_attached_to_and_retrieved_from_openapi_document()
        {
            var raw = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Side table", "version": "1.0.0" },
                  "paths": {},
                  "components": { "schemas": { "Pet": { "type": "object" } } }
                }
                """);
            var result = new JsonStructureDocumentPreprocessor().Preprocess(raw, "https://example.com/openapi.json");
            var openApiDocument = await OpenApiDocument.FromJsonAsync(result.Document.ToString(), null, SchemaType.OpenApi3);

            openApiDocument.AttachJsonStructureDocumentModel(result);

            Assert.Equal(
                result.LiftedSchemas["#/components/schemas/Pet"].Id,
                openApiDocument.GetJsonStructureDocument("#/components/schemas/Pet").Id);
            Assert.True(openApiDocument.TryGetJsonStructureDocumentModel(out var model));
            Assert.Single(model.LiftedSchemas);
            Assert.Null(new OpenApiDocument().GetJsonStructureDocumentModel());
        }

        [Fact]
        public void Openapi_schema_api_remains_jsonschema_typed()
        {
            Assert.Equal(typeof(NJsonSchema.JsonSchema), typeof(OpenApiMediaType).GetProperty(nameof(OpenApiMediaType.Schema)).PropertyType);
            Assert.Equal(typeof(NJsonSchema.JsonSchema), typeof(OpenApiParameter).GetProperty(nameof(OpenApiParameter.Schema)).PropertyType);
            Assert.Equal(typeof(NJsonSchema.JsonSchema), typeof(OpenApiResponse).GetProperty(nameof(OpenApiResponse.Schema)).PropertyType);
            Assert.Equal(typeof(IDictionary<string, NJsonSchema.JsonSchema>), typeof(OpenApiComponents).GetProperty(nameof(OpenApiComponents.Schemas)).PropertyType);
        }

        [Fact]
        public async Task Json_structure_schemas_round_trip_at_components_and_inline_schema_positions()
        {
            var source = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Round trip", "version": "1.0.0" },
                  "paths": {
                    "/pets": {
                      "post": {
                        "operationId": "pets",
                        "parameters": [
                          { "name": "limit", "in": "query", "schema": { "type": "uint32" } }
                        ],
                        "requestBody": {
                          "content": { "application/json": { "schema": { "type": "object", "name": "CreatePet" } } }
                        },
                        "responses": {
                          "200": {
                            "description": "OK",
                            "content": { "application/json": { "schema": { "type": "object", "name": "PetResponse" } } }
                          }
                        }
                      }
                    }
                  },
                  "components": {
                    "schemas": {
                      "Pet": { "type": "object", "properties": { "id": { "type": "uint64" } } }
                    }
                  }
                }
                """);

            var document = await OpenApiDocument.FromJsonAsync(source.ToString(), "https://example.com/api.json");
            Assert.NotNull(document.GetJsonStructureDocumentModel());
            var roundTripped = JObject.Parse(document.ToJson());

            Assert.True(JToken.DeepEquals(source, roundTripped), roundTripped.ToString());
        }

        [Fact]
        public async Task Json_structure_round_trip_preserves_nested_dialects_and_removes_placeholders()
        {
            var source = JObject.Parse("""
                {
                  "openapi": "3.1.0",
                  "jsonSchemaDialect": "https://json-structure.org/meta/core/v0/#",
                  "info": { "title": "Round trip", "version": "1.0.0" },
                  "paths": {
                    "/pets": {
                      "post": {
                        "parameters": [
                          { "name": "limit", "in": "query", "schema": { "type": "uint32" } }
                        ],
                        "requestBody": {
                          "content": {
                            "application/json": {
                              "schema": {
                                "$schema": "https://json-structure.org/meta/validation/v0/#",
                                "name": "CreatePet",
                                "type": "object",
                                "properties": { "name": { "type": "string" } }
                              }
                            }
                          }
                        },
                        "responses": {
                          "200": {
                            "description": "OK",
                            "content": {
                              "application/json": { "schema": { "type": "object", "name": "PetResponse" } }
                            }
                          }
                        }
                      }
                    }
                  },
                  "components": {
                    "schemas": {
                      "Pet": {
                        "type": "object",
                        "properties": { "id": { "type": "uint64" } }
                      }
                    }
                  }
                }
                """);

            var preprocessing = new JsonStructureDocumentPreprocessor().Preprocess(source, "https://example.com/api.json");
            Assert.Equal(JsonStructureDialect.Core, preprocessing.LiftedSchemas["#/paths/~1pets/post/parameters/0/schema"].Dialect);
            Assert.Equal(JsonStructureDialect.Validation, preprocessing.LiftedSchemas["#/paths/~1pets/post/requestBody/content/application~1json/schema"].Dialect);

            var document = await OpenApiDocument.FromJsonAsync(source.ToString(Formatting.None), "https://example.com/api.json");
            var output = JObject.Parse(document.ToJson());
            AssertLiftedSchemasEqual(source, output, preprocessing);
            AssertNoJsonStructurePlaceholders(output);
        }

        [Fact]
        public async Task Json_structure_schemas_round_trip_through_yaml()
        {
            var source = """
                openapi: 3.1.0
                jsonSchemaDialect: https://json-structure.org/meta/core/v0/#
                info:
                  title: YAML round trip
                  version: 1.0.0
                paths:
                  /pets:
                    post:
                      parameters:
                        - name: limit
                          in: query
                          schema:
                            type: uint32
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              name: CreatePet
                      responses:
                        '200':
                          description: OK
                          content:
                            application/json:
                              schema:
                                type: object
                                name: PetResponse
                components:
                  schemas:
                    Pet:
                      type: object
                      properties:
                        id:
                          type: uint64
                """;

            var sourceObject = LoadYamlObject(new StringReader(source));
            var preprocessing = new JsonStructureDocumentPreprocessor().Preprocess(sourceObject, "https://example.com/api.yaml");
            var document = await OpenApiYamlDocument.FromYamlAsync(source, "https://example.com/api.yaml");
            var output = LoadYamlObject(new StringReader(OpenApiYamlDocument.ToYaml(document)));

            AssertLiftedSchemasEqual(sourceObject, output, preprocessing);
            AssertNoJsonStructurePlaceholders(output);
        }

        [Theory]
        [MemberData(nameof(SampleOpenApiDocuments))]
        public async Task Oas_binding_samples_preserve_every_json_structure_schema_in_json_and_yaml(string relativePath)
        {
            var filePath = Path.Combine(SampleDirectory, relativePath);
            var source = LoadYamlObject(filePath);
            var documentUri = "https://example.com/" + relativePath.Replace('\\', '/');
            var preprocessing = new JsonStructureDocumentPreprocessor().Preprocess(source, documentUri);

            var jsonDocument = await OpenApiDocument.FromJsonAsync(source.ToString(Formatting.None), documentUri);
            var jsonOutput = JObject.Parse(jsonDocument.ToJson());
            AssertLiftedSchemasEqual(source, jsonOutput, preprocessing);
            AssertNoJsonStructurePlaceholders(jsonOutput);

            var yamlDocument = await OpenApiYamlDocument.FromYamlAsync(File.ReadAllText(filePath), documentUri);
            var yamlOutput = LoadYamlObject(new StringReader(OpenApiYamlDocument.ToYaml(yamlDocument)));
            AssertLiftedSchemasEqual(source, yamlOutput, preprocessing);
            AssertNoJsonStructurePlaceholders(yamlOutput);
        }

        private static JObject LoadYamlObject(string filePath)
        {
            var deserializer = new DeserializerBuilder().Build();
            var yamlObject = deserializer.Deserialize(File.ReadAllText(filePath));
            var serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();

            return JObject.Parse(serializer.Serialize(yamlObject));
        }

        private static JObject LoadYamlObject(TextReader reader)
        {
            var deserializer = new DeserializerBuilder().Build();
            var yamlObject = deserializer.Deserialize(reader);
            var serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();

            return JObject.Parse(serializer.Serialize(yamlObject));
        }

        private static void AssertLiftedSchemasEqual(
            JObject source,
            JObject output,
            JsonStructurePreprocessResult preprocessing)
        {
            Assert.NotEmpty(preprocessing.LiftedSchemas);
            foreach (var pair in preprocessing.LiftedSchemas)
            {
                var expected = pair.Value.SourceJson;
                var actual = ResolvePointer(output, pair.Key);
                Assert.True(
                    JToken.DeepEquals(expected, actual),
                    "Schema at " + pair.Key + " changed from:\n" + expected + "\nto:\n" + actual);
            }
        }

        private static void AssertNoJsonStructurePlaceholders(JObject output)
        {
            Assert.DoesNotContain(
                output.DescendantsAndSelf().OfType<JObject>(),
                obj => obj[JsonStructureDocumentPreprocessor.ExtensionName]?.Value<bool>() == true);

            var schemas = output["components"]?["schemas"] as JObject;
            Assert.DoesNotContain(
                schemas?.Properties() ?? Enumerable.Empty<JProperty>(),
                property => property.Name.StartsWith("__JsonStructure_", StringComparison.Ordinal));
        }

        private static JToken ResolvePointer(JToken root, string pointer)
        {
            var current = root;
            if (pointer == "#")
            {
                return current;
            }

            foreach (var segment in pointer.Substring(2).Split('/'))
            {
                var decoded = segment.Replace("~1", "/").Replace("~0", "~");
                current = current is JObject obj ? obj[decoded] :
                    current is JArray array && int.TryParse(decoded, out var index) ? array[index] : null;
                Assert.NotNull(current);
            }

            return current;
        }
    }
}
