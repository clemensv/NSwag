//-----------------------------------------------------------------------
// <copyright file="JsonStructureKeywords.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure
{
    /// <summary>The JSON Structure keyword names.</summary>
    public static class JsonStructureKeywords
    {
        /// <summary>The <c>$schema</c> keyword.</summary>
        public const string Schema = "$schema";

        /// <summary>The <c>$id</c> keyword.</summary>
        public const string Id = "$id";

        /// <summary>The <c>$root</c> keyword.</summary>
        public const string Root = "$root";

        /// <summary>The <c>$ref</c> keyword.</summary>
        public const string Ref = "$ref";

        /// <summary>The <c>$extends</c> keyword.</summary>
        public const string Extends = "$extends";

        /// <summary>The <c>$uses</c> keyword.</summary>
        public const string Uses = "$uses";

        /// <summary>The <c>$offers</c> keyword.</summary>
        public const string Offers = "$offers";

        /// <summary>The <c>$import</c> keyword.</summary>
        public const string Import = "$import";

        /// <summary>The <c>$importdefs</c> keyword.</summary>
        public const string ImportDefs = "$importdefs";

        /// <summary>The <c>definitions</c> keyword.</summary>
        public const string Definitions = "definitions";

        /// <summary>The <c>name</c> keyword.</summary>
        public const string Name = "name";

        /// <summary>The <c>type</c> keyword.</summary>
        public const string Type = "type";

        /// <summary>The <c>properties</c> keyword.</summary>
        public const string Properties = "properties";

        /// <summary>The <c>required</c> keyword.</summary>
        public const string Required = "required";

        /// <summary>The <c>items</c> keyword.</summary>
        public const string Items = "items";

        /// <summary>The <c>values</c> keyword.</summary>
        public const string Values = "values";

        /// <summary>The <c>tuple</c> keyword.</summary>
        public const string Tuple = "tuple";

        /// <summary>The <c>choices</c> keyword.</summary>
        public const string Choices = "choices";

        /// <summary>The <c>selector</c> keyword.</summary>
        public const string Selector = "selector";

        /// <summary>The <c>abstract</c> keyword.</summary>
        public const string Abstract = "abstract";

        /// <summary>The <c>additionalProperties</c> keyword.</summary>
        public const string AdditionalProperties = "additionalProperties";

        /// <summary>The <c>const</c> keyword.</summary>
        public const string Const = "const";

        /// <summary>The <c>enum</c> keyword.</summary>
        public const string Enum = "enum";

        /// <summary>The <c>description</c> keyword.</summary>
        public const string Description = "description";

        /// <summary>The <c>examples</c> keyword.</summary>
        public const string Examples = "examples";

        /// <summary>The <c>altnames</c> keyword from the alternate names add-in.</summary>
        public const string AlternateNames = "altnames";

        /// <summary>The <c>altenums</c> keyword from the alternate names add-in.</summary>
        public const string AlternateEnums = "altenums";

        private static readonly HashSet<string> _structural = new(StringComparer.Ordinal)
        {
            Schema, Id, Root, Ref, Extends, Uses, Offers, Import, ImportDefs,
            Definitions, Name, Type, Properties, Required, Items, Values,
            Tuple, Choices, Selector, Abstract, AdditionalProperties,
            Const, Enum, Description, Examples
        };

        private static readonly HashSet<string> _annotations = new(StringComparer.Ordinal)
        {
            // Core type annotations.
            "maxLength", "precision", "scale",
            "contentEncoding", "contentCompression", "contentMediaType",

            // OpenAPI annotations that may travel with a schema object.
            "readOnly", "writeOnly", "deprecated", "title", "example"
        };

        private static readonly HashSet<string> _validationAddInKeywords = new(StringComparer.Ordinal)
        {
            "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
            "minLength", "pattern", "format", "minItems", "maxItems", "uniqueItems",
            "minProperties", "maxProperties"
        };

        private static readonly HashSet<string> _conditionalCompositionAddInKeywords = new(StringComparer.Ordinal)
        {
            "allOf", "anyOf", "oneOf", "not", "if", "then", "else",
        };

        private static readonly HashSet<string> _alternateNamesAddInKeywords = new(StringComparer.Ordinal)
        {
            AlternateNames, "altsymbols"
        };

        private static readonly HashSet<string> _unitsAddInKeywords = new(StringComparer.Ordinal)
        {
            "unit"
        };

        private static readonly HashSet<string> _importAddInKeywords = new(StringComparer.Ordinal)
        {
            Import, ImportDefs
        };

        /// <summary>Gets a value indicating whether a keyword is a recognized structural keyword.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <returns><c>true</c> when the keyword is structural.</returns>
        public static bool IsStructural(string keyword)
        {
            return keyword != null && _structural.Contains(keyword);
        }

        /// <summary>Gets a value indicating whether a keyword is a recognized annotation or add-in keyword.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <returns><c>true</c> when the keyword is an annotation.</returns>
        public static bool IsAnnotation(string keyword)
        {
            return keyword != null && (_annotations.Contains(keyword) || TryGetAddIn(keyword, out _));
        }

        /// <summary>Gets a value indicating whether a keyword is a core annotation that does not require an add-in.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <returns><c>true</c> when the keyword is always active for JSON Structure schemas.</returns>
        public static bool IsCoreAnnotation(string keyword)
        {
            return keyword != null && _annotations.Contains(keyword);
        }

        /// <summary>Gets the add-in vocabulary that contributes a keyword.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <param name="addIn">The add-in vocabulary that contributes the keyword.</param>
        /// <returns><c>true</c> when the keyword belongs to a known add-in vocabulary.</returns>
        public static bool TryGetAddIn(string keyword, out JsonStructureAddIns addIn)
        {
            if (keyword == null)
            {
                addIn = JsonStructureAddIns.None;
                return false;
            }

            if (_validationAddInKeywords.Contains(keyword))
            {
                addIn = JsonStructureAddIns.Validation;
                return true;
            }

            if (_conditionalCompositionAddInKeywords.Contains(keyword))
            {
                addIn = JsonStructureAddIns.ConditionalComposition;
                return true;
            }

            if (_alternateNamesAddInKeywords.Contains(keyword))
            {
                addIn = JsonStructureAddIns.AlternateNames;
                return true;
            }

            if (_unitsAddInKeywords.Contains(keyword))
            {
                addIn = JsonStructureAddIns.Units;
                return true;
            }

            if (_importAddInKeywords.Contains(keyword))
            {
                addIn = JsonStructureAddIns.Import;
                return true;
            }

            addIn = JsonStructureAddIns.None;
            return false;
        }

        /// <summary>Gets a value indicating whether a keyword is active for the supplied add-ins.</summary>
        /// <param name="keyword">The keyword.</param>
        /// <param name="activeAddIns">The add-ins active for the current document.</param>
        /// <returns><c>true</c> when the keyword is core or its contributing add-in is active.</returns>
        public static bool IsKeywordActive(string keyword, JsonStructureAddIns activeAddIns)
        {
            if (TryGetAddIn(keyword, out var addIn))
            {
                return (activeAddIns & addIn) == addIn;
            }

            if (IsCoreAnnotation(keyword) || IsStructural(keyword))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a value indicating whether a keyword can affect generated type shape.
        /// Conditional-composition keywords are validation-only and MUST NOT be used by
        /// code generators to add, remove, merge, or otherwise shape generated types.
        /// </summary>
        /// <param name="keyword">The keyword.</param>
        /// <returns><c>false</c> for validation-only keywords; otherwise <c>true</c>.</returns>
        public static bool IsTypeShaping(string keyword)
        {
            return keyword == null || !_conditionalCompositionAddInKeywords.Contains(keyword);
        }

        /// <summary>
        /// Gets a value indicating whether a keyword is validation-only metadata.
        /// Conditional-composition keywords constrain validation and MUST NOT shape generated types.
        /// </summary>
        /// <param name="keyword">The keyword.</param>
        /// <returns><c>true</c> when the keyword is validation-only.</returns>
        public static bool IsValidationOnly(string keyword)
        {
            return keyword != null && _conditionalCompositionAddInKeywords.Contains(keyword);
        }
    }
}
