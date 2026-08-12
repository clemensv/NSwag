//-----------------------------------------------------------------------
// <copyright file="OutputCommandBase.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using NConsole;
using Newtonsoft.Json;
using NJsonSchema;
using NJsonSchema.Infrastructure;
using NSwag.JsonStructure;
using NSwag.Generation;

namespace NSwag.Commands
{
    public abstract class OutputCommandBase : IOutputCommand
    {
        [Argument(Name = "Output", IsRequired = false, Description = "The output file path (optional).")]
        [JsonProperty("output", NullValueHandling = NullValueHandling.Include)]
        public string OutputFilePath { get; set; }

        [Argument(Name = "NewLineBehavior", IsRequired = false, Description = "The new line behavior (Auto (OS default), CRLF, LF).")]
        [JsonProperty("newLineBehavior", NullValueHandling = NullValueHandling.Include)]
        public NewLineBehavior NewLineBehavior { get; set; } = NewLineBehavior.Auto;

        [Argument(Name = "OutputType", IsRequired = false, Description = "The document output type (Default or OpenApi31JsonStructure).")]
        [JsonProperty("outputType", NullValueHandling = NullValueHandling.Include)]
        public OpenApiDocumentOutputType OutputType { get; set; } = OpenApiDocumentOutputType.Default;

        [Argument(Name = "SchemaDialect", IsRequired = false, Description = "The schema dialect (JsonSchema or JsonStructure).")]
        [JsonProperty("schemaDialect", NullValueHandling = NullValueHandling.Include)]
        public SchemaDialect SchemaDialect { get; set; } = SchemaDialect.JsonSchema;

        [JsonProperty("jsonStructure", NullValueHandling = NullValueHandling.Ignore)]
        public JsonStructureSettings JsonStructure { get; set; } = new JsonStructureSettings();

        [Argument(Name = "JsonStructureDialect", IsRequired = false, Description = "The JSON Structure dialect (Core, Extended, or Validation).")]
        public JsonStructureDialect JsonStructureDialect
        {
            get => JsonStructure.Dialect;
            set => JsonStructure.Dialect = value;
        }

        [Argument(Name = "JsonStructureAllowNetwork", IsRequired = false, Description = "Allow network JSON Structure imports (default: false).")]
        public bool JsonStructureAllowNetwork
        {
            get => JsonStructure.ImportPolicy.AllowNetwork;
            set => JsonStructure.ImportPolicy.AllowNetwork = value;
        }

        [Argument(Name = "JsonStructureAllowFileSystem", IsRequired = false, Description = "Allow local file JSON Structure imports (default: false).")]
        public bool JsonStructureAllowFileSystem
        {
            get => JsonStructure.ImportPolicy.AllowFileSystem;
            set => JsonStructure.ImportPolicy.AllowFileSystem = value;
        }

        [Argument(Name = "JsonStructureAllowedHosts", IsRequired = false, Description = "Allowlisted hosts for network JSON Structure imports.")]
        public string[] JsonStructureAllowedHosts
        {
            get => JsonStructure.ImportPolicy.AllowedHosts.ToArray();
            set
            {
                JsonStructure.ImportPolicy.AllowedHosts.Clear();
                if (value != null)
                {
                    foreach (var host in value) JsonStructure.ImportPolicy.AllowedHosts.Add(host);
                }
            }
        }

        [Argument(Name = "JsonStructureMaxDocumentSize", IsRequired = false, Description = "Maximum imported JSON Structure document size in bytes.")]
        public long JsonStructureMaxDocumentSize
        {
            get => JsonStructure.ImportPolicy.MaxDocumentSize;
            set => JsonStructure.ImportPolicy.MaxDocumentSize = value;
        }

        [Argument(Name = "JsonStructureImportTimeout", IsRequired = false, Description = "Timeout for each JSON Structure import in seconds.")]
        public double JsonStructureImportTimeout
        {
            get => JsonStructure.ImportPolicy.Timeout.TotalSeconds;
            set => JsonStructure.ImportPolicy.Timeout = TimeSpan.FromSeconds(value);
        }

        [Argument(Name = "JsonStructureBaseDirectory", IsRequired = false, Description = "Base directory for local JSON Structure imports.")]
        public string JsonStructureBaseDirectory
        {
            get => JsonStructure.ImportPolicy.BaseDirectory;
            set => JsonStructure.ImportPolicy.BaseDirectory = value;
        }

        [Argument(Name = "JsonStructureDerivedMetaSchemaAllowlist", IsRequired = false, Description = "Allowlisted derived JSON Structure meta-schema URIs.")]
        public string[] JsonStructureDerivedMetaSchemaAllowlist
        {
            get => JsonStructure.DerivedMetaSchemaAllowlist.ToArray();
            set
            {
                JsonStructure.DerivedMetaSchemaAllowlist.Clear();
                if (value != null)
                {
                    foreach (var uri in value) JsonStructure.DerivedMetaSchemaAllowlist.Add(uri);
                }
            }
        }

        public abstract Task<object> RunAsync(CommandLineProcessor processor, IConsoleHost host);

        protected static Task<OpenApiDocument> ReadSwaggerDocumentAsync(string input)
        {
            return ReadSwaggerDocumentAsync(input, null);
        }

        protected static Task<OpenApiDocument> ReadSwaggerDocumentAsync(string input, JsonStructureSettings jsonStructure)
        {
            if (!IsJson(input) && !IsYaml(input))
            {
                if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (input.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                        input.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    {
                        return OpenApiYamlDocument.FromUrlAsync(input);
                    }
                    else
                    {
                        return OpenApiDocument.FromJsonAsync(DynamicApis.HttpGetAsync(input, default).GetAwaiter().GetResult(), input,
                            SchemaType.Swagger2, new OpenApiDocumentLoadSettings { JsonStructureSettings = jsonStructure });
                    }
                }
                else
                {
                    if (input.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                        input.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    {
                        return OpenApiYamlDocument.FromFileAsync(input);
                    }
                    else
                    {
                        return OpenApiDocument.FromJsonAsync(File.ReadAllText(input), input,
                            SchemaType.Swagger2, new OpenApiDocumentLoadSettings { JsonStructureSettings = jsonStructure });
                    }
                }
            }
            else
            {
                if (IsYaml(input))
                {
                    return OpenApiYamlDocument.FromYamlAsync(input);
                }
                else
                {
                    return OpenApiDocument.FromJsonAsync(input, null, SchemaType.Swagger2,
                        new OpenApiDocumentLoadSettings { JsonStructureSettings = jsonStructure });
                }
            }
        }

        protected static bool IsJson(string data)
        {
            return data.StartsWith('{');
        }

        protected static bool IsYaml(string data)
        {
            return !IsJson(data) && data.Contains('\n');
        }

        protected Task<bool> TryWriteFileOutputAsync(IConsoleHost host, Func<string> generator)
        {
            return OutputCommandExtensions.TryWriteFileOutputAsync(this, host, NewLineBehavior, generator);
        }

        protected Task<bool> TryWriteDocumentOutputAsync(IConsoleHost host, Func<OpenApiDocument> generator)
        {
            return OutputCommandExtensions.TryWriteDocumentOutputAsync(this, host, NewLineBehavior, generator);
        }

        protected Task<bool> TryWriteFileOutputAsync(string path, IConsoleHost host, Func<string> generator)
        {
            return OutputCommandExtensions.TryWriteFileOutputAsync(this, path, host, NewLineBehavior, generator);
        }
    }
}