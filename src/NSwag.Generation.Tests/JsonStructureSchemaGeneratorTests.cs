#nullable enable
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.OpenApi;
using Namotion.Reflection;
using System.Text.Json.Serialization;
using NJsonSchema.Converters;
using Xunit;

namespace NSwag.Generation.Tests;

public class JsonStructureSchemaGeneratorTests
{
    private sealed record Sample(
        int Count,
        long Total,
        decimal Amount,
        DateTime Created,
        TimeSpan Elapsed,
        Guid Id,
        byte[] Payload,
        Dictionary<string, int> Values,
        HashSet<Guid> Tags,
        (int, string) Pair,
        string? Optional);

    private abstract class Animal
    {
        public string? Name { get; set; }
    }

    [JsonDerivedType(typeof(Dog), "dog")]
    [JsonDerivedType(typeof(Cat), "cat")]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    private abstract class Pet : Animal
    {
    }

    private sealed class Dog : Pet
    {
        public bool Barks { get; set; }
    }

    private sealed class Cat : Pet
    {
        public int Lives { get; set; }
    }

    private sealed class Household
    {
        public Pet? Pet { get; set; }
    }

    [JsonInheritanceConverter(typeof(NJsonEntity), "kind")]
    [JsonInheritance("invoice", typeof(Invoice))]
    [JsonInheritance("receipt", typeof(Receipt))]
    private abstract class NJsonEntity
    {
    }

    private sealed class Invoice : NJsonEntity
    {
    }

    private sealed class Receipt : NJsonEntity
    {
    }

    private sealed class NJsonEnvelope
    {
        public NJsonEntity? Entity { get; set; }
    }

    [Fact]
    public void Generates_native_types_and_collections()
    {
        var document = new JsonStructureSchemaGenerator(new OpenApiDocumentGeneratorSettings()).Generate(typeof(Sample));
        var type = document.GetAllTypes().Single();

        Assert.Equal("NSwag.Generation.Tests.Sample", type.FullName);
        Assert.Equal("#/definitions/NSwag/Generation/Tests/Sample", document.RootPointer);
        Assert.Equal(JsonStructureTypeKind.Int32, type.Schema.Properties.Single(p => p.Name == "Count").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Int64, type.Schema.Properties.Single(p => p.Name == "Total").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Decimal, type.Schema.Properties.Single(p => p.Name == "Amount").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.DateTime, type.Schema.Properties.Single(p => p.Name == "Created").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Duration, type.Schema.Properties.Single(p => p.Name == "Elapsed").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Uuid, type.Schema.Properties.Single(p => p.Name == "Id").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Binary, type.Schema.Properties.Single(p => p.Name == "Payload").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Map, type.Schema.Properties.Single(p => p.Name == "Values").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Set, type.Schema.Properties.Single(p => p.Name == "Tags").Schema.Kind);
        Assert.Equal(JsonStructureTypeKind.Tuple, type.Schema.Properties.Single(p => p.Name == "Pair").Schema.Kind);
    }

    [Fact]
    public void Nullable_properties_are_optional_and_use_a_null_union()
    {
        var document = new JsonStructureSchemaGenerator(new OpenApiDocumentGeneratorSettings()).Generate(typeof(Sample));
        var property = document.GetAllTypes().Single().Schema.Properties.Single(p => p.Name == "Optional");

        Assert.False(property.IsRequired);
        Assert.True(property.Schema.IsUnion);
        Assert.Contains(property.Schema.Union, member => member.Kind == JsonStructureTypeKind.Null);
    }

    [Fact]
    public void Generates_inheritance_and_stj_discriminated_choices()
    {
        var document = new JsonStructureSchemaGenerator(new OpenApiDocumentGeneratorSettings()).Generate(typeof(Household));
        var types = document.GetAllTypes().ToDictionary(type => type.Name);

        Assert.True(types[nameof(Animal)].Schema.IsAbstract);
        Assert.Equal(types[nameof(Animal)].Pointer, types[nameof(Pet)].Schema.Extends);
        Assert.Equal(types[nameof(Pet)].Pointer, types[nameof(Dog)].Schema.Extends);
        Assert.Equal(types[nameof(Pet)].Pointer, types[nameof(Cat)].Schema.Extends);

        var propertySchema = types[nameof(Household)].Schema.Properties.Single(property => property.Name == nameof(Household.Pet)).Schema;
        var choice = propertySchema.IsUnion
            ? propertySchema.Union.Single(member => !member.IsNull).InlineSchema
            : propertySchema;
        Assert.NotNull(choice);
        Assert.Equal(JsonStructureTypeKind.Choice, choice!.Kind);
        Assert.Equal(types[nameof(Pet)].Pointer, choice.Extends);
        Assert.Equal("kind", choice.Selector);
        Assert.Equal("dog,cat", string.Join(",", choice.Choices.Select(variant => variant.Name)));

        var resolved = NSwag.JsonStructure.Resolution.JsonStructureResolver.Resolve(document);
        var model = NSwag.JsonStructure.CodeGeneration.JsonStructureCodeGenerationModel.Create(resolved);
        var pet = model.Types.Single(type => type.Name == nameof(Pet));
        Assert.Equal(nameof(Animal), pet.BaseType!.Name);
    }

    [Fact]
    public void Generates_njsonschema_discriminated_choices()
    {
        var document = new JsonStructureSchemaGenerator(new OpenApiDocumentGeneratorSettings()).Generate(typeof(NJsonEnvelope));
        var envelope = document.GetAllTypes().Single(type => type.Name == nameof(NJsonEnvelope));
        var propertySchema = envelope.Schema.Properties.Single(property => property.Name == nameof(NJsonEnvelope.Entity)).Schema;
        var choice = propertySchema.Union.Single(member => !member.IsNull).InlineSchema;

        Assert.Equal("kind", choice!.Selector);
        Assert.Equal("invoice,receipt", string.Join(",", choice.Choices.Select(variant => variant.Name)));
    }

    [Fact]
    public void Schema_dialect_registers_generated_schema_with_document()
    {
        var settings = new OpenApiDocumentGeneratorSettings
        {
            SchemaDialect = SchemaDialect.JsonStructure
        };
        var openApi = new OpenApiDocument();
        var schema = settings.GenerateSchema(openApi, typeof(Sample).ToContextualType(), false);

        Assert.True(schema!.ExtensionData!.ContainsKey("x-json-structure"));
        var model = openApi.GetJsonStructureDocumentModel();
        Assert.NotNull(model);
        Assert.Single(model!.LiftedSchemas);
    }
}
