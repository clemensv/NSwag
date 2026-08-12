//-----------------------------------------------------------------------
// <copyright file="JsonStructureDialect.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure
{
    /// <summary>The canonical JSON Structure dialects recognized by this binding implementation.</summary>
    public enum JsonStructureDialect
    {
        /// <summary>Not a JSON Structure dialect.</summary>
        None,

        /// <summary>The core meta-schema: core types and keywords only.</summary>
        Core,

        /// <summary>The extended meta-schema: core plus <c>JSONStructureImport</c>, with other add-ins available via <c>$uses</c>.</summary>
        Extended,

        /// <summary>The validation meta-schema: the extended meta-schema with all add-ins active by default.</summary>
        Validation
    }
}
