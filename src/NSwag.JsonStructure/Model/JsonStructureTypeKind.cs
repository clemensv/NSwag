//-----------------------------------------------------------------------
// <copyright file="JsonStructureTypeKind.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>The kind of a JSON Structure type.</summary>
    public enum JsonStructureTypeKind
    {
        /// <summary>No type was declared, or the type is expressed as a union or a type reference.</summary>
        None,

        /// <summary>The <c>null</c> primitive type.</summary>
        Null,

        /// <summary>The <c>boolean</c> primitive type.</summary>
        Boolean,

        /// <summary>The <c>string</c> primitive type.</summary>
        String,

        /// <summary>The <c>number</c> primitive type.</summary>
        Number,

        /// <summary>The <c>integer</c> primitive type.</summary>
        Integer,

        /// <summary>The <c>int8</c> extended primitive type.</summary>
        Int8,

        /// <summary>The <c>uint8</c> extended primitive type.</summary>
        UInt8,

        /// <summary>The <c>int16</c> extended primitive type.</summary>
        Int16,

        /// <summary>The <c>uint16</c> extended primitive type.</summary>
        UInt16,

        /// <summary>The <c>int32</c> extended primitive type.</summary>
        Int32,

        /// <summary>The <c>uint32</c> extended primitive type.</summary>
        UInt32,

        /// <summary>The <c>int64</c> extended primitive type.</summary>
        Int64,

        /// <summary>The <c>uint64</c> extended primitive type.</summary>
        UInt64,

        /// <summary>The <c>int128</c> extended primitive type.</summary>
        Int128,

        /// <summary>The <c>uint128</c> extended primitive type.</summary>
        UInt128,

        /// <summary>The <c>float8</c> extended primitive type.</summary>
        Float8,

        /// <summary>The <c>float</c> extended primitive type.</summary>
        Float,

        /// <summary>The <c>double</c> extended primitive type.</summary>
        Double,

        /// <summary>The <c>decimal</c> extended primitive type.</summary>
        Decimal,

        /// <summary>The <c>binary</c> extended primitive type.</summary>
        Binary,

        /// <summary>The <c>date</c> extended primitive type.</summary>
        Date,

        /// <summary>The <c>datetime</c> extended primitive type.</summary>
        DateTime,

        /// <summary>The <c>time</c> extended primitive type.</summary>
        Time,

        /// <summary>The <c>duration</c> extended primitive type.</summary>
        Duration,

        /// <summary>The <c>uuid</c> extended primitive type.</summary>
        Uuid,

        /// <summary>The <c>uri</c> extended primitive type.</summary>
        Uri,

        /// <summary>The <c>jsonpointer</c> extended primitive type.</summary>
        JsonPointer,

        /// <summary>The <c>object</c> compound type.</summary>
        Object,

        /// <summary>The <c>array</c> compound type.</summary>
        Array,

        /// <summary>The <c>set</c> compound type.</summary>
        Set,

        /// <summary>The <c>map</c> compound type.</summary>
        Map,

        /// <summary>The <c>tuple</c> compound type.</summary>
        Tuple,

        /// <summary>The <c>choice</c> compound type.</summary>
        Choice,

        /// <summary>The <c>any</c> compound type.</summary>
        Any
    }
}
