using NJsonSchema.CodeGeneration.CSharp;
using NSwag.JsonStructure.Model;
using Xunit;

namespace NSwag.CodeGeneration.CSharp.Tests
{
    public class JsonStructureCSharpTypeResolverTests
    {
        [Fact]
        public void Maps_json_structure_primitives_to_csharp_types()
        {
            var resolver = new JsonStructureCSharpTypeResolver(new CSharpGeneratorSettings());

            Assert.Equal("sbyte", resolver.Resolve(JsonStructureTypeKind.Int8));
            Assert.Equal("byte", resolver.Resolve(JsonStructureTypeKind.UInt8));
            Assert.Equal("System.Int128", resolver.Resolve(JsonStructureTypeKind.Int128));
            Assert.Equal("System.UInt128", resolver.Resolve(JsonStructureTypeKind.UInt128));
            Assert.Equal("decimal", resolver.Resolve(JsonStructureTypeKind.Decimal));
            Assert.Equal("byte[]", resolver.Resolve(JsonStructureTypeKind.Binary));
            Assert.Equal("System.Guid", resolver.Resolve(JsonStructureTypeKind.Uuid));
            Assert.Equal("System.Uri", resolver.Resolve(JsonStructureTypeKind.Uri));
            Assert.Equal("object", resolver.Resolve(JsonStructureTypeKind.Any));
        }

        [Fact]
        public void Uses_configured_temporal_and_collection_types()
        {
            var settings = new CSharpGeneratorSettings
            {
                DateType = "NodaTime.LocalDate",
                DateTimeType = "NodaTime.Instant",
                TimeType = "NodaTime.LocalTime",
                TimeSpanType = "NodaTime.Duration",
                ArrayType = "System.Collections.Generic.List",
                DictionaryType = "System.Collections.Generic.Dictionary"
            };
            var resolver = new JsonStructureCSharpTypeResolver(settings);

            Assert.Equal("NodaTime.LocalDate", resolver.Resolve(JsonStructureTypeKind.Date));
            Assert.Equal("NodaTime.Instant", resolver.Resolve(JsonStructureTypeKind.DateTime));
            Assert.Equal("NodaTime.LocalTime", resolver.Resolve(JsonStructureTypeKind.Time));
            Assert.Equal("NodaTime.Duration", resolver.Resolve(JsonStructureTypeKind.Duration));
        }
    }
}
