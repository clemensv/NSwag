//-----------------------------------------------------------------------
// <copyright file="JsonStructureImportPolicy.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace NSwag.JsonStructure.Import
{
    /// <summary>Controls whether and how JSON Structure <c>$import</c> and <c>$importdefs</c> targets may be loaded.</summary>
    public class JsonStructureImportPolicy
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureImportPolicy"/> class.</summary>
        public JsonStructureImportPolicy()
        {
            AllowedHosts = new Collection<string>();
            MaxDocumentSize = 1024 * 1024;
            Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Gets or sets a value indicating whether HTTP and HTTPS imports may be fetched.</summary>
        public bool AllowNetwork { get; set; }

        /// <summary>Gets or sets a value indicating whether local file imports may be read.</summary>
        public bool AllowFileSystem { get; set; }

        /// <summary>Gets the case-insensitive set of HTTP hosts that may be contacted when network imports are enabled.</summary>
        public Collection<string> AllowedHosts { get; }

        /// <summary>Gets or sets the maximum imported document size, in bytes.</summary>
        public long MaxDocumentSize { get; set; }

        /// <summary>Gets or sets the request timeout used for each imported document.</summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>Gets or sets the base directory for local file imports and relative file paths.</summary>
        public string BaseDirectory { get; set; }
    }
}
