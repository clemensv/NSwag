//-----------------------------------------------------------------------
// <copyright file="JsonStructureDialects.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure
{
    /// <summary>
    /// Recognizes JSON Structure meta-schema URIs.
    /// </summary>
    /// <remarks>
    /// The OAS binding requires exact, byte-for-byte comparison against the canonical URIs:
    /// no URI normalization, no trailing-slash equivalence and no version-range matching.
    /// Derived meta-schemas must be registered explicitly by the caller.
    /// </remarks>
    public class JsonStructureDialects
    {
        /// <summary>The canonical URI of the JSON Structure core meta-schema.</summary>
        public const string CoreUri = "https://json-structure.org/meta/core/v0/#";

        /// <summary>The canonical URI of the JSON Structure extended meta-schema.</summary>
        public const string ExtendedUri = "https://json-structure.org/meta/extended/v0/#";

        /// <summary>The canonical URI of the JSON Structure validation meta-schema.</summary>
        public const string ValidationUri = "https://json-structure.org/meta/validation/v0/#";

        private static readonly Dictionary<string, JsonStructureDialect> _canonical =
            new Dictionary<string, JsonStructureDialect>(StringComparer.Ordinal)
            {
                [CoreUri] = JsonStructureDialect.Core,
                [ExtendedUri] = JsonStructureDialect.Extended,
                [ValidationUri] = JsonStructureDialect.Validation
            };

        private readonly Dictionary<string, JsonStructureDialect> _derived =
            new Dictionary<string, JsonStructureDialect>(StringComparer.Ordinal);

        /// <summary>Gets the default recognizer, which knows only the three canonical dialects.</summary>
        public static JsonStructureDialects Default { get; } = new JsonStructureDialects();

        /// <summary>Gets the canonical meta-schema URIs.</summary>
        public static IEnumerable<string> CanonicalUris => _canonical.Keys;

        /// <summary>
        /// Registers a custom meta-schema URI that derives from one of the canonical dialects.
        /// </summary>
        /// <remarks>
        /// The binding permits tooling to be configured with derived meta-schemas, typically after
        /// resolving the meta-schema once and confirming that it imports a canonical meta-schema.
        /// Making that determination is the caller's responsibility.
        /// </remarks>
        /// <param name="uri">The derived meta-schema URI.</param>
        /// <param name="baseDialect">The canonical dialect it extends.</param>
        public void RegisterDerivedDialect(string uri, JsonStructureDialect baseDialect)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("The meta-schema URI must not be empty.", nameof(uri));
            }

            if (baseDialect == JsonStructureDialect.None)
            {
                throw new ArgumentException("A derived dialect must extend a canonical dialect.", nameof(baseDialect));
            }

            _derived[uri] = baseDialect;
        }

        /// <summary>Determines the dialect for a <c>$schema</c> value.</summary>
        /// <param name="schemaUri">The <c>$schema</c> value, which may be <c>null</c>.</param>
        /// <returns>The dialect, or <see cref="JsonStructureDialect.None"/> when the URI is not recognized.</returns>
        public JsonStructureDialect Resolve(string schemaUri)
        {
            if (string.IsNullOrEmpty(schemaUri))
            {
                return JsonStructureDialect.None;
            }

            if (_canonical.TryGetValue(schemaUri, out var dialect))
            {
                return dialect;
            }

            return _derived.TryGetValue(schemaUri, out var derived) ? derived : JsonStructureDialect.None;
        }

        /// <summary>Gets a value indicating whether a <c>$schema</c> value identifies a JSON Structure dialect.</summary>
        /// <param name="schemaUri">The <c>$schema</c> value.</param>
        /// <returns><c>true</c> when the URI is a recognized JSON Structure meta-schema.</returns>
        public bool IsJsonStructure(string schemaUri)
        {
            return Resolve(schemaUri) != JsonStructureDialect.None;
        }

        /// <summary>Gets a value indicating whether a URI looks like a JSON Structure meta-schema but is not recognized.</summary>
        /// <remarks>
        /// Used to produce actionable diagnostics: a schema pointing at, say, a v1 meta-schema must be
        /// rejected as an unknown dialect rather than silently processed as JSON Schema.
        /// </remarks>
        /// <param name="schemaUri">The <c>$schema</c> value.</param>
        /// <returns><c>true</c> when the URI is in the JSON Structure namespace but not recognized.</returns>
        public bool IsUnrecognizedJsonStructureUri(string schemaUri)
        {
            return !string.IsNullOrEmpty(schemaUri)
                && schemaUri.StartsWith("https://json-structure.org/", StringComparison.Ordinal)
                && Resolve(schemaUri) == JsonStructureDialect.None;
        }

        /// <summary>Gets the add-ins that are active by default for a dialect.</summary>
        /// <param name="dialect">The dialect.</param>
        /// <returns>The default add-ins.</returns>
        public static JsonStructureAddIns GetDefaultAddIns(JsonStructureDialect dialect)
        {
            return dialect switch
            {
                JsonStructureDialect.Core => JsonStructureAddIns.None,
                JsonStructureDialect.Extended => JsonStructureAddIns.Import,
                JsonStructureDialect.Validation => JsonStructureAddIns.Import
                    | JsonStructureAddIns.Validation
                    | JsonStructureAddIns.ConditionalComposition
                    | JsonStructureAddIns.AlternateNames
                    | JsonStructureAddIns.Units,
                _ => JsonStructureAddIns.None
            };
        }

        /// <summary>Gets the add-ins that a dialect offers for opt-in through <c>$uses</c>.</summary>
        /// <param name="dialect">The dialect.</param>
        /// <returns>The offered add-ins.</returns>
        public static JsonStructureAddIns GetOfferedAddIns(JsonStructureDialect dialect)
        {
            return dialect switch
            {
                JsonStructureDialect.Core => JsonStructureAddIns.None,
                JsonStructureDialect.Extended => JsonStructureAddIns.Import
                    | JsonStructureAddIns.Validation
                    | JsonStructureAddIns.ConditionalComposition
                    | JsonStructureAddIns.AlternateNames
                    | JsonStructureAddIns.Units,
                JsonStructureDialect.Validation => JsonStructureAddIns.Import
                    | JsonStructureAddIns.Validation
                    | JsonStructureAddIns.ConditionalComposition
                    | JsonStructureAddIns.AlternateNames
                    | JsonStructureAddIns.Units,
                _ => JsonStructureAddIns.None
            };
        }

        /// <summary>Resolves an add-in name as used in <c>$uses</c>.</summary>
        /// <param name="name">The add-in name.</param>
        /// <returns>The add-in, or <see cref="JsonStructureAddIns.None"/> when unknown.</returns>
        public static JsonStructureAddIns ResolveAddIn(string name)
        {
            return name switch
            {
                "JSONStructureImport" => JsonStructureAddIns.Import,
                "JSONStructureValidation" => JsonStructureAddIns.Validation,
                "JSONStructureConditionalComposition" => JsonStructureAddIns.ConditionalComposition,
                "JSONStructureAlternateNames" => JsonStructureAddIns.AlternateNames,
                "JSONStructureUnits" => JsonStructureAddIns.Units,
                _ => JsonStructureAddIns.None
            };
        }
    }
}
