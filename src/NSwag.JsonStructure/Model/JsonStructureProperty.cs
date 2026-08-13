//-----------------------------------------------------------------------
// <copyright file="JsonStructureProperty.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>A named property of an <c>object</c> or <c>tuple</c> type.</summary>
    public class JsonStructureProperty
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureProperty"/> class.</summary>
        /// <param name="name">The property name as it appears in JSON.</param>
        /// <param name="schema">The property schema.</param>
        public JsonStructureProperty(string name, JsonStructureSchema schema)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        /// <summary>Gets the property name.</summary>
        public string Name { get; }

        /// <summary>Gets the property schema.</summary>
        public JsonStructureSchema Schema { get; }

        /// <summary>Gets or sets a value indicating whether the property is required.</summary>
        public bool IsRequired { get; set; }

        /// <summary>Gets or sets a value indicating whether the property is required only in some alternative required sets.</summary>
        public bool IsConditionallyRequired { get; set; }
    }
}
