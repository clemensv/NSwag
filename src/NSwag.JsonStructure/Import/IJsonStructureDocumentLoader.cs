//-----------------------------------------------------------------------
// <copyright file="IJsonStructureDocumentLoader.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

namespace NSwag.JsonStructure.Import
{
    /// <summary>Loads a JSON Structure import document from an already-authorized URI.</summary>
    public interface IJsonStructureDocumentLoader
    {
        /// <summary>Loads the document text for an import target.</summary>
        /// <param name="documentUri">The absolute document URI to load.</param>
        /// <param name="policy">The import policy for the current resolution run.</param>
        /// <param name="cancellationToken">A cancellation token that is canceled when the configured timeout expires.</param>
        /// <returns>The JSON document text.</returns>
        string Load(Uri documentUri, JsonStructureImportPolicy policy, CancellationToken cancellationToken);
    }
}
