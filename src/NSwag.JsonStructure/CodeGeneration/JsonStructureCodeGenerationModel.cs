//-----------------------------------------------------------------------
// <copyright file="JsonStructureCodeGenerationModel.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.Model;
using NSwag.JsonStructure.Resolution;

#pragma warning disable CS1591

namespace NSwag.JsonStructure.CodeGeneration
{
    /// <summary>A language-neutral, resolved view of a JSON Structure document for code generators.</summary>
    public sealed class JsonStructureCodeGenerationModel
    {
        private JsonStructureCodeGenerationModel(
            JsonStructureDocument document,
            IReadOnlyList<JsonStructureCodeGenerationNamespace> namespaces,
            IReadOnlyList<JsonStructureCodeGenerationNamedType> types,
            JsonStructureCodeGenerationNamedType rootType,
            JsonStructureCodeGenerationTypeReference rootTypeReference)
        {
            Document = document;
            AddIns = document.AddIns;
            Namespaces = namespaces;
            Types = types;
            RootType = rootType;
            RootTypeReference = rootTypeReference;
        }

        /// <summary>Gets the resource scope name used when multiple schema resources are aggregated.</summary>
        public string ScopeName => Document.Name;

        /// <summary>Projects a parsed and resolved document into the code-generation model.</summary>
        public static JsonStructureCodeGenerationModel Create(JsonStructureDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var namedTypes = document.GetAllTypes().ToList();
            var bySchema = new Dictionary<JsonStructureSchema, JsonStructureCodeGenerationNamedType>();
            var byNamedType = new Dictionary<JsonStructureNamedType, JsonStructureCodeGenerationNamedType>();

            foreach (var namedType in namedTypes)
            {
                var projection = new JsonStructureCodeGenerationNamedType(
                    namedType.Name,
                    GetNamespacePath(namedType.Namespace),
                    namedType.Schema.Kind,
                    namedType.Schema.IsAbstract,
                    namedType.Schema.Description,
                    CopyMetadata(namedType.Schema),
                    namedType.Schema.Const,
                    namedType.Schema.Enumeration,
                    namedType.Schema.Examples);
                bySchema[namedType.Schema] = projection;
                byNamedType[namedType] = projection;
            }

            var projector = new Projector(bySchema, byNamedType);
            foreach (var namedType in namedTypes)
            {
                projector.Populate(bySchema[namedType.Schema], namedType.Schema);
            }

            var namespaces = document.Definitions.GetAllNamespaces()
                .Select(ns => new JsonStructureCodeGenerationNamespace(
                    ns.Name,
                    GetNamespacePath(ns),
                    ns.Types.Select(type => byNamedType[type]).ToList()))
                .ToList();

            bySchema.Values.ToList().ForEach(type => type.Freeze());
            var rootType = document.RootPointer == null
                ? (document.RootSchema != null && bySchema.TryGetValue(document.RootSchema, out var root) ? root : null)
                : JsonStructureResolver.ResolvePointer(document, document.RootPointer) is { } rootNamed &&
                  byNamedType.TryGetValue(rootNamed, out var resolvedRoot) ? resolvedRoot : null;

            var rootTypeReference = rootType == null && document.RootSchema != null
                ? projector.ProjectRoot(document.RootSchema)
                : null;

            return new JsonStructureCodeGenerationModel(document, namespaces, bySchema.Values.ToList(), rootType, rootTypeReference);
        }

        /// <summary>Projects a resolved document into the code-generation model.</summary>
        public static JsonStructureCodeGenerationModel FromResolvedDocument(JsonStructureDocument document)
        {
            return Create(document);
        }

        /// <summary>Gets the source document.</summary>
        public JsonStructureDocument Document { get; }

        /// <summary>Gets the active add-in vocabularies.</summary>
        public JsonStructureAddIns AddIns { get; }

        /// <summary>Gets all namespaces in declaration order.</summary>
        public IReadOnlyList<JsonStructureCodeGenerationNamespace> Namespaces { get; }

        /// <summary>Gets all named types in declaration order.</summary>
        public IReadOnlyList<JsonStructureCodeGenerationNamedType> Types { get; }

        /// <summary>Gets the document root named type, when one is declared.</summary>
        public JsonStructureCodeGenerationNamedType RootType { get; }

        /// <summary>Gets the document root type reference when the root is not a named type.</summary>
        public JsonStructureCodeGenerationTypeReference RootTypeReference { get; }

        /// <summary>Gets the namespace path of a type within this resource's aggregate scope.</summary>
        public IReadOnlyList<string> GetScopedNamespacePath(JsonStructureCodeGenerationNamedType type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return string.IsNullOrWhiteSpace(ScopeName)
                ? type.NamespacePath
                : new[] { ScopeName }.Concat(type.NamespacePath).ToList();
        }

        private static List<string> GetNamespacePath(JsonStructureNamespace ns)
        {
            var path = new List<string>();
            for (var current = ns; current != null && !current.IsRoot; current = current.Parent)
            {
                path.Insert(0, current.Name);
            }

            return path;
        }

        private static Dictionary<string, JToken> CopyMetadata(JsonStructureSchema schema)
        {
            return schema.Annotations.ToDictionary(
                pair => pair.Key,
                pair => pair.Value == null ? null : pair.Value.DeepClone(),
                StringComparer.Ordinal);
        }

        private sealed class Projector
        {
            private readonly Dictionary<JsonStructureSchema, JsonStructureCodeGenerationNamedType> _bySchema;
            private readonly Dictionary<JsonStructureNamedType, JsonStructureCodeGenerationNamedType> _byNamedType;
            private readonly Dictionary<JsonStructureSchema, JsonStructureCodeGenerationNamedType> _inlineTypes = new();

            public Projector(
                Dictionary<JsonStructureSchema, JsonStructureCodeGenerationNamedType> bySchema,
                Dictionary<JsonStructureNamedType, JsonStructureCodeGenerationNamedType> byNamedType)
            {
                _bySchema = bySchema;
                _byNamedType = byNamedType;
            }

            public void Populate(JsonStructureCodeGenerationNamedType target, JsonStructureSchema schema)
            {
                target.BaseType = schema.ResolvedExtends == null ? null : _byNamedType[schema.ResolvedExtends];
                target.Properties = schema.Properties.Select(ProjectProperty).ToList();
                target.Choices = schema.Choices.Select(choice => new JsonStructureCodeGenerationChoiceVariant(
                    choice.Name,
                    Project(choice.Schema),
                    choice.Schema.Const,
                    CopyMetadata(choice.Schema))).ToList();
                if (schema.Kind == JsonStructureTypeKind.Choice && !schema.IsInlineUnion)
                {
                    foreach (var choice in target.Choices)
                    {
                        var variant = choice.Type?.NamedType ?? choice.Type?.InlineType;
                        if (variant != null && variant.Kind == JsonStructureTypeKind.Object && variant.BaseType == null)
                        {
                            variant.BaseType = target;
                        }
                    }
                }
                target.Selector = schema.Selector;
                target.IsInlineChoice = schema.IsInlineUnion;
                target.TupleOrder = schema.TupleOrder.ToList();
                target.Items = Project(schema.Items);
                target.Values = Project(schema.Values);
            }

            public JsonStructureCodeGenerationTypeReference ProjectRoot(JsonStructureSchema schema)
            {
                return Project(schema);
            }

            private JsonStructureCodeGenerationProperty ProjectProperty(JsonStructureProperty property)
            {
                return new JsonStructureCodeGenerationProperty(
                    property.Name,
                    Project(property.Schema),
                    property.IsRequired,
                    property.IsConditionallyRequired,
                    CopyMetadata(property.Schema),
                    property.Schema.Const);
            }

            private JsonStructureCodeGenerationTypeReference Project(JsonStructureSchema schema)
            {
                if (schema == null)
                {
                    return null;
                }

                if (schema.IsReference)
                {
                    return new JsonStructureCodeGenerationTypeReference(
                        JsonStructureTypeKind.None,
                        schema.ResolvedReference == null ? null : _byNamedType[schema.ResolvedReference],
                        null,
                        null,
                        null,
                        false,
                        CopyMetadata(schema),
                        null,
                        null,
                        schema.Enumeration);
                }

                var union = schema.Union;
                if (union.Count > 0)
                {
                    var nullable = union.Any(member => member.IsNull);
                    var members = union
                        .Where(member => !member.IsNull)
                        .Select(member => Project(member))
                        .ToList();
                    return new JsonStructureCodeGenerationTypeReference(
                        members.Count == 1 ? members[0].Kind : JsonStructureTypeKind.None,
                        members.Count == 1 ? members[0].NamedType : null,
                        members.Count == 1 ? members[0].InlineType : null,
                        null,
                        null,
                        nullable,
                        CopyMetadata(schema),
                        members,
                        null);
                }

                var inline = GetInlineType(schema);
                return new JsonStructureCodeGenerationTypeReference(
                    schema.Kind,
                    null,
                    inline,
                    Project(schema.Items),
                    Project(schema.Values),
                    false,
                    CopyMetadata(schema),
                    null,
                    schema.TupleOrder.Select(name => schema.Properties.First(p => p.Name == name))
                        .Select(property => Project(property.Schema)).ToList(),
                    schema.Enumeration);
            }

            private JsonStructureCodeGenerationNamedType GetInlineType(JsonStructureSchema schema)
            {
                if (_bySchema.TryGetValue(schema, out var named))
                {
                    return named;
                }

                if (!JsonStructureTypeKinds.IsCompound(schema.Kind))
                {
                    return null;
                }

                if (!_inlineTypes.TryGetValue(schema, out var inline))
                {
                    inline = new JsonStructureCodeGenerationNamedType(
                        schema.Name,
                        [],
                        schema.Kind,
                        schema.IsAbstract,
                        schema.Description,
                        CopyMetadata(schema),
                        schema.Const,
                        schema.Enumeration,
                        schema.Examples);
                    _inlineTypes[schema] = inline;
                    Populate(inline, schema);
                }

                return inline;
            }

            private JsonStructureCodeGenerationTypeReference Project(JsonStructureTypeReference member)
            {
                if (member.IsReference)
                {
                    return new JsonStructureCodeGenerationTypeReference(
                        JsonStructureTypeKind.None,
                        member.ResolvedReference == null ? null : _byNamedType[member.ResolvedReference],
                        null,
                        null,
                        null,
                        false,
                        new Dictionary<string, JToken>(StringComparer.Ordinal));
                }

                return member.InlineSchema == null
                    ? new JsonStructureCodeGenerationTypeReference(
                        member.Kind,
                        null,
                        null,
                        null,
                        null,
                        false,
                        new Dictionary<string, JToken>(StringComparer.Ordinal))
                    : Project(member.InlineSchema);
            }
        }
    }

    /// <summary>A namespace in the code-generation model.</summary>
    public sealed class JsonStructureCodeGenerationNamespace
    {
        internal JsonStructureCodeGenerationNamespace(string name, IReadOnlyList<string> path, IReadOnlyList<JsonStructureCodeGenerationNamedType> types)
        {
            Name = name;
            Path = path;
            Types = types;
        }

        public string Name { get; }
        public IReadOnlyList<string> Path { get; }
        public string FullName => string.Join(".", Path);
        public IReadOnlyList<JsonStructureCodeGenerationNamedType> Types { get; }
    }

    /// <summary>A named generated type.</summary>
    public sealed class JsonStructureCodeGenerationNamedType
    {
        internal JsonStructureCodeGenerationNamedType(string name, IReadOnlyList<string> namespacePath, JsonStructureTypeKind kind, bool isAbstract, string description, IReadOnlyDictionary<string, JToken> annotations, JToken @const = null, IReadOnlyList<object> enumeration = null, JArray examples = null)
        {
            Name = name;
            NamespacePath = namespacePath;
            Kind = kind;
            IsAbstract = isAbstract;
            Description = description;
            Annotations = annotations;
            Const = @const?.DeepClone();
            Enumeration = enumeration ?? [];
            Examples = examples?.DeepClone() as JArray;
            Properties = [];
            Choices = [];
            TupleOrder = [];
        }

        public string Name { get; }
        public IReadOnlyList<string> NamespacePath { get; }
        public string FullName => NamespacePath.Count == 0 ? Name : string.Join(".", NamespacePath) + "." + Name;
        public JsonStructureTypeKind Kind { get; }
        public bool IsAbstract { get; }
        public string Description { get; }
        public IReadOnlyDictionary<string, JToken> Annotations { get; }
        public JToken Const { get; }
        public IReadOnlyList<object> Enumeration { get; }
        public JArray Examples { get; }
        public JsonStructureCodeGenerationNamedType BaseType { get; internal set; }
        public JsonStructureCodeGenerationNamedType Extends => BaseType;
        public IReadOnlyList<JsonStructureCodeGenerationProperty> Properties { get; internal set; }
        public IReadOnlyList<JsonStructureCodeGenerationChoiceVariant> Choices { get; internal set; }
        public string Selector { get; internal set; }
        public bool IsInlineChoice { get; internal set; }
        public IReadOnlyList<string> TupleOrder { get; internal set; }
        public JsonStructureCodeGenerationTypeReference Items { get; internal set; }
        public JsonStructureCodeGenerationTypeReference Values { get; internal set; }

        internal void Freeze()
        {
            Properties = Properties.ToList();
            Choices = Choices.ToList();
            TupleOrder = TupleOrder.ToList();
        }
    }

    /// <summary>A generated property and its resolved type.</summary>
    public sealed class JsonStructureCodeGenerationProperty
    {
        internal JsonStructureCodeGenerationProperty(string name, JsonStructureCodeGenerationTypeReference type, bool isRequired, bool isConditionallyRequired, IReadOnlyDictionary<string, JToken> annotations, JToken @const)
        {
            Name = name;
            Type = type;
            IsRequired = isRequired;
            IsConditionallyRequired = isConditionallyRequired;
            Annotations = annotations;
            Const = @const == null ? null : @const.DeepClone();
        }

        public string Name { get; }
        public JsonStructureCodeGenerationTypeReference Type { get; }
        public bool IsRequired { get; }
        public bool IsConditionallyRequired { get; }
        public IReadOnlyDictionary<string, JToken> Annotations { get; }
        public JToken Const { get; }
    }

    /// <summary>A choice variant with its discriminator value and resolved payload type.</summary>
    public sealed class JsonStructureCodeGenerationChoiceVariant
    {
        internal JsonStructureCodeGenerationChoiceVariant(string name, JsonStructureCodeGenerationTypeReference type, JToken discriminator, IReadOnlyDictionary<string, JToken> annotations)
        {
            Name = name;
            Type = type;
            Discriminator = discriminator == null ? null : discriminator.DeepClone();
            Annotations = annotations;
        }

        public string Name { get; }
        public string DiscriminatorName => Name;
        public JToken Discriminator { get; }
        public JsonStructureCodeGenerationTypeReference Type { get; }
        public IReadOnlyDictionary<string, JToken> Annotations { get; }
    }

    /// <summary>A recursive, language-neutral generated type reference.</summary>
    public sealed class JsonStructureCodeGenerationTypeReference
    {
        internal JsonStructureCodeGenerationTypeReference(
            JsonStructureTypeKind kind,
            JsonStructureCodeGenerationNamedType namedType,
            JsonStructureCodeGenerationNamedType inlineType,
            JsonStructureCodeGenerationTypeReference elementType,
            JsonStructureCodeGenerationTypeReference valueType,
            bool isNullable,
            IReadOnlyDictionary<string, JToken> annotations,
            IReadOnlyList<JsonStructureCodeGenerationTypeReference> union = null,
            IReadOnlyList<JsonStructureCodeGenerationTypeReference> tupleElements = null,
            IReadOnlyList<object> enumeration = null)
        {
            Kind = kind;
            NamedType = namedType;
            InlineType = inlineType;
            ElementType = elementType;
            ValueType = valueType;
            IsNullable = isNullable;
            Annotations = annotations;
            Union = union ?? [];
            TupleElements = tupleElements ?? [];
            Enumeration = enumeration ?? [];
        }

        public JsonStructureTypeKind Kind { get; }
        public bool IsNullable { get; }
        public JsonStructureCodeGenerationNamedType NamedType { get; }
        public JsonStructureCodeGenerationNamedType InlineType { get; }
        public JsonStructureCodeGenerationTypeReference ElementType { get; }
        public JsonStructureCodeGenerationTypeReference ValueType { get; }
        public IReadOnlyList<JsonStructureCodeGenerationTypeReference> TupleElements { get; }
        public IReadOnlyList<JsonStructureCodeGenerationTypeReference> Union { get; }
        public IReadOnlyList<object> Enumeration { get; }
        public IReadOnlyDictionary<string, JToken> Annotations { get; }
    }
}
