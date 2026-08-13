//-----------------------------------------------------------------------
// <copyright file="JsonStructureNamespace.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>
    /// A node in the JSON Structure namespace hierarchy rooted at <c>definitions</c>.
    /// </summary>
    /// <remarks>
    /// Any object under <c>definitions</c> that does not itself declare a <c>type</c> is a namespace.
    /// This hierarchy is what makes generated code namespaced rather than flat.
    /// </remarks>
    public class JsonStructureNamespace
    {
        private readonly List<JsonStructureNamespace> _namespaces = [];
        private readonly Dictionary<string, JsonStructureNamespace> _namespaceIndex = new(StringComparer.Ordinal);
        private readonly List<JsonStructureNamedType> _types = [];
        private readonly Dictionary<string, JsonStructureNamedType> _typeIndex = new(StringComparer.Ordinal);

        /// <summary>Initializes a new instance of the <see cref="JsonStructureNamespace"/> class.</summary>
        /// <param name="name">The namespace name, or <c>null</c> for the root namespace.</param>
        /// <param name="parent">The parent namespace, or <c>null</c> for the root namespace.</param>
        public JsonStructureNamespace(string name, JsonStructureNamespace parent)
        {
            Name = name;
            Parent = parent;
        }

        /// <summary>Gets the namespace name. The root namespace has no name.</summary>
        public string Name { get; }

        /// <summary>Gets the parent namespace, or <c>null</c> for the root namespace.</summary>
        public JsonStructureNamespace Parent { get; }

        /// <summary>Gets a value indicating whether this is the root namespace.</summary>
        public bool IsRoot => Parent == null;

        /// <summary>Gets the nested namespaces, in declaration order.</summary>
        public IReadOnlyList<JsonStructureNamespace> Namespaces => _namespaces;

        /// <summary>Gets the types declared directly in this namespace, in declaration order.</summary>
        public IReadOnlyList<JsonStructureNamedType> Types => _types;

        /// <summary>Gets the dotted path of this namespace, excluding the unnamed root.</summary>
        public string Path
        {
            get
            {
                var segments = new List<string>();
                for (var current = this; current != null && !current.IsRoot; current = current.Parent)
                {
                    segments.Insert(0, current.Name);
                }

                return string.Join(".", segments);
            }
        }

        /// <summary>Adds a nested namespace.</summary>
        /// <param name="child">The namespace.</param>
        public void AddNamespace(JsonStructureNamespace child)
        {
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            _namespaces.Add(child);
            _namespaceIndex[child.Name] = child;
        }

        /// <summary>Adds a type declaration.</summary>
        /// <param name="type">The type.</param>
        public void AddType(JsonStructureNamedType type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _types.Add(type);
            _typeIndex[type.Name] = type;
        }

        /// <summary>Tries to get a nested namespace by name.</summary>
        /// <param name="name">The namespace name.</param>
        /// <param name="result">The namespace.</param>
        /// <returns><c>true</c> when found.</returns>
        public bool TryGetNamespace(string name, out JsonStructureNamespace result)
        {
            if (name == null)
            {
                result = null;
                return false;
            }

            return _namespaceIndex.TryGetValue(name, out result);
        }

        /// <summary>Tries to get a type declared directly in this namespace.</summary>
        /// <param name="name">The type name.</param>
        /// <param name="result">The type.</param>
        /// <returns><c>true</c> when found.</returns>
        public bool TryGetType(string name, out JsonStructureNamedType result)
        {
            if (name == null)
            {
                result = null;
                return false;
            }

            return _typeIndex.TryGetValue(name, out result);
        }

        /// <summary>Enumerates every type in this namespace and all nested namespaces.</summary>
        /// <returns>The types.</returns>
        public IEnumerable<JsonStructureNamedType> GetAllTypes()
        {
            foreach (var type in _types)
            {
                yield return type;
            }

            foreach (var child in _namespaces)
            {
                foreach (var type in child.GetAllTypes())
                {
                    yield return type;
                }
            }
        }

        /// <summary>Enumerates this namespace and all nested namespaces.</summary>
        /// <returns>The namespaces.</returns>
        public IEnumerable<JsonStructureNamespace> GetAllNamespaces()
        {
            yield return this;

            foreach (var child in _namespaces)
            {
                foreach (var descendant in child.GetAllNamespaces())
                {
                    yield return descendant;
                }
            }
        }
    }
}
