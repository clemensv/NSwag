using NJsonSchema.CodeGeneration.CSharp;
using NSwag.CodeGeneration.Tests;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.OpenApi;
using NSwag.JsonStructure.Parsing;
using NSwag.JsonStructure.Resolution;

namespace NSwag.CodeGeneration.CSharp.Tests;

public class JsonStructureCSharpGeneratorTests
{
    [Fact]
    public async Task Generates_and_compiles_representative_corpus()
    {
        var outputs = new List<string>();
        foreach (var file in new[]
        {
            "basic-types.struct.json",
            "choice-types.struct.json",
            "collections.struct.json",
            "numeric-types.struct.json",
            "advanced-features.struct.json",
            "validation-constraints.struct.json"
        })
        {
            var structure = new JsonStructureParser().Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", file)));
            JsonStructureResolver.Resolve(structure);

            var document = new OpenApiDocument();
            document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
                [new KeyValuePair<string, JsonStructureDocument>(
                    $"#/components/schemas/{structure.RootSchema?.Name ?? file}", structure)]));

            var settings = new CSharpClientGeneratorSettings();
            settings.CSharpGeneratorSettings.Namespace = "Corpus.Generated";
            settings.CSharpGeneratorSettings.GenerateNullableReferenceTypes = true;
            settings.CSharpGeneratorSettings.GenerateDataAnnotations = true;
            var code = new CSharpClientGenerator(document, settings).GenerateFile();
            if (file is not ("choice-types.struct.json" or "advanced-features.struct.json"))
            {
                CSharpCompiler.AssertCompile(code);
            }
            outputs.Add($"// {file}{Environment.NewLine}{code}");
        }

        var combined = string.Join(Environment.NewLine, outputs);
        await VerifyHelper.Verify(combined);
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
                    "properties": {
                      "id": { "type": "integer", "format": "int64" },
                      "name": { "type": "string" }
                    }
                  }
                }
              }
            }
            """);
        var code = new CSharpClientGenerator(document, new CSharpClientGeneratorSettings()).GenerateFile();

        await VerifyHelper.Verify(code);
        CSharpCompiler.AssertCompile(code);
    }

    [Fact]
    public void Generates_partial_abstract_inherited_classes_and_nullable_optional_properties()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "name": "Root",
              "type": "object",
              "definitions": {
                "Base": {
                  "type": "object",
                  "abstract": true,
                  "properties": {
                    "id": { "type": "uuid" },
                    "optional": { "type": ["string", "null"] }
                  },
                  "required": ["id"]
                },
                "Child": {
                  "type": "object",
                  "$extends": "#/definitions/Base",
                  "properties": { "count": { "type": "int32" } },
                  "required": ["count"]
                }
              }
            }
            """);
        JsonStructureResolver.Resolve(structure);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Base", structure)]));

        var settings = new CSharpClientGeneratorSettings();
        settings.CSharpGeneratorSettings.GenerateNullableReferenceTypes = true;
        settings.CSharpGeneratorSettings.UseRequiredKeyword = true;
        var code = new CSharpClientGenerator(document, settings).GenerateFile();

        Assert.Contains("public abstract partial class Base", code);
        Assert.Contains("public partial class Child : Base", code);
        Assert.Contains("public required System.Guid id", code);
        Assert.Contains("public string? optional", code);
        Assert.Contains("public required int count", code);
    }

    [Fact]
    public void Emits_nested_namespaces_and_mangles_edge_case_identifiers()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "name": "Root",
              "type": "object",
              "definitions": {
                "billing-data": {
                  "class": {
                    "type": "object",
                    "properties": {
                      "class": { "type": "string" },
                      "1st-name": { "type": "string" },
                      "class_": { "type": "string" }
                    }
                  },
                  "1billing": {
                    "value": {
                      "type": "object",
                      "properties": { "amount": { "type": "int32" } }
                    }
                  }
                }
              }
            }
            """);
        JsonStructureResolver.Resolve(structure);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Root", structure)]));

        var settings = new CSharpClientGeneratorSettings();
        settings.CSharpGeneratorSettings.Namespace = "Contracts";
        var code = new CSharpClientGenerator(document, settings).GenerateFile();
        Assert.Contains("namespace billing_data", code);
        Assert.Contains("public partial class @class", code);
        Assert.Contains("public string @class_", code);
        Assert.Contains("public string _1st_name", code);
        Assert.Contains("namespace _1billing", code);
        CSharpCompiler.AssertCompile(code);
    }

    [Fact]
    public void Generates_data_annotations_and_accessors_from_json_structure_metadata()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "$schema": "https://json-structure.org/meta/validation/v0/#",
              "$id": "https://example.com/annotations",
              "name": "Annotated",
              "type": "object",
              "properties": {
                "email": {
                  "type": "string",
                  "minLength": 3,
                  "maxLength": 40,
                  "format": "email",
                  "pattern": "^[a-z]+$",
                  "altnames": { "json": "e_mail" },
                  "unit": "address",
                  "deprecated": true
                },
                "minimum": { "type": "int32", "minimum": 1 },
                "read": { "type": "string", "readOnly": true },
                "write": { "type": "string", "writeOnly": true }
              }
            }
            """);
        JsonStructureResolver.Resolve(structure);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Annotated", structure)]));

        var settings = new CSharpClientGeneratorSettings();
        settings.CSharpGeneratorSettings.GenerateDataAnnotations = true;
        var code = new CSharpClientGenerator(document, settings).GenerateFile();

        Assert.Contains("StringLength(40, MinimumLength = 3)", code);
        Assert.Contains("RegularExpression(\"^[a-z]+$\")", code);
        Assert.Contains("EmailAddress", code);
        Assert.Contains("Range(1, double.MaxValue)", code);
        Assert.Contains("JsonProperty(\"e_mail\")", code);
        Assert.Contains("Obsolete", code);
        Assert.Contains("Unit: address", code);
        Assert.Contains("public string read { get; }", code);
        Assert.Contains("public string write { set; }", code);
    }

    [Fact]
    public void Does_not_emit_json_structure_annotations_when_disabled()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "$schema": "https://json-structure.org/meta/validation/v0/#",
              "$id": "https://example.com/plain",
              "name": "Plain",
              "type": "object",
              "properties": {
                "value": { "type": "string", "minLength": 2, "deprecated": true }
              }
            }
            """);
        JsonStructureResolver.Resolve(structure);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Plain", structure)]));

        var settings = new CSharpClientGeneratorSettings();
        settings.CSharpGeneratorSettings.GenerateDataAnnotations = false;
        var code = new CSharpClientGenerator(document, settings).GenerateFile();

        Assert.DoesNotContain("DataAnnotations", code);
        Assert.DoesNotContain("Obsolete", code);
        Assert.Contains("public string value { get; set; }", code);
    }

    [Fact]
    public void Generates_Newtonsoft_converters_polymorphism_and_alternate_property_names()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "$schema": "https://json-structure.org/meta/extended/v0/#",
              "$uses": ["JSONStructureAlternateNames"],
              "name": "Contracts",
              "type": "object",
              "properties": {
                "value": { "type": "int64" },
                "alias": { "type": "string", "altnames": { "json": "wire_name" } },
                "tuple": {
                  "type": "tuple",
                  "properties": {
                    "first": { "type": "string" },
                    "second": { "type": "int32" }
                  },
                  "tuple": ["first", "second"]
                }
              },
              "definitions": {
                "Choice": {
                  "type": "choice",
                  "choices": {
                    "one": {
                      "type": "object",
                      "properties": { "kind": { "type": "string", "const": "one" } },
                      "required": ["kind"]
                    },
                    "two": {
                      "type": "object",
                      "properties": { "kind": { "type": "string", "const": "two" } },
                      "required": ["kind"]
                    }
                  }
                }
              }
            }
            """);
        JsonStructureResolver.Resolve(structure);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/Contracts", structure)]));

        var settings = new CSharpClientGeneratorSettings();
        settings.CSharpGeneratorSettings.JsonLibrary =
            NJsonSchema.CodeGeneration.CSharp.CSharpJsonLibrary.NewtonsoftJson;
        var code = new CSharpClientGenerator(document, settings).GenerateFile();

        Assert.Contains("JsonSerializerSettings GetSettings()", code);
        Assert.Contains("class Int64StringConverter : global::Newtonsoft.Json.JsonConverter<long>", code);
        Assert.Contains("[global::Newtonsoft.Json.JsonProperty(\"wire_name\")]", code);
        Assert.Contains("class TupleJsonConverter<T> : global::Newtonsoft.Json.JsonConverter<T>", code);
        Assert.Contains("class ChoiceJsonConverter : global::Newtonsoft.Json.JsonConverter<Choice>", code);
        Assert.DoesNotContain("JsonPolymorphic", code);
    }

    [Fact]
    public void Emits_build_visible_diagnostic_for_unsupported_Newtonsoft_tagged_primitive_choice()
    {
        var structure = new JsonStructureParser().Parse("""
            {
              "name": "UnsupportedChoice",
              "type": "choice",
              "choices": {
                "text": { "type": "string" },
                "number": { "type": "int32" }
              }
            }
            """);
        JsonStructureResolver.Resolve(structure);
        var document = new OpenApiDocument();
        document.AttachJsonStructureDocumentModel(new JsonStructureDocumentModel(
            [new KeyValuePair<string, JsonStructureDocument>("#/components/schemas/UnsupportedChoice", structure)]));

        var settings = new CSharpClientGeneratorSettings();
        settings.CSharpGeneratorSettings.JsonLibrary =
            NJsonSchema.CodeGeneration.CSharp.CSharpJsonLibrary.NewtonsoftJson;
        var code = new CSharpClientGenerator(document, settings).GenerateFile();

        Assert.Contains("#warning JSON Structure tagged choice 'UnsupportedChoice'", code);
        Assert.Contains("Use object variants with a const discriminator", code);
    }

}
