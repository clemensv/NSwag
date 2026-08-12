using NSwag.JsonStructure.Import;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;
using NSwag.JsonStructure.Resolution;
using Xunit;

namespace NSwag.JsonStructure.Tests
{
    public class JsonStructureImportTests
    {
        private const string ExtendedSchema = JsonStructureDialects.ExtendedUri;

        [Fact]
        public void Import_disabled_by_default_rejects_remote_import_without_fetching()
        {
            var document = Parse("""
                {
                  "$import": "https://schemas.example/people.json",
                  "definitions": { }
                }
                """);

            var exception = Assert.Throws<JsonStructureException>(() => JsonStructureResolver.Resolve(document));

            Assert.Contains("offline by default", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AllowNetwork", exception.Message, StringComparison.Ordinal);
            Assert.Equal("#/$import", exception.Pointer);
        }

        [Fact]
        public void Import_and_importdefs_copy_definitions_and_rewrite_references()
        {
            var loader = new InMemoryLoader();
            loader.Add("https://schemas.example/people.json", """
                {
                  "$schema": "https://json-structure.org/meta/extended/v0/#",
                  "$id": "https://schemas.example/people.json",
                  "name": "Person",
                  "type": "object",
                  "properties": {
                    "address": { "type": { "$ref": "#/definitions/Address" } }
                  },
                  "definitions": {
                    "Address": { "type": "object", "properties": { "city": { "type": "string" } } }
                  }
                }
                """);
            loader.Add("https://schemas.example/library.json", """
                {
                  "$schema": "https://json-structure.org/meta/extended/v0/#",
                  "$id": "https://schemas.example/library.json",
                  "name": "NotImported",
                  "type": "object",
                  "definitions": {
                    "Address": { "type": "object", "properties": { "street": { "type": "string" } } }
                  }
                }
                """);

            var document = Parse("""
                {
                  "definitions": {
                    "People": { "$import": "https://schemas.example/people.json" },
                    "Lib": { "$importdefs": "https://schemas.example/library.json" },
                    "Order": {
                      "type": "object",
                      "properties": {
                        "person": { "type": { "$ref": "#/definitions/People/Person" } },
                        "shipTo": { "type": { "$ref": "#/definitions/Lib/Address" } }
                      }
                    }
                  }
                }
                """);

            var resolved = CreateResolver(loader).Resolve(document);

            Assert.True(resolved.Definitions.TryGetNamespace("People", out var people));
            Assert.True(people.TryGetType("Person", out var person));
            Assert.True(people.TryGetType("Address", out var address));
            Assert.Same(address, person.Schema.Properties.Single(p => p.Name == "address").Schema.ResolvedReference);

            Assert.True(resolved.Definitions.TryGetNamespace("Lib", out var lib));
            Assert.True(lib.TryGetType("Address", out _));
            Assert.False(lib.TryGetType("NotImported", out _));

            var order = resolved.GetAllTypes().Single(t => t.Name == "Order");
            Assert.Equal("People.Person", order.Schema.Properties.Single(p => p.Name == "person").Schema.ResolvedReference.FullName);
            Assert.Equal("Lib.Address", order.Schema.Properties.Single(p => p.Name == "shipTo").Schema.ResolvedReference.FullName);
        }

        [Fact]
        public void Non_allowlisted_host_is_rejected_before_loader_runs()
        {
            var loader = new InMemoryLoader();
            var document = Parse("""{ "$import": "https://evil.example/schema.json" }""");
            var policy = new JsonStructureImportPolicy { AllowNetwork = true };

            var exception = Assert.Throws<JsonStructureException>(
                () => new JsonStructureResolver(policy, loader).Resolve(document));

            Assert.Contains("not allowlisted", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, loader.TotalCalls);
        }

        [Fact]
        public void Oversize_response_is_rejected()
        {
            var loader = new InMemoryLoader();
            loader.Add("https://schemas.example/huge.json", new string(' ', 64));
            var document = Parse("""{ "$import": "https://schemas.example/huge.json" }""");
            var policy = AllowedNetworkPolicy();
            policy.MaxDocumentSize = 8;

            var exception = Assert.Throws<JsonStructureException>(
                () => new JsonStructureResolver(policy, loader).Resolve(document));

            Assert.Contains("maximum size", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Import_cycle_is_rejected_cleanly()
        {
            var loader = new InMemoryLoader();
            loader.Add("https://schemas.example/b.json", """
                {
                  "$schema": "https://json-structure.org/meta/extended/v0/#",
                  "$id": "https://schemas.example/b.json",
                  "$import": "https://schemas.example/a.json",
                  "name": "B"
                }
                """);
            var document = Parse("""
                {
                  "$id": "https://schemas.example/a.json",
                  "$import": "https://schemas.example/b.json"
                }
                """);

            var exception = Assert.Throws<JsonStructureException>(() => CreateResolver(loader).Resolve(document));

            Assert.Contains("cyclic", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Same_uri_is_loaded_once_per_resolution_run()
        {
            var loader = new InMemoryLoader();
            loader.Add("https://schemas.example/common.json", """
                {
                  "$schema": "https://json-structure.org/meta/extended/v0/#",
                  "$id": "https://schemas.example/common.json",
                  "definitions": { "Common": { "type": "object" } }
                }
                """);
            var document = Parse("""
                {
                  "definitions": {
                    "A": { "$importdefs": "https://schemas.example/common.json" },
                    "B": { "$importdefs": "https://schemas.example/common.json" }
                  }
                }
                """);

            CreateResolver(loader).Resolve(document);

            Assert.Equal(1, loader.GetCallCount("https://schemas.example/common.json"));
            Assert.True(document.Definitions.TryGetNamespace("A", out var a));
            Assert.True(document.Definitions.TryGetNamespace("B", out var b));
            Assert.True(a.TryGetType("Common", out _));
            Assert.True(b.TryGetType("Common", out _));
        }

        [Fact]
        public void Relative_path_traversal_outside_base_directory_is_rejected()
        {
            var baseDirectory = Path.Combine(AppContext.BaseDirectory, "json-structure-import-base");
            var document = Parse("""{ "$importdefs": "..\\secret.json" }""");
            var policy = new JsonStructureImportPolicy
            {
                AllowFileSystem = true,
                BaseDirectory = baseDirectory
            };

            var exception = Assert.Throws<JsonStructureException>(
                () => new JsonStructureResolver(policy, new InMemoryLoader()).Resolve(document));

            Assert.Contains("escapes the configured BaseDirectory", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Loader_timeout_is_reported_as_import_timeout()
        {
            var loader = new InMemoryLoader { ThrowOperationCanceled = true };
            var document = Parse("""{ "$import": "https://schemas.example/slow.json" }""");
            var policy = AllowedNetworkPolicy();
            policy.Timeout = TimeSpan.FromMilliseconds(1);

            var exception = Assert.Throws<JsonStructureException>(
                () => new JsonStructureResolver(policy, loader).Resolve(document));

            Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static JsonStructureResolver CreateResolver(InMemoryLoader loader)
        {
            return new JsonStructureResolver(AllowedNetworkPolicy(), loader);
        }

        private static JsonStructureImportPolicy AllowedNetworkPolicy()
        {
            var policy = new JsonStructureImportPolicy { AllowNetwork = true };
            policy.AllowedHosts.Add("schemas.example");
            return policy;
        }

        private static JsonStructureDocument Parse(string body)
        {
            var envelope = $$"""
                {
                  "$schema": "{{ExtendedSchema}}",
                  "$id": "https://schemas.example/test.json",
                  "name": "Test",
                """;

            return new JsonStructureParser().Parse(envelope + body.TrimStart().TrimStart('{'));
        }

        private sealed class InMemoryLoader : IJsonStructureDocumentLoader
        {
            private readonly Dictionary<Uri, string> _documents = new Dictionary<Uri, string>();
            private readonly Dictionary<Uri, int> _calls = new Dictionary<Uri, int>();

            public bool ThrowOperationCanceled { get; set; }

            public int TotalCalls => _calls.Values.Sum();

            public void Add(string uri, string json)
            {
                _documents[new Uri(uri)] = json;
            }

            public int GetCallCount(string uri)
            {
                return _calls.TryGetValue(new Uri(uri), out var count) ? count : 0;
            }

            public string Load(Uri documentUri, JsonStructureImportPolicy policy, CancellationToken cancellationToken)
            {
                _calls[documentUri] = GetCallCount(documentUri.AbsoluteUri) + 1;

                if (ThrowOperationCanceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (_documents.TryGetValue(documentUri, out var json))
                {
                    return json;
                }

                throw new InvalidOperationException("No test document registered for " + documentUri + ".");
            }
        }
    }
}
