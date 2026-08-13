//-----------------------------------------------------------------------
// <copyright file="DefaultJsonStructureDocumentLoader.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
//-----------------------------------------------------------------------

using System.Net.Http;
using System.Text;

namespace NSwag.JsonStructure.Import
{
    /// <summary>Default loader for JSON Structure import documents.</summary>
    public sealed class DefaultJsonStructureDocumentLoader : IJsonStructureDocumentLoader
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <inheritdoc />
        public string Load(Uri documentUri, JsonStructureImportPolicy policy, CancellationToken cancellationToken)
        {
            if (documentUri == null)
            {
                throw new ArgumentNullException(nameof(documentUri));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (documentUri.IsFile)
            {
                using (var stream = File.OpenRead(documentUri.LocalPath))
                {
                    return ReadCapped(stream, policy.MaxDocumentSize, cancellationToken);
                }
            }

            using (var response = _httpClient.GetAsync(
                documentUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > policy.MaxDocumentSize)
                {
                    throw new JsonStructureException(
                        "The imported document '" + documentUri + "' exceeds the configured maximum size of " +
                        policy.MaxDocumentSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes.", "#");
                }

#if NET8_0_OR_GREATER
                using (var stream = response.Content.ReadAsStreamAsync(CancellationToken.None).GetAwaiter().GetResult())
#else
                using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
#endif
                {
                    return ReadCapped(stream, policy.MaxDocumentSize, cancellationToken);
                }
            }
        }

        private static string ReadCapped(Stream stream, long maxBytes, CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                throw new JsonStructureException("The import maximum document size must be greater than zero.", "#");
            }

            var buffer = new byte[8192];
            long total = 0;

            using (var memory = new MemoryStream())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > maxBytes)
                    {
                        throw new JsonStructureException(
                            "The imported document exceeds the configured maximum size of " +
                            maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes.", "#");
                    }

                    memory.Write(buffer, 0, read);
                }

                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }
    }
}
