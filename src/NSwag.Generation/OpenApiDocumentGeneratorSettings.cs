//-----------------------------------------------------------------------
// <copyright file="SwaggerGeneratorSettings.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using Namotion.Reflection;
using Newtonsoft.Json;
using NJsonSchema;
using NJsonSchema.Generation;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Collections;
using NSwag.Generation.Processors.Contexts;
using NSwag.JsonStructure;
using NSwag.JsonStructure.Import;

namespace NSwag.Generation
{
    /// <summary>Settings for the Swagger generator.</summary>
    public class OpenApiDocumentGeneratorSettings
    {
        /// <summary>Initializes a new instance of the <see cref="OpenApiDocumentGeneratorSettings"/> class.</summary>
        public OpenApiDocumentGeneratorSettings()
        {
            SchemaGeneratorFactory = () => new OpenApiSchemaGenerator(this);
            DefaultResponseReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;
        }

        /// <summary>Gets or sets the JSON Schema generator factory (default: new instance of <see cref="OpenApiSchemaGenerator"/>.</summary>
        public Func<OpenApiSchemaGenerator> SchemaGeneratorFactory { get; set; }

        /// <summary></summary>
        public JsonSchemaGeneratorSettings SchemaSettings { get; set; } = new SystemTextJsonSchemaGeneratorSettings
        {
            SchemaType = SchemaType.OpenApi3
        };

        /// <summary>Gets or sets the schema dialect used by generated parameters, request bodies, and responses.</summary>
        public SchemaDialect SchemaDialect { get; set; } = global::NSwag.Generation.SchemaDialect.JsonSchema;

        /// <summary>Gets or sets the JSON Structure dialect used when <see cref="SchemaDialect"/> is JSON Structure.</summary>
        public JsonStructureDialect JsonStructureDialect { get; set; } = JsonStructureDialect.Extended;

        /// <summary>Gets the JSON Structure import policy.</summary>
        public JsonStructureImportPolicy JsonStructureImportPolicy { get; } = new JsonStructureImportPolicy();

        /// <summary>Gets the allowlist of derived JSON Structure meta-schema URIs.</summary>
        public ICollection<string> JsonStructureDerivedMetaSchemaAllowlist { get; } = [];

        /// <summary>Generates a schema using the configured dialect.</summary>
        /// <param name="document">The document receiving the schema.</param>
        /// <param name="type">The CLR type.</param>
        /// <param name="nullable">Whether the schema is nullable.</param>
        /// <param name="resolver">The existing schema resolver.</param>
        /// <param name="generator">The existing schema generator.</param>
        /// <returns>The generated schema.</returns>
        public JsonSchema GenerateSchema(OpenApiDocument document, ContextualType type, bool nullable, JsonSchemaResolver resolver = null, OpenApiSchemaGenerator generator = null)
        {
            if (SchemaDialect == global::NSwag.Generation.SchemaDialect.JsonSchema)
            {
                return (generator ?? SchemaGeneratorFactory()).GenerateWithReferenceAndNullability<JsonSchema>(
                    type, nullable, resolver ?? new OpenApiSchemaResolver(document, SchemaSettings));
            }

            return JsonStructureSchemaBridge.Create(this, document, type);
        }

        /// <summary>Gets or sets the Swagger specification title.</summary>
        public string Title { get; set; } = "My Title";

        /// <summary>Gets or sets the Swagger specification description.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the Swagger specification version.</summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>Gets or sets a value indicating whether nullable body parameters are allowed (ignored when MvcOptions.AllowEmptyInputInBodyModelBinding is available (ASP.NET Core 2.0+), default: true).</summary>
        public bool AllowNullableBodyParameters { get; set; } = true;

        /// <summary>Gets or sets the default response reference type null handling when no nullability information is available (if NotNullAttribute and CanBeNullAttribute are missing, default: NotNull).</summary>
        public ReferenceTypeNullHandling DefaultResponseReferenceTypeNullHandling { get; set; }

        /// <summary>Gets or sets a value indicating whether to generate x-originalName properties when parameter name is different in .NET and HTTP (default: true).</summary>
        public bool GenerateOriginalParameterNames { get; set; } = true;

        /// <summary>Gets the operation processors.</summary>
        [JsonIgnore]
        public OperationProcessorCollection OperationProcessors { get; } =
        [
            new OperationSummaryAndDescriptionProcessor(),
            new OperationTagsProcessor(),
            new OperationExtensionDataProcessor()
        ];

        /// <summary>Gets the document processors.</summary>
        [JsonIgnore]
        public DocumentProcessorCollection DocumentProcessors { get; } =
        [
            new DocumentTagsProcessor(),
            new DocumentExtensionDataProcessor()
        ];

        /// <summary>Gets or sets the document template representing the initial Swagger specification (JSON data).</summary>
        public string DocumentTemplate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether controllers' XML documentation will be used as tag descriptions (but only when the controller name is used as a tag, default: false).
        /// </summary>
        public bool UseControllerSummaryAsTagDescription { get; set; }

        /// <summary>Gets or sets a value indicating whether the HttpMethodAttribute Name property shall be used as OperationId.</summary>
        public bool UseHttpAttributeNameAsOperationId { get; set; }

        /// <summary>Inserts a function based operation processor at the beginning of the pipeline to be used to filter operations.</summary>
        /// <param name="filter">The processor filter.</param>
        public void AddOperationFilter(Func<OperationProcessorContext, bool> filter)
        {
            OperationProcessors.Insert(0, new OperationProcessor(filter));
        }

        /// <summary>Applies the given settings to this settings object.</summary>
        /// <param name="schemaSettings">The schema generator settings.</param>
        /// <param name="mvcOptions">The MVC options.</param>
        public void ApplySettings(JsonSchemaGeneratorSettings schemaSettings, object mvcOptions)
        {
            if (schemaSettings != null)
            {
                SchemaSettings = schemaSettings;
            }

            if (mvcOptions != null && mvcOptions.HasProperty("AllowEmptyInputInBodyModelBinding"))
            {
                AllowNullableBodyParameters = mvcOptions.TryGetPropertyValue("AllowEmptyInputInBodyModelBinding", false);
            }
        }
    }
}
