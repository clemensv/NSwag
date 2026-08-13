//-----------------------------------------------------------------------
// <copyright file="JsonStructureSettings.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using System.Collections.ObjectModel;
using NSwag.JsonStructure.Import;

namespace NSwag.JsonStructure
{
    /// <summary>Configuration used when reading or generating JSON Structure schemas.</summary>
    public class JsonStructureSettings
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureSettings"/> class.</summary>
        public JsonStructureSettings()
        {
            Dialect = JsonStructureDialect.Extended;
            ImportPolicy = new JsonStructureImportPolicy();
            DerivedMetaSchemaAllowlist = new Collection<string>();
            DerivedMetaSchemaDialects = new Dictionary<string, JsonStructureDialect>(StringComparer.Ordinal);
        }

        /// <summary>Gets or sets the dialect used for generated schemas.</summary>
        public JsonStructureDialect Dialect { get; set; }

        /// <summary>Gets the import policy used while resolving imported schemas.</summary>
        public JsonStructureImportPolicy ImportPolicy { get; }

        /// <summary>Gets the derived meta-schema URIs accepted as JSON Structure dialects.</summary>
        public Collection<string> DerivedMetaSchemaAllowlist { get; }

        /// <summary>
        /// Gets the explicitly confirmed base dialect for each configured derived meta-schema URI.
        /// </summary>
        public IDictionary<string, JsonStructureDialect> DerivedMetaSchemaDialects { get; }

        /// <summary>
        /// Registers a derived meta-schema URI and the canonical dialect it extends.
        /// </summary>
        public void RegisterDerivedMetaSchema(string uri, JsonStructureDialect baseDialect)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("The meta-schema URI must not be empty.", nameof(uri));
            }

            if (baseDialect == JsonStructureDialect.None)
            {
                throw new ArgumentException("A derived meta-schema must extend a canonical dialect.", nameof(baseDialect));
            }

            DerivedMetaSchemaDialects[uri] = baseDialect;
        }

        /// <summary>Creates a dialect recognizer using the configured derived meta-schema allowlist.</summary>
        public JsonStructureDialects CreateDialects()
        {
            var dialects = new JsonStructureDialects();
            foreach (var pair in DerivedMetaSchemaDialects)
            {
                dialects.RegisterDerivedDialect(pair.Key, pair.Value);
            }

            foreach (var uri in DerivedMetaSchemaAllowlist)
            {
                if (!DerivedMetaSchemaDialects.ContainsKey(uri))
                {
                    dialects.RegisterDerivedDialect(uri, Dialect == JsonStructureDialect.None ? JsonStructureDialect.Extended : Dialect);
                }
            }

            return dialects;
        }
    }
}
