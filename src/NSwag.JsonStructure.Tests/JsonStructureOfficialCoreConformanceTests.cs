using NSwag.JsonStructure.Parsing;
using NSwag.JsonStructure.Resolution;
using NSwag.JsonStructure.Validation;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>
    /// Runs the NSwag parser, resolver, and validator over the official Core examples imported
    /// from json-structure/primer-and-samples/samples/core.
    /// </summary>
    public class JsonStructureOfficialCoreConformanceTests
    {
        private static string PositiveDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas", "OfficialCore", "positive");
        private static string NegativeDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas", "OfficialCore", "negative");

        public static TheoryData<string> OfficialSchemas
        {
            get
            {
                var data = new TheoryData<string>();
                foreach (var file in Directory.GetFiles(PositiveDirectory, "*.struct.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    data.Add(Path.GetFileName(file));
                }

                return data;
            }
        }

        [Fact]
        public void Official_core_corpus_is_present()
        {
            Assert.Single(Directory.GetFiles(PositiveDirectory, "*.struct.json"));
            Assert.Equal(12, Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Schemas", "OfficialCore", "imported"), "*.struct.json").Length);
            Assert.Equal(34, Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Schemas", "OfficialCore", "imported", "samples"), "*.json").Length);
        }

        [Theory]
        [MemberData(nameof(OfficialSchemas))]
        public void Official_core_schema_parses_resolves_and_validates_without_errors(string fileName)
        {
            var document = new JsonStructureParser().Parse(File.ReadAllText(Path.Combine(PositiveDirectory, fileName)));

            Assert.Same(document, JsonStructureResolver.Resolve(document));
            var diagnostics = JsonStructureValidator.Validate(document);

            Assert.DoesNotContain(diagnostics, d => d.Severity == JsonStructureDiagnosticSeverity.Error);
        }

        [Fact]
        public void Official_core_positive_corpus_covers_every_compound_type()
        {
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(PositiveDirectory, "*.struct.json"))
            {
                var document = JsonStructureResolver.Resolve(new JsonStructureParser().Parse(File.ReadAllText(file)));
                foreach (var type in document.GetAllTypes())
                {
                    kinds.Add(type.Schema.Kind.ToString());
                }
            }

            Assert.Contains("Object", kinds);
            Assert.Contains("Array", kinds);
            Assert.Contains("Set", kinds);
            Assert.Contains("Map", kinds);
            Assert.Contains("Tuple", kinds);
            Assert.Contains("Choice", kinds);
        }

        [Fact]
        public void Official_core_positive_corpus_covers_every_primitive_declared_by_meta_schema()
        {
            var expected = new[]
            {
                "string", "number", "integer", "boolean", "null", "binary", "int32", "uint32",
                "int64", "uint64", "int128", "uint128", "float", "double", "decimal", "date",
                "datetime", "time", "duration", "uuid", "uri", "jsonpointer"
            };
            var source = File.ReadAllText(Path.Combine(PositiveDirectory, "core-v0-all-types.struct.json"));

            foreach (var type in expected)
            {
                Assert.Contains("\"type\": \"" + type + "\"", source, StringComparison.Ordinal);
            }
        }

        public static TheoryData<string> NegativeSchemas
        {
            get
            {
                var data = new TheoryData<string>();
                foreach (var file in Directory.GetFiles(NegativeDirectory, "*.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    data.Add(Path.GetFileName(file));
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(NegativeSchemas))]
        public void Official_core_negative_cases_are_rejected(string fileName)
        {
            var json = File.ReadAllText(Path.Combine(NegativeDirectory, fileName));
            if (fileName.StartsWith("parser-", StringComparison.Ordinal))
            {
                Assert.Throws<JsonStructureException>(() => new JsonStructureParser().Parse(json));
                return;
            }

            var document = new JsonStructureParser().Parse(json);
            JsonStructureResolver.Resolve(document);
            Assert.Contains(JsonStructureValidator.Validate(document), d => d.Severity == JsonStructureDiagnosticSeverity.Error);
        }
    }
}
