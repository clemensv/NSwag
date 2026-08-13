using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>
    /// Parses the JSON Structure conformance corpus. Every schema in <c>Schemas</c> must parse,
    /// which keeps the parser honest against real-world documents rather than hand-written snippets.
    /// </summary>
    public class JsonStructureParserCorpusTests
    {
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
        public void Every_corpus_schema_parses(string fileName)
        {
            var json = File.ReadAllText(Path.Combine(SchemaDirectory, fileName));

            var document = new JsonStructureParser().Parse(json);

            Assert.NotEqual(JsonStructureDialect.None, document.Dialect);
            Assert.NotNull(document.SourceJson);
            Assert.True(
                document.RootSchema != null || document.RootPointer != null || document.GetAllTypes().Any(),
                "The document declared neither a root type nor any definitions.");
        }

        [Fact]
        public void Corpus_is_not_empty()
        {
            Assert.NotEmpty(Directory.GetFiles(SchemaDirectory, "*.struct.json"));
        }

        [Fact]
        public void Parses_primitive_types_with_full_precision()
        {
            var json = File.ReadAllText(Path.Combine(SchemaDirectory, "numeric-types.struct.json"));

            var document = new JsonStructureParser().Parse(json);
            var kinds = CollectPropertyKinds(document);

            // The whole point of the dialect: width- and sign-precise numerics survive parsing
            // instead of collapsing into JSON Schema's "integer"/"number".
            Assert.Contains(JsonStructureTypeKind.Int8, kinds);
            Assert.Contains(JsonStructureTypeKind.Int16, kinds);
            Assert.Contains(JsonStructureTypeKind.UInt32, kinds);
            Assert.Contains(JsonStructureTypeKind.UInt64, kinds);
            Assert.Contains(JsonStructureTypeKind.Decimal, kinds);
            Assert.Contains(JsonStructureTypeKind.Float, kinds);
            Assert.Contains(JsonStructureTypeKind.Double, kinds);
        }

        [Fact]
        public void Parses_temporal_types()
        {
            var json = File.ReadAllText(Path.Combine(SchemaDirectory, "temporal-types.struct.json"));

            var document = new JsonStructureParser().Parse(json);
            var kinds = CollectPropertyKinds(document);

            Assert.Contains(JsonStructureTypeKind.Date, kinds);
            Assert.Contains(JsonStructureTypeKind.DateTime, kinds);
        }

        [Fact]
        public void Parses_collections_as_distinct_kinds()
        {
            var json = File.ReadAllText(Path.Combine(SchemaDirectory, "collections.struct.json"));

            var document = new JsonStructureParser().Parse(json);
            var kinds = CollectPropertyKinds(document);

            // set and map must not collapse into array/object the way they would in JSON Schema.
            Assert.Contains(JsonStructureTypeKind.Array, kinds);
            Assert.Contains(JsonStructureTypeKind.Set, kinds);
            Assert.Contains(JsonStructureTypeKind.Map, kinds);
        }

        [Fact]
        public void Parses_tuple_element_order()
        {
            var json = File.ReadAllText(Path.Combine(SchemaDirectory, "tuple-test.struct.json"));

            var document = new JsonStructureParser().Parse(json);
            var tuple = AllSchemas(document).First(s => s.Kind == JsonStructureTypeKind.Tuple);

            Assert.NotEmpty(tuple.TupleOrder);
            Assert.Equal(tuple.Properties.Count, tuple.TupleOrder.Count);
            Assert.All(tuple.Properties, p => Assert.True(p.IsRequired));
        }

        [Fact]
        public void Parses_choices_and_inheritance()
        {
            var choiceJson = File.ReadAllText(Path.Combine(SchemaDirectory, "choice-types.struct.json"));
            var choiceDocument = new JsonStructureParser().Parse(choiceJson);
            var choice = AllSchemas(choiceDocument).First(s => s.Kind == JsonStructureTypeKind.Choice);

            Assert.NotEmpty(choice.Choices);

            var inheritanceJson = File.ReadAllText(Path.Combine(SchemaDirectory, "inheritance.struct.json"));
            var inheritanceDocument = new JsonStructureParser().Parse(inheritanceJson);

            Assert.Contains(AllSchemas(inheritanceDocument), s => s.Extends != null);
        }

        private static IEnumerable<JsonStructureSchema> AllSchemas(JsonStructureDocument document)
        {
            var roots = document.GetAllTypes().Select(t => t.Schema).ToList();
            if (document.RootSchema != null)
            {
                roots.Add(document.RootSchema);
            }

            return roots.SelectMany(Descend).Distinct();
        }

        private static IEnumerable<JsonStructureSchema> Descend(JsonStructureSchema schema)
        {
            if (schema == null)
            {
                yield break;
            }

            yield return schema;

            foreach (var property in schema.Properties)
            {
                foreach (var nested in Descend(property.Schema))
                {
                    yield return nested;
                }
            }

            foreach (var choice in schema.Choices)
            {
                foreach (var nested in Descend(choice.Schema))
                {
                    yield return nested;
                }
            }

            foreach (var nested in Descend(schema.Items))
            {
                yield return nested;
            }

            foreach (var nested in Descend(schema.Values))
            {
                yield return nested;
            }

            foreach (var nested in Descend(schema.AdditionalProperties))
            {
                yield return nested;
            }
        }

        private static HashSet<JsonStructureTypeKind> CollectPropertyKinds(JsonStructureDocument document)
        {
            return [.. AllSchemas(document).Select(s => s.Kind)];
        }
    }
}
