//-----------------------------------------------------------------------
// <copyright file="JsonStructureDocument.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using Newtonsoft.Json.Linq;

namespace NSwag.JsonStructure.Model
{
    /// <summary>A parsed JSON Structure schema document.</summary>
    public class JsonStructureDocument
    {
        /// <summary>Initializes a new instance of the <see cref="JsonStructureDocument"/> class.</summary>
        public JsonStructureDocument()
        {
            Definitions = new JsonStructureNamespace(null, null);
        }

        /// <summary>Gets or sets the <c>$id</c> of the document.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the <c>$schema</c> meta-schema URI.</summary>
        public string SchemaUri { get; set; }

        /// <summary>Gets or sets the document <c>name</c>.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the recognized dialect.</summary>
        public JsonStructureDialect Dialect { get; set; }

        /// <summary>Gets or sets the add-ins that are active for this document.</summary>
        public JsonStructureAddIns AddIns { get; set; }

        /// <summary>Gets or sets the add-ins offered by this document through <c>$offers</c>.</summary>
        public JsonStructureAddIns OfferedAddIns { get; set; }

        /// <summary>Gets or sets the root type declared inline with <c>type</c>, if any.</summary>
        public JsonStructureSchema RootSchema { get; set; }

        /// <summary>Gets or sets the <c>$root</c> JSON Pointer, if any.</summary>
        public string RootPointer { get; set; }

        /// <summary>Gets the root of the namespace hierarchy declared under <c>definitions</c>.</summary>
        public JsonStructureNamespace Definitions { get; }

        /// <summary>Gets or sets the original document JSON, used to round-trip losslessly.</summary>
        public JToken SourceJson { get; set; }

        /// <summary>Gets or sets the document path or URI this document was loaded from.</summary>
        public string DocumentPath { get; set; }

        /// <summary>Gets a value indicating whether an add-in is active.</summary>
        /// <param name="addIn">The add-in.</param>
        /// <returns><c>true</c> when the add-in is active for this document.</returns>
        public bool IsAddInActive(JsonStructureAddIns addIn)
        {
            return (AddIns & addIn) == addIn;
        }

        /// <summary>Enumerates every named type declared in the document.</summary>
        /// <returns>The named types.</returns>
        public IEnumerable<JsonStructureNamedType> GetAllTypes()
        {
            return Definitions.GetAllTypes();
        }
    }
}
