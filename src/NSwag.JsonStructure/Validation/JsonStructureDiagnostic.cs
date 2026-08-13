//-----------------------------------------------------------------------
// <copyright file="JsonStructureDiagnostic.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Validation
{
    /// <summary>A positioned JSON Structure validation diagnostic.</summary>
    public sealed class JsonStructureDiagnostic
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureDiagnostic"/> class.</summary>
        /// <param name="severity">The diagnostic severity.</param>
        /// <param name="pointer">The JSON Pointer to the offending keyword or value.</param>
        /// <param name="message">The diagnostic message.</param>
        public JsonStructureDiagnostic(JsonStructureDiagnosticSeverity severity, string pointer, string message)
        {
            Severity = severity;
            Pointer = string.IsNullOrEmpty(pointer) ? "#" : pointer;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>Gets the diagnostic severity.</summary>
        public JsonStructureDiagnosticSeverity Severity { get; }

        /// <summary>Gets the JSON Pointer to the offending keyword or value.</summary>
        public string Pointer { get; }

        /// <summary>Gets the diagnostic message.</summary>
        public string Message { get; }

        /// <summary>Returns a display string for the diagnostic.</summary>
        /// <returns>The diagnostic text.</returns>
        public override string ToString()
        {
            return Severity + ": " + Message + " (at " + Pointer + ")";
        }
    }
}
