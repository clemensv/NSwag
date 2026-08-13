//-----------------------------------------------------------------------
// <copyright file="JsonStructureDiagnosticSeverity.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Validation
{
    /// <summary>Severity of a JSON Structure validation diagnostic.</summary>
    public enum JsonStructureDiagnosticSeverity
    {
        /// <summary>The document violates the JSON Structure meta-schema or prose rules.</summary>
        Error,

        /// <summary>The document is valid but carries a condition callers may want to surface.</summary>
        Warning
    }
}
