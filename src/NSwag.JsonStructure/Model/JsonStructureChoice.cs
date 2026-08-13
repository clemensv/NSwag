//-----------------------------------------------------------------------
// <copyright file="JsonStructureChoice.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>A single variant of a <c>choice</c> type.</summary>
    public class JsonStructureChoice
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureChoice"/> class.</summary>
        /// <param name="name">The variant name, which is the tag or selector value.</param>
        /// <param name="schema">The variant schema.</param>
        public JsonStructureChoice(string name, JsonStructureSchema schema)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        /// <summary>Gets the variant name.</summary>
        /// <remarks>
        /// For a tagged union this is the wrapping property name; for an inline union it is the
        /// value written into the selector property.
        /// </remarks>
        public string Name { get; }

        /// <summary>Gets the variant schema.</summary>
        public JsonStructureSchema Schema { get; }
    }
}
