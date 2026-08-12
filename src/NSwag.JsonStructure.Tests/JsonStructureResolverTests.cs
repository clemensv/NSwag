using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;
using NSwag.JsonStructure.Resolution;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    /// <summary>
    /// Resolution turns the parsed tree into a navigable type graph. These tests cover the
    /// happy path against the real corpus plus the failure modes that must not pass silently.
    /// </summary>
    public class JsonStructureResolverTests
    {
        private const string CoreSchema = JsonStructureDialects.CoreUri;

        private static string SchemaDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas");

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

        [Theory]
        [MemberData(nameof(CorpusFiles))]
        public void Every_corpus_document_resolves(string fileName)
        {
            var document = ParseFile(fileName);

            var resolved = JsonStructureResolver.Resolve(document);

            Assert.Same(document, resolved);
        }

        [Fact]
        public void Inheritance_is_linked_to_the_base_type()
        {
            var document = JsonStructureResolver.Resolve(ParseFile("inheritance.struct.json"));

            var derived = document.GetAllTypes().First(t => t.Schema.Extends != null);

            Assert.NotNull(derived.Schema.ResolvedExtends);
            Assert.NotSame(derived, derived.Schema.ResolvedExtends);
        }

        [Fact]
        public void Resolves_a_reference_into_a_nested_namespace()
        {
            var document = Parse("""
                {
                  "definitions": {
                    "Shipping": {
                      "Address": {
                        "name": "Address",
                        "type": "object",
                        "properties": { "city": { "type": "string" } }
                      }
                    },
                    "Order": {
                      "name": "Order",
                      "type": "object",
                      "properties": {
                        "shipTo": { "type": { "$ref": "#/definitions/Shipping/Address" } }
                      }
                    }
                  }
                }
                """);

            JsonStructureResolver.Resolve(document);

            var order = document.GetAllTypes().Single(t => t.Name == "Order");
            var target = order.Schema.Properties.Single(p => p.Name == "shipTo").Schema.ResolvedReference;

            Assert.NotNull(target);
            Assert.Equal("Address", target.Name);

            // Namespaces are the reason JSON Structure can express this at all; the qualified
            // name is what downstream code generation needs.
            Assert.Equal("Shipping.Address", target.FullName);
        }

        [Fact]
        public void Dangling_reference_is_rejected()
        {
            var document = Parse("""
                {
                  "definitions": {
                    "Order": {
                      "name": "Order",
                      "type": "object",
                      "properties": { "a": { "type": { "$ref": "#/definitions/Nope" } } }
                    }
                  }
                }
                """);

            var exception = Assert.Throws<JsonStructureException>(() => JsonStructureResolver.Resolve(document));
            Assert.Contains("#/definitions/Nope", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Dangling_extends_is_rejected()
        {
            var document = Parse("""
                {
                  "definitions": {
                    "Derived": { "name": "Derived", "type": "object", "$extends": "#/definitions/Missing" }
                  }
                }
                """);

            Assert.Throws<JsonStructureException>(() => JsonStructureResolver.Resolve(document));
        }

        [Fact]
        public void Self_extension_is_rejected()
        {
            var document = Parse("""
                {
                  "definitions": {
                    "Loop": { "name": "Loop", "type": "object", "$extends": "#/definitions/Loop" }
                  }
                }
                """);

            Assert.Throws<JsonStructureException>(() => JsonStructureResolver.Resolve(document));
        }

        [Fact]
        public void Cyclic_inheritance_is_rejected()
        {
            var document = Parse("""
                {
                  "definitions": {
                    "A": { "name": "A", "type": "object", "$extends": "#/definitions/B" },
                    "B": { "name": "B", "type": "object", "$extends": "#/definitions/A" }
                  }
                }
                """);

            var exception = Assert.Throws<JsonStructureException>(() => JsonStructureResolver.Resolve(document));
            Assert.Contains("cyclic", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Recursive_type_reference_is_allowed()
        {
            // A type referring to itself through a property is legal and must not be mistaken
            // for an inheritance cycle.
            var document = Parse("""
                {
                  "definitions": {
                    "Node": {
                      "name": "Node",
                      "type": "object",
                      "properties": {
                        "children": { "type": "array", "items": { "type": { "$ref": "#/definitions/Node" } } }
                      }
                    }
                  }
                }
                """);

            JsonStructureResolver.Resolve(document);

            var node = document.GetAllTypes().Single(t => t.Name == "Node");
            var itemRef = node.Schema.Properties.Single().Schema.Items.ResolvedReference;

            Assert.Same(node, itemRef);
        }

        [Fact]
        public void Dangling_root_pointer_is_rejected()
        {
            var document = Parse("""
                {
                  "$root": "#/definitions/Missing",
                  "definitions": { "Real": { "name": "Real", "type": "object" } }
                }
                """);

            Assert.Throws<JsonStructureException>(() => JsonStructureResolver.Resolve(document));
        }

        [Fact]
        public void Root_pointer_resolves_to_a_declared_type()
        {
            var document = Parse("""
                {
                  "$root": "#/definitions/Real",
                  "definitions": { "Real": { "name": "Real", "type": "object" } }
                }
                """);

            JsonStructureResolver.Resolve(document);

            Assert.Equal("Real", JsonStructureResolver.ResolvePointer(document, document.RootPointer).Name);
        }

        private static JsonStructureDocument ParseFile(string fileName)
        {
            return new JsonStructureParser().Parse(File.ReadAllText(Path.Combine(SchemaDirectory, fileName)));
        }

        private static JsonStructureDocument Parse(string body)
        {
            var envelope = $$"""
                {
                  "$schema": "{{CoreSchema}}",
                  "$id": "https://example.com/test",
                  "name": "Test",
                """;

            return new JsonStructureParser().Parse(envelope + body.TrimStart().TrimStart('{'));
        }
    }
}
