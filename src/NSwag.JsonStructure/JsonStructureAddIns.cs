//-----------------------------------------------------------------------
// <copyright file="JsonStructureAddIns.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure
{
    /// <summary>The JSON Structure add-in vocabularies that can be activated through <c>$uses</c>.</summary>
    [Flags]
    public enum JsonStructureAddIns
    {
        /// <summary>No add-in is active.</summary>
        None = 0,

        /// <summary>The <c>JSONStructureImport</c> add-in, enabling <c>$import</c> and <c>$importdefs</c>.</summary>
        Import = 1,

        /// <summary>The <c>JSONStructureValidation</c> add-in.</summary>
        Validation = 2,

        /// <summary>The <c>JSONStructureConditionalComposition</c> add-in.</summary>
        ConditionalComposition = 4,

        /// <summary>The <c>JSONStructureAlternateNames</c> add-in.</summary>
        AlternateNames = 8,

        /// <summary>The <c>JSONStructureUnits</c> add-in.</summary>
        Units = 16
    }
}
