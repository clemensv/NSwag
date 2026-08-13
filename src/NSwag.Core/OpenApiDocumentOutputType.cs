namespace NSwag
{
    /// <summary>Specifies the OpenAPI document output mode.</summary>
    public enum OpenApiDocumentOutputType
    {
        /// <summary>Use the document's configured schema type (Swagger 2 or OpenAPI 3.0).</summary>
        Default,

        /// <summary>Write an OpenAPI 3.1 document containing JSON Structure schemas.</summary>
        OpenApi31JsonStructure
    }
}
