#pragma warning disable CS1591

using NJsonSchema;
using NSwag;
using NSwag.JsonStructure.OpenApi;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Resolution;

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
        private readonly Dictionary<JsonStructureCodeGenerationNamedType, JsonStructureCodeGenerationModel> _typeModels = new();
        private readonly Dictionary<JsonStructureCodeGenerationModel, IReadOnlyList<JsonStructureCodeGenerationNamedType>> _ownedTypes = new();
        private readonly IReadOnlyList<string> _reservedNames;

        public JsonStructureCodeGenerationContext(OpenApiDocument document, IEnumerable<string> reservedNames)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            _reservedNames = (reservedNames ?? Enumerable.Empty<string>()).ToList();
            var sideTable = document.GetJsonStructureDocumentModel();
            if (sideTable == null)
            {
                return;
            }

            var scopeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in sideTable.LiftedSchemas)
            {
                JsonStructureResolver.Resolve(pair.Value);
                var model = JsonStructureCodeGenerationModel.Create(pair.Value);
                if (string.IsNullOrWhiteSpace(model.ScopeName))
                {
                    throw new JsonStructureException(
                        "The JSON Structure schema resource at '" + pair.Key +
                        "' must declare a root 'name' before it can be aggregated.");
                }
                if (!scopeNames.Add(model.ScopeName))
                {
                    throw new JsonStructureException(
                        "The JSON Structure schema resource name '" + model.ScopeName +
                        "' is duplicated. Resource names must be unique when an OpenAPI document is aggregated.");
                }

                _models[pair.Value] = model;
                var ownedTypes = GetOwnedTypes(model).ToList();
                _ownedTypes[model] = ownedTypes;
                foreach (var type in ownedTypes)
                {
                    _typeModels[type] = model;
                }
            }

            var usedByNamespace = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var rootUsed = GetUsed(usedByNamespace, string.Empty);
            foreach (var reservedName in _reservedNames)
            {
                rootUsed.Add(Sanitize(reservedName));
            }

            foreach (var model in _models.Values)
            {
                var paths = new[] { (IReadOnlyList<string>)new[] { model.ScopeName } }
                    .Concat(model.Namespaces.Select(namespaceModel =>
                        (IReadOnlyList<string>)new[] { model.ScopeName }.Concat(namespaceModel.Path).ToList()))
                    .Where(path => path.Count > 0);
                foreach (var path in paths)
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

            foreach (var model in _models.Values)
            {
                foreach (var type in _ownedTypes[model])
                {
                    var scopedPath = model.GetScopedNamespacePath(type);
                    var namespaceKey = string.Join("\u001f", scopedPath);
                    var used = GetUsed(usedByNamespace, namespaceKey);
                    var candidate = Sanitize(type.Name);
                    var localName = Allocate(candidate, used);
                    _localNames[type] = localName;
                    var qualifiedParts = scopedPath.Select((_, index) =>
                    {
                        var path = scopedPath.Take(index + 1);
                        return _namespaceNames[string.Join("\u001f", path)];
                    }).ToList();
                    qualifiedParts.Add(localName);
                    _names[type] = string.Join(".", qualifiedParts);
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

        /// <summary>Gets ordinary OpenAPI names reserved in the aggregate's outer scope.</summary>
        public IReadOnlyList<string> ReservedNames => _reservedNames;

        public bool TryResolvePlaceholder(JsonSchema schema, out string name)
        {
            name = null;
            if (!TryGetPlaceholderModel(schema, out _, out var pointer) ||
                !_rootNames.TryGetValue(pointer, out name))
            {
                return false;
            }

            return true;
        }

        public bool TryGetPlaceholderModel(
            JsonSchema schema,
            out JsonStructureCodeGenerationModel model,
            out string correlationKey)
        {
            model = null;
            correlationKey = null;
            if (schema?.ExtensionData == null ||
                !schema.ExtensionData.TryGetValue(JsonStructureDocumentPreprocessor.CorrelationKeyExtensionName, out var value) ||
                value is not string pointer)
            {
                return false;
            }

            if (!Document.GetJsonStructureDocumentModel().LiftedSchemas.TryGetValue(pointer, out var document) ||
                !_models.TryGetValue(document, out model))
            {
                return false;
            }

            correlationKey = pointer;
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

        /// <summary>Gets the unsanitized aggregate namespace path rooted at the schema resource name.</summary>
        public IReadOnlyList<string> GetScopePath(JsonStructureCodeGenerationNamedType type)
        {
            return _typeModels.TryGetValue(type, out var model)
                ? model.GetScopedNamespacePath(type)
                : type.NamespacePath;
        }

        public IReadOnlyList<string> GetNamespacePath(JsonStructureCodeGenerationNamedType type)
        {
            var path = GetScopePath(type);
            return path.Select((_, index) =>
                _namespaceNames[string.Join("\u001f", path.Take(index + 1))]).ToList();
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

        private static IEnumerable<JsonStructureCodeGenerationNamedType> GetOwnedTypes(
            JsonStructureCodeGenerationModel model)
        {
            var seen = new HashSet<JsonStructureCodeGenerationNamedType>();
            foreach (var type in model.Types)
            {
                foreach (var owned in GetOwnedTypes(type, seen))
                {
                    yield return owned;
                }
            }
        }

        private static IEnumerable<JsonStructureCodeGenerationNamedType> GetOwnedTypes(
            JsonStructureCodeGenerationNamedType type,
            ISet<JsonStructureCodeGenerationNamedType> seen)
        {
            if (type == null || !seen.Add(type))
            {
                yield break;
            }

            yield return type;
            foreach (var reference in type.Properties.Select(property => property.Type)
                .Concat(type.Choices.Select(choice => choice.Type))
                .Concat(new[] { type.Items, type.Values }))
            {
                foreach (var owned in GetOwnedTypes(reference, seen))
                {
                    yield return owned;
                }
            }
        }

        private static IEnumerable<JsonStructureCodeGenerationNamedType> GetOwnedTypes(
            JsonStructureCodeGenerationTypeReference reference,
            ISet<JsonStructureCodeGenerationNamedType> seen)
        {
            if (reference == null)
            {
                yield break;
            }

            if (reference.InlineType != null && !string.IsNullOrWhiteSpace(reference.InlineType.Name))
            {
                foreach (var owned in GetOwnedTypes(reference.InlineType, seen))
                {
                    yield return owned;
                }
            }

            foreach (var nested in reference.Union
                .Concat(reference.TupleElements)
                .Concat(new[] { reference.ElementType, reference.ValueType }))
            {
                foreach (var owned in GetOwnedTypes(nested, seen))
                {
                    yield return owned;
                }
            }
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
