using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;
using NSwag.JsonStructure.Resolution;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    public class JsonStructureCodeGenerationModelTests
    {
        [Fact]
        public void Projects_resolved_structure_without_using_conditional_validation_to_shape_types()
        {
            var document = new JsonStructureParser().Parse("""
                {
                  "$schema": "https://json-structure.org/meta/extended/v0/#",
                  "$id": "https://example.com/model",
                  "name": "Model",
                  "$uses": ["JSONStructureConditionalComposition", "JSONStructureUnits"],
                  "definitions": {
                    "Transport": {
                      "Base": {
                        "type": "object",
                        "abstract": true,
                        "unit": "ignored-on-type",
                        "properties": { "id": { "type": "uuid" } }
                      },
                      "Car": {
                        "type": "object",
                        "$extends": "#/definitions/Transport/Base",
                        "properties": {
                          "label": {
                            "type": ["null", "string"],
                            "if": { "properties": { "kind": { "const": "car" } } },
                            "then": { "required": ["label"] }
                          }
                        }
                      }
                    },
                    "Payload": {
                      "type": "choice",
                      "selector": "kind",
                      "$extends": "#/definitions/Transport/Base",
                      "choices": {
                        "car": { "type": { "$ref": "#/definitions/Transport/Car" } },
                        "bike": { "type": "string" }
                      }
                    },
                    "Tuple": {
                      "type": "tuple",
                      "tuple": ["second", "first"],
                      "properties": {
                        "first": { "type": "int32" },
                        "second": { "type": "set", "items": { "type": "uuid" } }
                      }
                    },
                    "Container": {
                      "type": "object",
                      "properties": {
                        "payload": { "type": { "$ref": "#/definitions/Payload" } },
                        "values": { "type": "map", "values": { "type": ["null", "decimal"] } },
                        "tuple": { "type": { "$ref": "#/definitions/Tuple" } }
                      }
                    }
                  }
                }
                """);

            JsonStructureResolver.Resolve(document);
            var model = JsonStructureCodeGenerationModel.Create(document);

            var car = model.Types.Single(type => type.FullName == "Transport.Car");
            Assert.Equal("Transport", string.Join(".", car.NamespacePath));
            Assert.Equal("Transport.Base", car.BaseType.FullName);
            Assert.Equal(JsonStructureTypeKind.String, car.Properties.Single().Type.Kind);
            Assert.True(car.Properties.Single().Type.IsNullable);
            Assert.Contains("if", car.Properties.Single().Annotations.Keys);

            var payload = model.Types.Single(type => type.Name == "Payload");
            Assert.Equal("kind", payload.Selector);
            Assert.True(payload.IsInlineChoice);
            Assert.Equal("car,bike", string.Join(",", payload.Choices.Select(choice => choice.DiscriminatorName)));
            Assert.Equal("Transport.Car", payload.Choices[0].Type.NamedType.FullName);

            var tuple = model.Types.Single(type => type.Name == "Tuple");
            Assert.Equal("second,first", string.Join(",", tuple.TupleOrder));
            var tupleProperty = tuple.Properties.Single(property => property.Name == "second");
            Assert.Equal(JsonStructureTypeKind.Set, tupleProperty.Type.Kind);
            Assert.Equal(JsonStructureTypeKind.Uuid, tupleProperty.Type.ElementType.Kind);

            var container = model.Types.Single(type => type.Name == "Container");
            var values = container.Properties.Single(property => property.Name == "values").Type;
            Assert.Equal(JsonStructureTypeKind.Map, values.Kind);
            Assert.True(values.ValueType.IsNullable);
            Assert.Equal(JsonStructureTypeKind.Decimal, values.ValueType.Kind);
        }
    }
}
