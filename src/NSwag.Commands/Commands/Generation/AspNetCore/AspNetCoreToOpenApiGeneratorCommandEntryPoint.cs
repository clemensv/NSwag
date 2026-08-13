//-----------------------------------------------------------------------
// <copyright file="AspNetCoreToSwaggerGeneratorCommandEntryPoint.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/RicoSuter/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using System.Reflection;
using Newtonsoft.Json;
using NSwag.Generation;
using NSwag.Generation.AspNetCore;
using NSwag.JsonStructure;

#pragma warning disable CS0618

namespace NSwag.Commands.Generation.AspNetCore
{
    /// <summary>In-process entry point for the aspnetcore2swagger command.</summary>
    internal sealed class AspNetCoreToOpenApiGeneratorCommandEntryPoint
    {
        public static void Process(string commandContent, string outputFile, string applicationName)
        {
            var command = JsonConvert.DeserializeObject<AspNetCoreToOpenApiCommand>(commandContent);
            var previousWorkingDirectory = command.ChangeWorkingDirectoryAndSetAspNetCoreEnvironment();

            var assemblyName = new AssemblyName(applicationName);
            var assembly = Assembly.Load(assemblyName);
            var serviceProvider = ServiceProviderResolver.GetServiceProvider(assembly);

            var generator = serviceProvider.GetService(typeof(AspNetCoreOpenApiDocumentGenerator))
                as AspNetCoreOpenApiDocumentGenerator;
            if (generator != null)
            {
                generator.Settings.SchemaDialect = command.SchemaDialect;
                generator.Settings.JsonStructureDialect = command.JsonStructureDialect;
                generator.Settings.JsonStructureImportPolicy.AllowNetwork = command.JsonStructureAllowNetwork;
                generator.Settings.JsonStructureImportPolicy.AllowFileSystem = command.JsonStructureAllowFileSystem;
                generator.Settings.JsonStructureImportPolicy.BaseDirectory = command.JsonStructureBaseDirectory;
                generator.Settings.JsonStructureImportPolicy.MaxDocumentSize = command.JsonStructureMaxDocumentSize;
                generator.Settings.JsonStructureImportPolicy.Timeout = TimeSpan.FromSeconds(command.JsonStructureImportTimeout);
                generator.Settings.JsonStructureImportPolicy.AllowedHosts.Clear();
                foreach (var host in command.JsonStructureAllowedHosts)
                {
                    generator.Settings.JsonStructureImportPolicy.AllowedHosts.Add(host);
                }
                foreach (var uri in command.JsonStructureDerivedMetaSchemaAllowlist)
                {
                    if (!generator.Settings.JsonStructureDerivedMetaSchemaAllowlist.Contains(uri))
                    {
                        generator.Settings.JsonStructureDerivedMetaSchemaAllowlist.Add(uri);
                    }
                }
            }

            var document = command.GenerateDocumentAsync(serviceProvider, previousWorkingDirectory).GetAwaiter().GetResult();
            var json = document.ToJson();

            var outputPathDirectory = Path.GetDirectoryName(outputFile);
            Directory.CreateDirectory(outputPathDirectory);
            File.WriteAllText(outputFile, json);
        }
    }
}
