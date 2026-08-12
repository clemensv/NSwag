//-----------------------------------------------------------------------
// <copyright file="JsonStructurePreprocessResult.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.Model;

namespace NSwag.JsonStructure.OpenApi
{
    /// <summary>Contains an OpenAPI document after JSON Structure schemas have been lifted.</summary>
    public class JsonStructurePreprocessResult
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructurePreprocessResult"/> class.</summary>
        /// <param name="document">The preprocessed OpenAPI document.</param>
        /// <param name="liftedSchemas">The lifted JSON Structure documents keyed by correlation key.</param>
        public JsonStructurePreprocessResult(JObject document, IDictionary<string, JsonStructureDocument> liftedSchemas)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (liftedSchemas == null)
            {
                throw new ArgumentNullException(nameof(liftedSchemas));
            }

            Document = document;
            LiftedSchemas = new ReadOnlyDictionary<string, JsonStructureDocument>(
                liftedSchemas.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        /// <summary>Gets the preprocessed OpenAPI document.</summary>
        public JObject Document { get; }

        /// <summary>Gets the lifted JSON Structure documents keyed by their OpenAPI JSON Pointer correlation key.</summary>
        public IReadOnlyDictionary<string, JsonStructureDocument> LiftedSchemas { get; }

        /// <summary>Creates a side-channel model for attaching the lifted documents to an OpenAPI document.</summary>
        /// <returns>The side-channel model.</returns>
        public JsonStructureDocumentModel CreateDocumentModel()
        {
            return new JsonStructureDocumentModel(LiftedSchemas);
        }
    }
}
