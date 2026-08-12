//-----------------------------------------------------------------------
// <copyright file="JsonStructureResolver.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.Import;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Parsing;

namespace NSwag.JsonStructure.Resolution
{
    /// <summary>
    /// Links a parsed <see cref="JsonStructureDocument"/> into a navigable type graph by resolving
    /// imports, <c>$ref</c>, <c>$extends</c> and <c>$root</c> pointers against the document's namespace tree.
    /// </summary>
    /// <remarks>
    /// Parsing and resolution are separate passes because a <c>$ref</c> may point forward to a type
    /// that has not been read yet. Resolution is therefore only possible once the whole document is
    /// in memory.
    /// </remarks>
    public class JsonStructureResolver
    {
        private readonly JsonStructureImportPolicy _importPolicy;
        private readonly IJsonStructureDocumentLoader _documentLoader;

        /// <summary>Initializes a new instance of the <see cref="JsonStructureResolver"/> class.</summary>
        public JsonStructureResolver()
            : this(new JsonStructureImportPolicy(), new DefaultJsonStructureDocumentLoader())
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureResolver"/> class.</summary>
        /// <param name="importPolicy">The import security policy.</param>
        public JsonStructureResolver(JsonStructureImportPolicy importPolicy)
            : this(importPolicy, new DefaultJsonStructureDocumentLoader())
        {
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureResolver"/> class.</summary>
        /// <param name="importPolicy">The import security policy.</param>
        /// <param name="documentLoader">The document loader used after the policy authorizes an import URI.</param>
        public JsonStructureResolver(JsonStructureImportPolicy importPolicy, IJsonStructureDocumentLoader documentLoader)
        {
            if (importPolicy == null)
            {
                throw new ArgumentNullException(nameof(importPolicy));
            }

            if (documentLoader == null)
            {
                throw new ArgumentNullException(nameof(documentLoader));
            }

            _importPolicy = importPolicy;
            _documentLoader = documentLoader;
        }

        /// <summary>Resolves every reference in a document using the default offline import policy.</summary>
        /// <param name="document">The parsed document.</param>
        /// <returns>The same document, with resolved links populated.</returns>
        /// <exception cref="JsonStructureException">A reference is unresolvable, an import is not authorized, or a chain is cyclic.</exception>
        public static JsonStructureDocument Resolve(JsonStructureDocument document)
        {
            return new JsonStructureResolver().Resolve(document, CancellationToken.None);
        }

        /// <summary>Resolves every import and reference in a document.</summary>
        /// <param name="document">The parsed document.</param>
        /// <param name="cancellationToken">A cancellation token for the resolution run.</param>
        /// <returns>The same document, with imported declarations and resolved links populated.</returns>
        /// <exception cref="JsonStructureException">A reference is unresolvable, an import is not authorized, or a chain is cyclic.</exception>
        public JsonStructureDocument Resolve(JsonStructureDocument document, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var context = new ImportResolutionContext();
            var currentUri = GetDocumentUri(document);
            if (currentUri != null)
            {
                context.ImportStack.Add(currentUri);
            }

            ProcessImports(document, context, currentUri, cancellationToken);
            ResolveReferences(document);
            return document;
        }

        /// <summary>Resolves a JSON Pointer to a declared type.</summary>
        /// <param name="document">The document.</param>
        /// <param name="pointer">The JSON Pointer, for example <c>#/definitions/Ns/Type</c>.</param>
        /// <returns>The referenced type, or <c>null</c> when the pointer does not resolve.</returns>
        public static JsonStructureNamedType ResolvePointer(JsonStructureDocument document, string pointer)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var segments = SplitPointer(pointer);
            if (segments == null || segments.Count == 0)
            {
                return null;
            }

            if (!string.Equals(segments[0], JsonStructureKeywords.Definitions, StringComparison.Ordinal))
            {
                return null;
            }

            var current = document.Definitions;
            for (var i = 1; i < segments.Count - 1; i++)
            {
                if (!current.TryGetNamespace(segments[i], out var child))
                {
                    return null;
                }

                current = child;
            }

            return current.TryGetType(segments[segments.Count - 1], out var type) ? type : null;
        }

        private static void ResolveReferences(JsonStructureDocument document)
        {
            var types = document.GetAllTypes().ToList();

            foreach (var type in types)
            {
                ResolveSchema(document, type.Schema);
            }

            if (document.RootSchema != null)
            {
                ResolveSchema(document, document.RootSchema);
            }

            if (document.RootPointer != null && ResolvePointer(document, document.RootPointer) == null)
            {
                throw new JsonStructureException(
                    "The '$root' pointer '" + document.RootPointer + "' does not resolve to a declared type.", "#/$root");
            }

            foreach (var type in types)
            {
                VerifyNoInheritanceCycle(type);
            }
        }

        private void ProcessImports(
            JsonStructureDocument document,
            ImportResolutionContext context,
            Uri currentUri,
            CancellationToken cancellationToken)
        {
            if (document.SourceJson is not JObject root)
            {
                return;
            }

            ProcessImportAtNode(document, document.Definitions, root, "#", context, currentUri, cancellationToken);

            if (root[JsonStructureKeywords.Definitions] is JObject definitions)
            {
                ProcessNamespaceImports(
                    document,
                    document.Definitions,
                    definitions,
                    "#/definitions",
                    context,
                    currentUri,
                    cancellationToken);
            }
        }

        private void ProcessNamespaceImports(
            JsonStructureDocument document,
            JsonStructureNamespace target,
            JObject node,
            string pointer,
            ImportResolutionContext context,
            Uri currentUri,
            CancellationToken cancellationToken)
        {
            ProcessImportAtNode(document, target, node, pointer, context, currentUri, cancellationToken);

            foreach (var property in node.Properties())
            {
                if (IsImportKeyword(property.Name) || property.Value is not JObject child || IsTypeDeclaration(child))
                {
                    continue;
                }

                if (!target.TryGetNamespace(property.Name, out var childNamespace))
                {
                    continue;
                }

                ProcessNamespaceImports(
                    document,
                    childNamespace,
                    child,
                    pointer + "/" + EscapePointerSegment(property.Name),
                    context,
                    currentUri,
                    cancellationToken);
            }
        }

        private void ProcessImportAtNode(
            JsonStructureDocument document,
            JsonStructureNamespace target,
            JObject node,
            string pointer,
            ImportResolutionContext context,
            Uri currentUri,
            CancellationToken cancellationToken)
        {
            ImportOne(document, target, node, JsonStructureKeywords.Import, false, pointer, context, currentUri, cancellationToken);
            ImportOne(document, target, node, JsonStructureKeywords.ImportDefs, true, pointer, context, currentUri, cancellationToken);
        }

        private void ImportOne(
            JsonStructureDocument document,
            JsonStructureNamespace target,
            JObject node,
            string keyword,
            bool definitionsOnly,
            string pointer,
            ImportResolutionContext context,
            Uri currentUri,
            CancellationToken cancellationToken)
        {
            var importToken = node[keyword];
            if (importToken == null)
            {
                return;
            }

            if (!document.IsAddInActive(JsonStructureAddIns.Import))
            {
                throw new JsonStructureException(
                    "The '" + keyword + "' keyword requires the JSONStructureImport add-in. Use the extended JSON Structure dialect or enable JSONStructureImport with '$uses'.",
                    Combine(pointer, keyword));
            }

            var reference = (string)importToken;
            if (string.IsNullOrEmpty(reference))
            {
                throw new JsonStructureException(
                    "The '" + keyword + "' value must be a non-empty URI or relative file path.",
                    Combine(pointer, keyword));
            }

            var importUri = ResolveImportUri(reference, currentUri, Combine(pointer, keyword));
            ValidateImportUri(importUri, Combine(pointer, keyword));

            var imported = LoadImportedDocument(importUri, context, Combine(pointer, keyword), cancellationToken);
            CopyImportedDefinitions(imported, target, definitionsOnly, pointer);
        }

        private JsonStructureDocument LoadImportedDocument(
            Uri importUri,
            ImportResolutionContext context,
            string pointer,
            CancellationToken cancellationToken)
        {
            if (context.Cache.TryGetValue(importUri, out var cached))
            {
                return cached;
            }

            if (context.ImportStack.Contains(importUri))
            {
                throw new JsonStructureException(
                    "The '$import' graph is cyclic at '" + importUri + "'. Import cycle detection stopped resolution before recursion.",
                    pointer);
            }

            context.ImportStack.Add(importUri);
            try
            {
                string json;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(_importPolicy.Timeout);
                    try
                    {
                        json = _documentLoader.Load(importUri, _importPolicy, timeout.Token);
                    }
                    catch (OperationCanceledException exception)
                    {
                        throw new JsonStructureException(
                            "Loading imported document '" + importUri + "' timed out after " + _importPolicy.Timeout + ". Increase JsonStructureImportPolicy.Timeout if this source is trusted.",
                            pointer,
                            exception);
                    }
                }

                EnsureWithinSizeLimit(json, importUri, pointer);

                var imported = new JsonStructureParser().Parse(json);
                imported.DocumentPath = importUri.IsFile ? importUri.LocalPath : importUri.AbsoluteUri;

                ProcessImports(imported, context, importUri, cancellationToken);
                context.Cache[importUri] = imported;
                return imported;
            }
            finally
            {
                context.ImportStack.Remove(importUri);
            }
        }

        private void ValidateImportUri(Uri importUri, string pointer)
        {
            if (importUri == null || !importUri.IsAbsoluteUri)
            {
                throw new JsonStructureException("The import URI could not be resolved to an absolute URI.", pointer);
            }

            if (string.Equals(importUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(importUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                if (!_importPolicy.AllowNetwork)
                {
                    throw new JsonStructureException(
                        "Import fetching is offline by default. Set JsonStructureImportPolicy.AllowNetwork to true and add '" + importUri.Host + "' to AllowedHosts to allow this import.",
                        pointer);
                }

                if (!_importPolicy.AllowedHosts.Any(h => string.Equals(h, importUri.Host, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new JsonStructureException(
                        "The import host '" + importUri.Host + "' is not allowlisted. Add it to JsonStructureImportPolicy.AllowedHosts only if it is trusted.",
                        pointer);
                }

                return;
            }

            if (importUri.IsFile)
            {
                if (!_importPolicy.AllowFileSystem)
                {
                    throw new JsonStructureException(
                        "File imports are disabled by default. Set JsonStructureImportPolicy.AllowFileSystem to true and configure BaseDirectory to allow trusted local imports.",
                        pointer);
                }

                EnsureFileIsUnderBaseDirectory(importUri, pointer);
                return;
            }

            throw new JsonStructureException(
                "The import URI scheme '" + importUri.Scheme + "' is not supported. Only http, https, and file imports are supported.",
                pointer);
        }

        private void EnsureFileIsUnderBaseDirectory(Uri importUri, string pointer)
        {
            if (string.IsNullOrEmpty(_importPolicy.BaseDirectory))
            {
                throw new JsonStructureException(
                    "JsonStructureImportPolicy.BaseDirectory must be configured before local file imports are allowed.",
                    pointer);
            }

            var baseDirectory = Path.GetFullPath(_importPolicy.BaseDirectory);
            var importPath = Path.GetFullPath(importUri.LocalPath);
            var baseWithSeparator = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!string.Equals(importPath, baseDirectory, StringComparison.OrdinalIgnoreCase) &&
                !importPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonStructureException(
                    "The local import path '" + importPath + "' escapes the configured BaseDirectory '" + baseDirectory + "'. Path traversal is not allowed.",
                    pointer);
            }
        }

        private Uri ResolveImportUri(string reference, Uri currentUri, string pointer)
        {
            if (Uri.TryCreate(reference, UriKind.Absolute, out var absolute))
            {
                return absolute;
            }

            if (!string.IsNullOrEmpty(_importPolicy.BaseDirectory) && (currentUri == null || !currentUri.IsFile))
            {
                var combined = Path.GetFullPath(Path.Combine(_importPolicy.BaseDirectory, reference));
                return new Uri(combined);
            }

            if (currentUri != null)
            {
                return new Uri(currentUri, reference);
            }

            if (!string.IsNullOrEmpty(_importPolicy.BaseDirectory))
            {
                var combined = Path.GetFullPath(Path.Combine(_importPolicy.BaseDirectory, reference));
                return new Uri(combined);
            }

            throw new JsonStructureException(
                "The relative import path '" + reference + "' has no base URI. Set JsonStructureDocument.DocumentPath or JsonStructureImportPolicy.BaseDirectory.",
                pointer);
        }

        private static Uri GetDocumentUri(JsonStructureDocument document)
        {
            Uri result;
            if (!string.IsNullOrEmpty(document.DocumentPath))
            {
                if (Uri.TryCreate(document.DocumentPath, UriKind.Absolute, out result))
                {
                    return result;
                }

                var fullPath = Path.GetFullPath(document.DocumentPath);
                return new Uri(fullPath);
            }

            if (!string.IsNullOrEmpty(document.Id) && Uri.TryCreate(document.Id, UriKind.Absolute, out result))
            {
                return result;
            }

            return null;
        }

        private void EnsureWithinSizeLimit(string json, Uri importUri, string pointer)
        {
            if (_importPolicy.MaxDocumentSize <= 0)
            {
                throw new JsonStructureException("JsonStructureImportPolicy.MaxDocumentSize must be greater than zero.", pointer);
            }

            var byteCount = System.Text.Encoding.UTF8.GetByteCount(json ?? string.Empty);
            if (byteCount > _importPolicy.MaxDocumentSize)
            {
                throw new JsonStructureException(
                    "The imported document '" + importUri + "' exceeds the configured maximum size of " +
                    _importPolicy.MaxDocumentSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes.",
                    pointer);
            }
        }

        private static void CopyImportedDefinitions(
            JsonStructureDocument imported,
            JsonStructureNamespace target,
            bool definitionsOnly,
            string targetPointer)
        {
            CopyNamespace(imported.Definitions, target, definitionsOnly, targetPointer);
        }

        private static void CopyNamespace(
            JsonStructureNamespace source,
            JsonStructureNamespace target,
            bool definitionsOnly,
            string targetPointer)
        {
            foreach (var sourceNamespace in source.Namespaces)
            {
                if (target.TryGetType(sourceNamespace.Name, out _))
                {
                    continue;
                }

                if (!target.TryGetNamespace(sourceNamespace.Name, out var targetNamespace))
                {
                    targetNamespace = new JsonStructureNamespace(sourceNamespace.Name, target);
                    target.AddNamespace(targetNamespace);
                }

                CopyNamespace(
                    sourceNamespace,
                    targetNamespace,
                    definitionsOnly,
                    targetPointer + "/" + EscapePointerSegment(sourceNamespace.Name));
            }

            foreach (var sourceType in source.Types)
            {
                if (definitionsOnly && !IsDefinitionType(sourceType))
                {
                    continue;
                }

                if (target.TryGetType(sourceType.Name, out _) || target.TryGetNamespace(sourceType.Name, out _))
                {
                    continue;
                }

                var prefix = NamespaceSegments(target);
                var clone = CloneSchema(sourceType.Schema, prefix, targetPointer + "/" + EscapePointerSegment(sourceType.Name));
                target.AddType(new JsonStructureNamedType(sourceType.Name, clone, target));
            }
        }

        private static bool IsDefinitionType(JsonStructureNamedType type)
        {
            return type.Pointer != null && type.Pointer.StartsWith("#/definitions/", StringComparison.Ordinal);
        }

        private static JsonStructureSchema CloneSchema(JsonStructureSchema source, IReadOnlyList<string> namespacePrefix, string pointer)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new JsonStructureSchema
            {
                Kind = source.Kind,
                Name = source.Name,
                Pointer = pointer,
                Reference = RewritePointer(source.Reference, namespacePrefix),
                IsAbstract = source.IsAbstract,
                AdditionalPropertiesAllowed = source.AdditionalPropertiesAllowed,
                Const = source.Const == null ? null : source.Const.DeepClone(),
                Description = source.Description,
                Examples = source.Examples == null ? null : new JArray(source.Examples),
                SourceJson = source.SourceJson == null ? null : source.SourceJson.DeepClone()
            };

            foreach (var extendsPointer in source.ExtendsPointers.Count == 0
                ? (source.Extends == null ? Array.Empty<string>() : new[] { source.Extends })
                : source.ExtendsPointers)
            {
                clone.AddExtends(RewritePointer(extendsPointer, namespacePrefix));
            }

            foreach (var member in source.Union)
            {
                if (member.Reference != null)
                {
                    clone.AddUnionMember(new JsonStructureTypeReference(RewritePointer(member.Reference, namespacePrefix)));
                }
                else if (member.InlineSchema != null)
                {
                    clone.AddUnionMember(new JsonStructureTypeReference(CloneSchema(member.InlineSchema, namespacePrefix, pointer + "/type")));
                }
                else
                {
                    clone.AddUnionMember(new JsonStructureTypeReference(member.Kind));
                }
            }

            foreach (var property in source.Properties)
            {
                var clonedProperty = new JsonStructureProperty(
                    property.Name,
                    CloneSchema(property.Schema, namespacePrefix, pointer + "/properties/" + EscapePointerSegment(property.Name)))
                {
                    IsRequired = property.IsRequired,
                    IsConditionallyRequired = property.IsConditionallyRequired
                };
                clone.AddProperty(clonedProperty);
            }

            foreach (var set in source.RequiredSets)
            {
                clone.AddRequiredSet(set.ToList());
            }

            foreach (var element in source.TupleOrder)
            {
                clone.AddTupleElement(element);
            }

            foreach (var choice in source.Choices)
            {
                clone.AddChoice(new JsonStructureChoice(
                    choice.Name,
                    CloneSchema(choice.Schema, namespacePrefix, pointer + "/choices/" + EscapePointerSegment(choice.Name))));
            }

            clone.Items = CloneSchema(source.Items, namespacePrefix, pointer + "/items");
            clone.Values = CloneSchema(source.Values, namespacePrefix, pointer + "/values");
            clone.AdditionalProperties = CloneSchema(source.AdditionalProperties, namespacePrefix, pointer + "/additionalProperties");

            foreach (var value in source.Enumeration)
            {
                clone.AddEnumerationValue(value);
            }

            foreach (var annotation in source.Annotations)
            {
                clone.SetAnnotation(annotation.Key, annotation.Value == null ? null : annotation.Value.DeepClone());
            }

            foreach (var extension in source.ExtensionData)
            {
                clone.SetExtensionData(extension.Key, extension.Value == null ? null : extension.Value.DeepClone());
            }

            return clone;
        }

        private static string RewritePointer(string pointer, IReadOnlyList<string> namespacePrefix)
        {
            var segments = SplitPointer(pointer);
            if (segments == null || segments.Count == 0 || namespacePrefix.Count == 0)
            {
                return pointer;
            }

            if (!string.Equals(segments[0], JsonStructureKeywords.Definitions, StringComparison.Ordinal))
            {
                return pointer;
            }

            var rewritten = new List<string> { JsonStructureKeywords.Definitions };
            rewritten.AddRange(namespacePrefix);
            for (var i = 1; i < segments.Count; i++)
            {
                rewritten.Add(segments[i]);
            }

            return "#/" + string.Join("/", rewritten.Select(EscapePointerSegment));
        }

        private static List<string> NamespaceSegments(JsonStructureNamespace target)
        {
            var segments = new List<string>();
            for (var current = target; current != null && !current.IsRoot; current = current.Parent)
            {
                segments.Insert(0, current.Name);
            }

            return segments;
        }

        private static void ResolveSchema(JsonStructureDocument document, JsonStructureSchema schema)
        {
            ResolveSchema(document, schema, new HashSet<JsonStructureSchema>());
        }

        private static void ResolveSchema(
            JsonStructureDocument document,
            JsonStructureSchema schema,
            HashSet<JsonStructureSchema> visited)
        {
            if (schema == null || !visited.Add(schema))
            {
                return;
            }

            if (schema.Reference != null)
            {
                schema.ResolvedReference = Require(document, schema.Reference, schema.Pointer, "type/$ref");
            }

            var extendsPointers = schema.ExtendsPointers.Count == 0
                ? (schema.Extends == null ? Array.Empty<string>() : new[] { schema.Extends })
                : schema.ExtendsPointers;

            foreach (var extendsPointer in extendsPointers)
            {
                var baseType = Require(document, extendsPointer, schema.Pointer, JsonStructureKeywords.Extends);

                if (baseType.Schema == schema)
                {
                    throw new JsonStructureException(
                        "The type extends itself.", Combine(schema.Pointer, JsonStructureKeywords.Extends));
                }

                schema.AddResolvedExtends(baseType);
            }

            foreach (var member in schema.Union)
            {
                if (member.Reference != null)
                {
                    member.ResolvedReference = Require(document, member.Reference, schema.Pointer, "type");
                }

                ResolveSchema(document, member.InlineSchema, visited);
            }

            foreach (var property in schema.Properties)
            {
                ResolveSchema(document, property.Schema, visited);
            }

            foreach (var choice in schema.Choices)
            {
                ResolveSchema(document, choice.Schema, visited);
            }

            ResolveSchema(document, schema.Items, visited);
            ResolveSchema(document, schema.Values, visited);
            ResolveSchema(document, schema.AdditionalProperties, visited);
        }

        private static JsonStructureNamedType Require(
            JsonStructureDocument document,
            string pointer,
            string schemaPointer,
            string keyword)
        {
            var resolved = ResolvePointer(document, pointer);
            if (resolved == null)
            {
                throw new JsonStructureException(
                    "The '" + keyword + "' pointer '" + pointer + "' does not resolve to a declared type. " +
                    "References must point at a type declared under 'definitions'.",
                    Combine(schemaPointer, keyword));
            }

            return resolved;
        }

        private static void VerifyNoInheritanceCycle(JsonStructureNamedType type)
        {
            var seen = new HashSet<JsonStructureSchema>();
            var chain = new List<string>();

            var stack = new HashSet<JsonStructureSchema>();
            VerifyNoInheritanceCycle(type, stack, new List<string>());
        }

        private static void VerifyNoInheritanceCycle(
            JsonStructureNamedType type,
            HashSet<JsonStructureSchema> stack,
            List<string> chain)
        {
            if (type == null)
            {
                return;
            }

            if (!stack.Add(type.Schema))
            {
                chain.Add(type.FullName);
                throw new JsonStructureException(
                    "The '$extends' chain is cyclic: " + string.Join(" -> ", chain) + ".",
                    Combine(type.Schema.Pointer, JsonStructureKeywords.Extends));
            }

            chain.Add(type.FullName);
            foreach (var baseType in type.Schema.ResolvedExtendsTypes)
            {
                VerifyNoInheritanceCycle(baseType, stack, chain);
            }

            chain.RemoveAt(chain.Count - 1);
            stack.Remove(type.Schema);
        }

        private static bool IsImportKeyword(string keyword)
        {
            return string.Equals(keyword, JsonStructureKeywords.Import, StringComparison.Ordinal) ||
                string.Equals(keyword, JsonStructureKeywords.ImportDefs, StringComparison.Ordinal);
        }

        private static bool IsTypeDeclaration(JObject node)
        {
            return node[JsonStructureKeywords.Type] != null;
        }

        private static string Combine(string pointer, string keyword)
        {
            return pointer == null ? null : pointer.TrimEnd('/') + "/" + keyword;
        }

        private static List<string> SplitPointer(string pointer)
        {
            if (string.IsNullOrEmpty(pointer))
            {
                return null;
            }

            if (pointer[0] != '#')
            {
                return null;
            }

            var value = Uri.UnescapeDataString(pointer.Substring(1));
            if (value.Length == 0 || value[0] != '/')
            {
                return null;
            }

            var segments = new List<string>();
            foreach (var segment in value.Substring(1).Split('/'))
            {
                if (segment.Length == 0)
                {
                    return null;
                }

                for (var i = 0; i < segment.Length; i++)
                {
                    if (segment[i] == '~' && (i + 1 >= segment.Length || (segment[i + 1] != '0' && segment[i + 1] != '1')))
                    {
                        return null;
                    }
                }

                segments.Add(segment.Replace("~1", "/").Replace("~0", "~"));
            }

            return segments;
        }

        private static string EscapePointerSegment(string segment)
        {
            return segment.Replace("~", "~0").Replace("/", "~1");
        }

        private sealed class ImportResolutionContext
        {
            internal ImportResolutionContext()
            {
                Cache = new Dictionary<Uri, JsonStructureDocument>();
                ImportStack = new HashSet<Uri>();
            }

            internal Dictionary<Uri, JsonStructureDocument> Cache { get; }

            internal HashSet<Uri> ImportStack { get; }
        }
    }
}
