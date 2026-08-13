using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NSwag
{
    internal static class JsonStructureRoundTrip
    {
        public static string TryRestore(OpenApiDocument document, string json, Formatting formatting)
        {
            var extensionsType = Type.GetType(
                "NSwag.JsonStructure.OpenApi.JsonStructureDocumentExtensions, NSwag.JsonStructure",
                throwOnError: false);
            var restore = extensionsType?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(method =>
                    method.Name == "RestoreJson" &&
                    method.GetParameters().Length == 3);
            if (restore == null)
            {
                return json;
            }

            try
            {
                return (string)restore.Invoke(null, [document, json, formatting]);
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        public static string TryRestore31(OpenApiDocument document, string json, Formatting formatting)
        {
            var extensionsType = Type.GetType(
                "NSwag.JsonStructure.OpenApi.JsonStructureDocumentExtensions, NSwag.JsonStructure",
                throwOnError: false);
            var restore = extensionsType?.GetMethod("RestoreJson31",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (restore == null)
            {
                throw new NotSupportedException(
                    "OpenAPI 3.1 JSON Structure output requires the NSwag.JsonStructure assembly.");
            }

            try
            {
                return (string)restore.Invoke(null, [document, json, formatting]);
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }
    }
}
