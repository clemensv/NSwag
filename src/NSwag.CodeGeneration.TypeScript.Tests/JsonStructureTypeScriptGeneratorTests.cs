using NSwag.CodeGeneration.TypeScript;
using NSwag.CodeGeneration.Tests;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Resolution;
using NSwag.JsonStructure.OpenApi;
using NSwag.JsonStructure.Parsing;

namespace NSwag.CodeGeneration.TypeScript.Tests;

public class JsonStructureTypeScriptGeneratorTests
{
    [Fact]
    public void Aggregated_resources_keep_local_pet_types_and_qualified_roots()
    {
        var document = CreateAggregateNamespaceDocument();
        var settings = new TypeScriptClientGeneratorSettings();
        settings.TypeScriptGeneratorSettings.MarkOptionalProperties = true;
        var generator = new TypeScriptClientGenerator(document, settings);

        var code = generator.GenerateFile();

        Assert.Equal("Pet.Pet", generator.GetTypeName(document.Definitions["Pet"], false, null));
        Assert.Equal("PetListResponse.PetListResponse",
            generator.GetTypeName(document.Definitions["PetListResponse"], false, null));
        Assert.Contains("export namespace Pet", code);
        Assert.Contains("export namespace PetListResponse", code);
        Assert.Contains("pets?: Pet[]", code);
        Assert.DoesNotContain("Pet_2", code);
        TypeScriptCompiler.AssertCompile(code);
    }

    [Fact]
    public async Task Generates_and_compiles_representative_corpus()
    {
        var outputs = new List<string>();
        foreach (var file in new[]
        {
            "basic-types.struct.json",
            "choice-types.struct.json",
            "collections.struct.json",
            "complex-scenario.struct.json",
            "numeric-types.struct.json",
            "nested-objects.struct.json",
            "advanced-features.struct.json",
            "validation-constraints.struct.json"
        })
        {
            var structure = new JsonStructureParser().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", file)));
            var document = new OpenApiDocument();
            document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
                [new KeyValuePair<string, JsonStructureDocument>($"#/components/schemas/{structure.RootSchema?.Name ?? file}", structure)]));

            var settings = new TypeScriptClientGeneratorSettings();
            settings.TypeScriptGeneratorSettings.Namespace = "Corpus.Generated";
            settings.TypeScriptGeneratorSettings.MarkOptionalProperties = true;
            outputs.Add($"// {file}{Environment.NewLine}{new TypeScriptClientGenerator(document, settings).GenerateFile()}");
        }

        var code = string.Join(Environment.NewLine, outputs);
        await VerifyHelper.Verify(code);
        TypeScriptCompiler.AssertCompile(code);
    }

    [Fact]
    public async Task Ordinary_openapi_schema_regression_still_generates_and_compiles()
    {
        var document = await OpenApiDocument.FromJsonAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "ordinary", "version": "1.0" },
              "paths": {},
              "components": {
                "schemas": {
                  "Ordinary": {
                    "type": "object",
                    "required": ["id"],
                    "properties": { "id": { "type": "integer", "format": "int64" }, "name": { "type": "string" } }
                  }
                }
              }
            }
            """);
        var code = new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings()).GenerateFile();

        await VerifyHelper.Verify(code);
        TypeScriptCompiler.AssertCompile(code);
    }

    [Fact]
    public void Maps_compound_types_and_datetime_settings()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "namespace": "sample.models",
              "name": "Message",
              "type": "object",
              "properties": {
                "when": { "type": "datetime" },
                "ids": { "type": "set", "items": { "type": "int128" } },
                "data": { "type": "binary" },
                "values": { "type": "map", "values": { "type": "decimal" } },
                "pair": { "type": "tuple", "tuple": ["first", "second"], "properties": {
                  "first": { "type": "string" }, "second": { "type": "null" }
                } }
              }
            }
            """);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Message", structure)]));

        var code = new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings
        {
            TypeScriptGeneratorSettings =
            {
                DateTimeType = NJsonSchema.CodeGeneration.TypeScript.TypeScriptDateTimeType.String,
                MarkOptionalProperties = true
            }
        }).GenerateFile();

        Assert.Contains("when?: string;", code);
        Assert.Contains("ids?: Set<bigint>;", code);
        Assert.Contains("data?: string;", code);
        Assert.Contains("values?: { [key: string]: string };", code);
        Assert.Contains("pair?: [string, null];", code);
    }

    [Fact]
    public void Generates_collection_conversion_code()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "name": "Collections",
              "type": "object",
              "properties": {
                "tags": { "type": "set", "items": { "type": "string" } },
                "counts": { "type": "map", "values": { "type": "integer" } },
                "point": { "type": "tuple", "tuple": ["x", "y"], "properties": {
                  "x": { "type": "string" }, "y": { "type": "integer" }
                } }
              }
            }
            """);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Collections", structure)]));

        var code = new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings()).GenerateFile();

        Assert.Contains("class Collections implements ICollections", code);
        Assert.Contains("new Set((_data[\"tags\"] || []).map", code);
        Assert.Contains("Object.keys(_data[\"counts\"] || {}).reduce", code);
        Assert.Contains("return [", code);
        Assert.Contains("Array.from(this.tags || []).map", code);
        Assert.Contains("Object.keys(this.counts || {}).reduce", code);
    }

    [Fact]
    public void Generates_discriminated_choices_and_runtime_conversion()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "name": "ChoiceContainer",
              "type": "object",
              "properties": {
                "value": { "type": { "$ref": "#/definitions/Value" } }
              },
              "definitions": {
                "Base": {
                  "type": "object",
                  "abstract": true,
                  "properties": { "kind": { "type": "string" } }
                },
                "Text": {
                  "type": "object",
                  "$extends": "#/definitions/Base",
                  "properties": { "text": { "type": "string" } }
                },
                "Number": {
                  "type": "object",
                  "$extends": "#/definitions/Base",
                  "properties": { "number": { "type": "integer" } }
                },
                "Value": {
                  "type": "choice",
                  "$extends": "#/definitions/Base",
                  "selector": "kind",
                  "choices": {
                    "text": { "type": { "$ref": "#/definitions/Text" } },
                    "number": { "type": { "$ref": "#/definitions/Number" } }
                  }
                },
                "Inline": {
                  "type": "choice",
                  "choices": {
                    "text": { "type": "string" },
                    "number": { "type": "integer" }
                  }
                }
              }
            }
            """);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/ChoiceContainer", structure)]));

        var code = new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings()).GenerateFile();

        Assert.Contains("export type Value =", code);
        Assert.Contains("Text", code);
        Assert.Contains("Number", code);
        Assert.Contains("namespace Value", code);
        Assert.Contains("value", code);
        Assert.Contains("export type Inline = string | number;", code);
    }

    private static OpenApiDocument CreateAggregateNamespaceDocument()
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
                  "properties": {
                    "tags": { "type": "set", "items": { "type": "string" } }
                  }
                }
              }
            }
            """);
        var document = new OpenApiDocument();
        document.Definitions["Pet"] = CreatePlaceholder("#/components/schemas/Pet");
        document.Definitions["PetListResponse"] = CreatePlaceholder("#/components/schemas/PetListResponse");
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
        [
            new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Pet", pet),
            new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/PetListResponse", response)
        ]));
        return document;
    }

    private static NJsonSchema.JsonSchema CreatePlaceholder(string correlationKey)
    {
        return new NJsonSchema.JsonSchema
        {
            ExtensionData = new Dictionary<string, object>
            {
                [JsonStructureDocumentPreprocessor.ExtensionName] = true,
                [JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName] = correlationKey
            }
        };
    }
}
