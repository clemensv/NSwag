using Newtonsoft.Json.Linq;
using NSwag.Generation;
using NSwag.JsonStructure.OpenApi;
using Xunit;

namespace NSwag.JsonStructure.Tests;

public class CliSurfaceSettingsTests
{
    [Fact]
    public void JsonStructureSettingsAllowDerivedDialect()
    {
        var settings = new JsonStructureSettings();
        settings.DerivedMetaSchemaAllowlist.Add("https://example.test/meta/#");

        var result = new JsonStructureDocumentPreprocessor(settings).Preprocess(
            JObject.Parse("""
            {
              "openapi": "3.1.0",
              "info": { "title": "test", "version": "1" },
              "jsonSchemaDialect": "https://example.test/meta/#",
              "paths": {}
            }
            """));

        Assert.NotNull(result);
    }

    [Fact]
    public void GeneratorUsesSelectedDialect()
    {
        var settings = new OpenApiDocumentGeneratorSettings
        {
            SchemaDialect = SchemaDialect.JsonStructure,
            JsonStructureDialect = JsonStructureDialect.Core
        };

        var structure = new JsonStructureSchemaGenerator(settings).Generate(typeof(string));

        Assert.Equal(JsonStructureDialects.CoreUri, structure.SchemaUri);
        Assert.Equal(JsonStructureDialects.CoreUri, (string)structure.SourceJson["$schema"]);
    }

}
