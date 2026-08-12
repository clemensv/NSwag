//-----------------------------------------------------------------------
// <copyright file="JsonStructureSchema.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;

namespace NSwag.JsonStructure.Model
{
    /// <summary>
    /// A JSON Structure schema element: a node that declares a type, together with the
    /// structural keywords, annotations and add-in keywords that apply to it.
    /// </summary>
    public class JsonStructureSchema
    {
        private readonly List<JsonStructureProperty> _properties = [];
        private readonly Dictionary<string, JsonStructureProperty> _propertyIndex = new(StringComparer.Ordinal);
        private readonly List<JsonStructureChoice> _choices = [];
        private readonly List<JsonStructureTypeReference> _union = [];
        private readonly List<IReadOnlyList<string>> _requiredSets = [];
        private readonly List<string> _tupleOrder = [];
        private readonly List<object> _enumeration = [];
        private readonly Dictionary<string, JToken> _annotations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JToken> _extensionData = new(StringComparer.Ordinal);

        /// <summary>Gets or sets the declared type name of this element.</summary>
        public JsonStructureTypeKind Kind { get; set; } = JsonStructureTypeKind.None;

        /// <summary>Gets or sets the declared <c>name</c> of the type.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the JSON Pointer at which this element was declared, relative to its document root.</summary>
        public string Pointer { get; set; }

        /// <summary>Gets or sets the type reference when <c>type</c> is <c>{ "$ref": "..." }</c>.</summary>
        public string Reference { get; set; }

        /// <summary>Gets or sets the type this element references, once resolved.</summary>
        /// <remarks>Populated by the resolver; <c>null</c> until then, and on elements that are not references.</remarks>
        public JsonStructureNamedType ResolvedReference { get; set; }

        /// <summary>Gets or sets the base type named by <c>$extends</c>, once resolved.</summary>
        public JsonStructureNamedType ResolvedExtends { get; set; }

        /// <summary>Gets the members of a non-discriminated type union when <c>type</c> is an array.</summary>
        public IReadOnlyList<JsonStructureTypeReference> Union => _union;

        /// <summary>Gets a value indicating whether this element declares a non-discriminated type union.</summary>
        public bool IsUnion => _union.Count > 0;

        /// <summary>Gets a value indicating whether this element is a type reference.</summary>
        public bool IsReference => Reference != null;

        /// <summary>Gets the declared properties of an <c>object</c> or <c>tuple</c> type, in declaration order.</summary>
        public IReadOnlyList<JsonStructureProperty> Properties => _properties;

        /// <summary>Gets the alternative required property sets of an <c>object</c> type.</summary>
        /// <remarks>
        /// A plain <c>required</c> array yields exactly one set. Alternative sets are mutually
        /// exclusive: exactly one set must match.
        /// </remarks>
        public IReadOnlyList<IReadOnlyList<string>> RequiredSets => _requiredSets;

        /// <summary>Gets a value indicating whether the element declares mutually exclusive required sets.</summary>
        public bool HasAlternativeRequiredSets => _requiredSets.Count > 1;

        /// <summary>Gets or sets the element schema of an <c>array</c> or <c>set</c> type.</summary>
        public JsonStructureSchema Items { get; set; }

        /// <summary>Gets or sets the value schema of a <c>map</c> type.</summary>
        public JsonStructureSchema Values { get; set; }

        /// <summary>Gets the declared element order of a <c>tuple</c> type.</summary>
        public IReadOnlyList<string> TupleOrder => _tupleOrder;

        /// <summary>Gets the variants of a <c>choice</c> type, in declaration order.</summary>
        public IReadOnlyList<JsonStructureChoice> Choices => _choices;

        /// <summary>Gets or sets the selector property name of an inline (untagged) <c>choice</c>.</summary>
        public string Selector { get; set; }

        /// <summary>Gets a value indicating whether a <c>choice</c> is an inline union rather than a tagged union.</summary>
        /// <remarks>Inline unions extend a common abstract base type and carry a selector property.</remarks>
        public bool IsInlineUnion => Kind == JsonStructureTypeKind.Choice && Extends != null;

        /// <summary>Gets or sets the <c>$extends</c> JSON Pointer of this type.</summary>
        public string Extends { get; set; }

        /// <summary>Gets or sets a value indicating whether the type is abstract.</summary>
        public bool IsAbstract { get; set; }

        /// <summary>Gets or sets whether additional properties are permitted, when declared as a boolean.</summary>
        public bool? AdditionalPropertiesAllowed { get; set; }

        /// <summary>Gets or sets the schema for additional properties, when declared as a schema.</summary>
        public JsonStructureSchema AdditionalProperties { get; set; }

        /// <summary>Gets or sets the <c>const</c> value.</summary>
        public JToken Const { get; set; }

        /// <summary>Gets the <c>enum</c> values.</summary>
        public IReadOnlyList<object> Enumeration => _enumeration;

        /// <summary>Gets or sets the <c>description</c>.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the <c>examples</c>.</summary>
        public JArray Examples { get; set; }

        /// <summary>
        /// Gets the annotation and add-in keywords carried by this element, such as
        /// <c>maxLength</c>, <c>precision</c>, <c>scale</c>, <c>pattern</c> or <c>altnames</c>.
        /// </summary>
        public IReadOnlyDictionary<string, JToken> Annotations => _annotations;

        /// <summary>Gets keywords that are not part of the recognized JSON Structure vocabulary.</summary>
        public IReadOnlyDictionary<string, JToken> ExtensionData => _extensionData;

        /// <summary>Gets or sets the original JSON of this element, used to round-trip documents losslessly.</summary>
        public JToken SourceJson { get; set; }

        /// <summary>Gets a value indicating whether the element declares a named, generatable type.</summary>
        public bool IsNamedType => JsonStructureTypeKinds.IsNameable(Kind);

        /// <summary>Adds a property.</summary>
        /// <param name="property">The property.</param>
        public void AddProperty(JsonStructureProperty property)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            _properties.Add(property);
            _propertyIndex[property.Name] = property;
        }

        /// <summary>Tries to get a declared property by name.</summary>
        /// <param name="name">The property name.</param>
        /// <param name="property">The property.</param>
        /// <returns><c>true</c> when the property is declared on this element.</returns>
        public bool TryGetProperty(string name, out JsonStructureProperty property)
        {
            if (name == null)
            {
                property = null;
                return false;
            }

            return _propertyIndex.TryGetValue(name, out property);
        }

        /// <summary>Adds a choice variant.</summary>
        /// <param name="choice">The choice.</param>
        public void AddChoice(JsonStructureChoice choice)
        {
            if (choice == null)
            {
                throw new ArgumentNullException(nameof(choice));
            }

            _choices.Add(choice);
        }

        /// <summary>Adds a union member.</summary>
        /// <param name="member">The union member.</param>
        public void AddUnionMember(JsonStructureTypeReference member)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            _union.Add(member);
        }

        /// <summary>Adds a required property set.</summary>
        /// <param name="names">The property names in the set.</param>
        public void AddRequiredSet(IReadOnlyList<string> names)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            _requiredSets.Add(names);
        }

        /// <summary>Adds a tuple element name, defining positional order.</summary>
        /// <param name="name">The property name.</param>
        public void AddTupleElement(string name)
        {
            _tupleOrder.Add(name);
        }

        /// <summary>Adds an enumeration value.</summary>
        /// <param name="value">The value.</param>
        public void AddEnumerationValue(object value)
        {
            _enumeration.Add(value);
        }

        /// <summary>Sets an annotation or add-in keyword.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <param name="value">The value.</param>
        public void SetAnnotation(string keyword, JToken value)
        {
            _annotations[keyword] = value;
        }

        /// <summary>Sets an unrecognized keyword.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <param name="value">The value.</param>
        public void SetExtensionData(string keyword, JToken value)
        {
            _extensionData[keyword] = value;
        }

        /// <summary>Gets a value indicating whether a property is required in every alternative set.</summary>
        /// <param name="name">The property name.</param>
        /// <returns><c>true</c> when the property is required unconditionally.</returns>
        public bool IsRequired(string name)
        {
            if (_requiredSets.Count == 0)
            {
                return false;
            }

            // A tuple's declared properties are implicitly required.
            return _requiredSets.All(set => set.Contains(name, StringComparer.Ordinal));
        }
    }
}
