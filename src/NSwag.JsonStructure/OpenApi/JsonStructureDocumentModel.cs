//-----------------------------------------------------------------------
// <copyright file="JsonStructureDocumentModel.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using NSwag.JsonStructure.Model;

namespace NSwag.JsonStructure.OpenApi
{
    /// <summary>Contains JSON Structure documents lifted from an OpenAPI document.</summary>
    public sealed class JsonStructureDocumentModel
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureDocumentModel"/> class.</summary>
        /// <param name="liftedSchemas">The lifted JSON Structure documents keyed by OpenAPI JSON Pointer.</param>
        public JsonStructureDocumentModel(IEnumerable<KeyValuePair<string, JsonStructureDocument>> liftedSchemas)
        {
            if (liftedSchemas == null)
            {
                throw new ArgumentNullException(nameof(liftedSchemas));
            }

            var copy = new Dictionary<string, JsonStructureDocument>(StringComparer.Ordinal);
            foreach (var pair in liftedSchemas)
            {
                copy.Add(pair.Key, pair.Value);
            }

            LiftedSchemas = new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonStructureDocument>(copy);
        }

        /// <summary>Gets the lifted JSON Structure documents keyed by OpenAPI JSON Pointer.</summary>
        public IReadOnlyDictionary<string, JsonStructureDocument> LiftedSchemas { get; }

        /// <summary>Gets a lifted document by its OpenAPI JSON Pointer.</summary>
        /// <param name="pointer">The OpenAPI JSON Pointer correlation key.</param>
        /// <returns>The lifted document, or <c>null</c> when it is not present.</returns>
        public JsonStructureDocument Get(string pointer)
        {
            return LiftedSchemas.TryGetValue(pointer, out var document) ? document : null;
        }
    }
}
