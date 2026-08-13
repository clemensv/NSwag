namespace NSwag.Generation
{
    /// <summary>Specifies the schema dialect used by the document generator.</summary>
    public enum SchemaDialect
    {
        /// <summary>Generate the existing NJsonSchema/OpenAPI schemas.</summary>
        JsonSchema,

        /// <summary>Generate JSON Structure schemas.</summary>
        JsonStructure
    }
}
