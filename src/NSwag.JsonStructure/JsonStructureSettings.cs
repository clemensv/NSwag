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
        }

        /// <summary>Gets or sets the dialect used for generated schemas.</summary>
        public JsonStructureDialect Dialect { get; set; }

        /// <summary>Gets the import policy used while resolving imported schemas.</summary>
        public JsonStructureImportPolicy ImportPolicy { get; }

        /// <summary>Gets the derived meta-schema URIs accepted as JSON Structure dialects.</summary>
        public Collection<string> DerivedMetaSchemaAllowlist { get; }

        /// <summary>Creates a dialect recognizer using the configured derived meta-schema allowlist.</summary>
        public JsonStructureDialects CreateDialects()
        {
            var dialects = new JsonStructureDialects();
            foreach (var uri in DerivedMetaSchemaAllowlist)
            {
                dialects.RegisterDerivedDialect(uri, Dialect == JsonStructureDialect.None ? JsonStructureDialect.Extended : Dialect);
            }

            return dialects;
        }
    }
}
