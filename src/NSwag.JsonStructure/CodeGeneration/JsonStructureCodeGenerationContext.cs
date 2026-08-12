#pragma warning disable CS1591

using NJsonSchema;
using NSwag;
using NSwag.JsonStructure.OpenApi;
using NSwag.JsonStructure.Model;

namespace NSwag.JsonStructure.CodeGeneration
{
    /// <summary>Shared naming and placeholder resolution seam for language generators.</summary>
    public sealed class JsonStructureCodeGenerationContext
    {
        private readonly Dictionary<JsonStructureDocument, JsonStructureCodeGenerationModel> _models = new();
        private readonly Dictionary<JsonStructureCodeGenerationNamedType, string> _names = new();
        private readonly Dictionary<JsonStructureCodeGenerationNamedType, string> _localNames = new();
        private readonly Dictionary<string, string> _namespaceNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _rootNames = new(StringComparer.Ordinal);

        public JsonStructureCodeGenerationContext(OpenApiDocument document, IEnumerable<string> reservedNames)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            var sideTable = document.GetJsonStructureDocumentModel();
            if (sideTable == null)
            {
                return;
            }

            foreach (var pair in sideTable.LiftedSchemas)
            {
                var model = JsonStructureCodeGenerationModel.Create(pair.Value);
                _models[pair.Value] = model;
            }

            var usedByNamespace = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var model in _models.Values)
            {
                foreach (var path in model.Namespaces.Select(namespaceModel => namespaceModel.Path)
                    .Where(path => path.Count > 0))
                {
                    var parent = string.Join("\u001f", path.Take(path.Count - 1));
                    var key = string.Join("\u001f", path);
                    if (!_namespaceNames.ContainsKey(key))
                    {
                        var used = GetUsed(usedByNamespace, parent);
                        _namespaceNames[key] = Allocate(Sanitize(path[path.Count - 1]), used);
                    }
                }
            }

            var rootUsed = GetUsed(usedByNamespace, string.Empty);
            foreach (var reservedName in reservedNames ?? Enumerable.Empty<string>())
            {
                rootUsed.Add(Sanitize(reservedName));
            }

            foreach (var model in _models.Values)
            {
                foreach (var type in model.Types)
                {
                    var namespaceKey = string.Join("\u001f", type.NamespacePath);
                    var used = GetUsed(usedByNamespace, namespaceKey);
                    var candidate = Sanitize(type.Name);
                    if (type.NamespacePath.Count == 0 && used.Contains(candidate))
                    {
                        candidate = "JsonStructure_" + candidate;
                    }

                    var localName = Allocate(candidate, used);
                    _localNames[type] = localName;
                    if (type.NamespacePath.Count == 0)
                    {
                        _names[type] = localName;
                    }
                    else
                    {
                        var qualifiedParts = type.NamespacePath.Select((_, index) =>
                        {
                            var path = type.NamespacePath.Take(index + 1);
                            return _namespaceNames[string.Join("\u001f", path)];
                        }).ToList();
                        qualifiedParts.Add(localName);
                        _names[type] = string.Join(".", qualifiedParts);
                    }
                }
            }

            foreach (var pair in sideTable.LiftedSchemas)
            {
                var model = _models[pair.Value];
                if (model.RootType != null)
                {
                    _rootNames[pair.Key] = GetName(model.RootType);
                }
            }
        }

        public OpenApiDocument Document { get; }

        public bool TryResolvePlaceholder(JsonSchema schema, out string name)
        {
            name = null;
            if (schema?.ExtensionData == null ||
                !schema.ExtensionData.TryGetValue(JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName, out var value) ||
                value is not string pointer ||
                !_rootNames.TryGetValue(pointer, out name))
            {
                return false;
            }

            return true;
        }

        public IEnumerable<(JsonStructureCodeGenerationModel Model, IReadOnlyList<JsonStructureCodeGenerationNamedType> Types)> Models =>
            _models.Values.Select(model => (model, model.Types));

        public string GetName(JsonStructureCodeGenerationNamedType type)
        {
            return _names.TryGetValue(type, out var name) ? name : Sanitize(type.Name);
        }

        public string GetLocalName(JsonStructureCodeGenerationNamedType type)
        {
            return _localNames.TryGetValue(type, out var name) ? name : Sanitize(type.Name);
        }

        public IReadOnlyList<string> GetNamespacePath(JsonStructureCodeGenerationNamedType type)
        {
            return type.NamespacePath.Select((_, index) =>
                _namespaceNames[string.Join("\u001f", type.NamespacePath.Take(index + 1))]).ToList();
        }

        private static HashSet<string> GetUsed(Dictionary<string, HashSet<string>> usedByNamespace, string key)
        {
            if (!usedByNamespace.TryGetValue(key, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByNamespace[key] = used;
            }

            return used;
        }

        private static string Allocate(string baseName, HashSet<string> used)
        {
            var name = baseName;
            var index = 2;
            while (!used.Add(name))
            {
                name = baseName + "_" + index++;
            }

            return name;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "JsonStructureType";
            }

            var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            var result = new string(chars);
            if (char.IsDigit(result[0]))
            {
                result = "_" + result;
            }
            return CSharpKeywords.Contains(result) ? "@" + result : result;
        }

        private static readonly HashSet<string> CSharpKeywords =
        [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
            "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
            "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
            "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
            "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "file",
            "required", "scoped"
        ];
    }
}
