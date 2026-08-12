//-----------------------------------------------------------------------
// <copyright file="JsonStructureNamedType.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Model
{
    /// <summary>A named type declaration within a namespace.</summary>
    public class JsonStructureNamedType
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureNamedType"/> class.</summary>
        /// <param name="name">The declared type name.</param>
        /// <param name="schema">The type schema.</param>
        /// <param name="declaringNamespace">The declaring namespace.</param>
        public JsonStructureNamedType(string name, JsonStructureSchema schema, JsonStructureNamespace declaringNamespace)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            Namespace = declaringNamespace;
        }

        /// <summary>Gets the type name.</summary>
        public string Name { get; }

        /// <summary>Gets the type schema.</summary>
        public JsonStructureSchema Schema { get; }

        /// <summary>Gets the declaring namespace.</summary>
        public JsonStructureNamespace Namespace { get; }

        /// <summary>Gets the JSON Pointer at which the type is declared.</summary>
        public string Pointer => Schema.Pointer;

        /// <summary>Gets the fully qualified, dot-separated name of the type.</summary>
        public string FullName
        {
            get
            {
                var path = Namespace?.Path;
                return string.IsNullOrEmpty(path) ? Name : path + "." + Name;
            }
        }
    }
}
