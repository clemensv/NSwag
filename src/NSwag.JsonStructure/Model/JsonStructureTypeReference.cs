//-----------------------------------------------------------------------
// <copyright file="JsonStructureTypeReference.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>
    /// A member of a non-discriminated type union: either a primitive type name,
    /// a <c>$ref</c> type reference, or an inline compound type.
    /// </summary>
    public class JsonStructureTypeReference
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureTypeReference"/> class for a primitive type.</summary>
        /// <param name="kind">The primitive type kind.</param>
        public JsonStructureTypeReference(JsonStructureTypeKind kind)
        {
            Kind = kind;
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureTypeReference"/> class for a type reference.</summary>
        /// <param name="reference">The JSON Pointer of the referenced type.</param>
        public JsonStructureTypeReference(string reference)
        {
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        }

        /// <summary>Initializes a new instance of the <see cref="JsonStructureTypeReference"/> class for an inline compound type.</summary>
        /// <param name="schema">The inline schema.</param>
        public JsonStructureTypeReference(JsonStructureSchema schema)
        {
            InlineSchema = schema ?? throw new ArgumentNullException(nameof(schema));
            Kind = schema.Kind;
        }

        /// <summary>Gets the primitive type kind, when this member names a type directly.</summary>
        public JsonStructureTypeKind Kind { get; }

        /// <summary>Gets the JSON Pointer of the referenced type, when this member is a <c>$ref</c>.</summary>
        public string Reference { get; }

        /// <summary>Gets the inline schema, when this member declares a compound type inline.</summary>
        public JsonStructureSchema InlineSchema { get; }

        /// <summary>Gets a value indicating whether this member is the <c>null</c> type.</summary>
        public bool IsNull => Kind == JsonStructureTypeKind.Null;

        /// <summary>Gets a value indicating whether this member is a type reference.</summary>
        public bool IsReference => Reference != null;

        /// <summary>Gets or sets the type this member references, once resolved.</summary>
        public JsonStructureNamedType ResolvedReference { get; set; }
    }
}
