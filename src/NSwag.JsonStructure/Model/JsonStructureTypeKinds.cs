//-----------------------------------------------------------------------
// <copyright file="JsonStructureTypeKinds.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>Maps JSON Structure type names to <see cref="JsonStructureTypeKind"/> values.</summary>
    public static class JsonStructureTypeKinds
    {
        private static readonly Dictionary<string, JsonStructureTypeKind> _byName =
            new Dictionary<string, JsonStructureTypeKind>(StringComparer.Ordinal)
            {
                ["null"] = JsonStructureTypeKind.Null,
                ["boolean"] = JsonStructureTypeKind.Boolean,
                ["string"] = JsonStructureTypeKind.String,
                ["number"] = JsonStructureTypeKind.Number,
                ["integer"] = JsonStructureTypeKind.Integer,
                ["int8"] = JsonStructureTypeKind.Int8,
                ["uint8"] = JsonStructureTypeKind.UInt8,
                ["int16"] = JsonStructureTypeKind.Int16,
                ["uint16"] = JsonStructureTypeKind.UInt16,
                ["int32"] = JsonStructureTypeKind.Int32,
                ["uint32"] = JsonStructureTypeKind.UInt32,
                ["int64"] = JsonStructureTypeKind.Int64,
                ["uint64"] = JsonStructureTypeKind.UInt64,
                ["int128"] = JsonStructureTypeKind.Int128,
                ["uint128"] = JsonStructureTypeKind.UInt128,
                ["float8"] = JsonStructureTypeKind.Float8,
                ["float"] = JsonStructureTypeKind.Float,
                ["double"] = JsonStructureTypeKind.Double,
                ["decimal"] = JsonStructureTypeKind.Decimal,
                ["binary"] = JsonStructureTypeKind.Binary,
                ["date"] = JsonStructureTypeKind.Date,
                ["datetime"] = JsonStructureTypeKind.DateTime,
                ["time"] = JsonStructureTypeKind.Time,
                ["duration"] = JsonStructureTypeKind.Duration,
                ["uuid"] = JsonStructureTypeKind.Uuid,
                ["uri"] = JsonStructureTypeKind.Uri,
                ["jsonpointer"] = JsonStructureTypeKind.JsonPointer,
                ["object"] = JsonStructureTypeKind.Object,
                ["array"] = JsonStructureTypeKind.Array,
                ["set"] = JsonStructureTypeKind.Set,
                ["map"] = JsonStructureTypeKind.Map,
                ["tuple"] = JsonStructureTypeKind.Tuple,
                ["choice"] = JsonStructureTypeKind.Choice,
                ["any"] = JsonStructureTypeKind.Any
            };

        private static readonly Dictionary<JsonStructureTypeKind, string> _names =
            _byName.ToDictionary(p => p.Value, p => p.Key);

        /// <summary>Tries to resolve a JSON Structure type name.</summary>
        /// <param name="name">The type name.</param>
        /// <param name="kind">The resolved kind.</param>
        /// <returns><c>true</c> when the name is a known JSON Structure type name.</returns>
        public static bool TryParse(string name, out JsonStructureTypeKind kind)
        {
            if (name == null)
            {
                kind = JsonStructureTypeKind.None;
                return false;
            }

            return _byName.TryGetValue(name, out kind);
        }

        /// <summary>Gets the JSON Structure type name for a kind.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The type name, or <c>null</c> for <see cref="JsonStructureTypeKind.None"/>.</returns>
        public static string GetName(JsonStructureTypeKind kind)
        {
            return _names.TryGetValue(kind, out var name) ? name : null;
        }

        /// <summary>Gets a value indicating whether the kind is a compound type.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns><c>true</c> for object, array, set, map, tuple and choice.</returns>
        public static bool IsCompound(JsonStructureTypeKind kind)
        {
            return kind is JsonStructureTypeKind.Object
                or JsonStructureTypeKind.Array
                or JsonStructureTypeKind.Set
                or JsonStructureTypeKind.Map
                or JsonStructureTypeKind.Tuple
                or JsonStructureTypeKind.Choice;
        }

        /// <summary>Gets a value indicating whether the kind is a named compound type.</summary>
        /// <remarks>Named types are the ones that become generated classes.</remarks>
        /// <param name="kind">The kind.</param>
        /// <returns><c>true</c> for object, tuple and choice.</returns>
        public static bool IsNameable(JsonStructureTypeKind kind)
        {
            return kind is JsonStructureTypeKind.Object
                or JsonStructureTypeKind.Tuple
                or JsonStructureTypeKind.Choice;
        }

        /// <summary>Gets a value indicating whether the kind is a numeric type.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns><c>true</c> for all integer and floating point types.</returns>
        public static bool IsNumeric(JsonStructureTypeKind kind)
        {
            return kind is JsonStructureTypeKind.Number
                or JsonStructureTypeKind.Integer
                or JsonStructureTypeKind.Int8
                or JsonStructureTypeKind.UInt8
                or JsonStructureTypeKind.Int16
                or JsonStructureTypeKind.UInt16
                or JsonStructureTypeKind.Int32
                or JsonStructureTypeKind.UInt32
                or JsonStructureTypeKind.Int64
                or JsonStructureTypeKind.UInt64
                or JsonStructureTypeKind.Int128
                or JsonStructureTypeKind.UInt128
                or JsonStructureTypeKind.Float8
                or JsonStructureTypeKind.Float
                or JsonStructureTypeKind.Double
                or JsonStructureTypeKind.Decimal;
        }
    }
}
