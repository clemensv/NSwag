using NJsonSchema.CodeGeneration;
using NJsonSchema.CodeGeneration.CSharp;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;

namespace NSwag.CodeGeneration.CSharp
{
    internal static class JsonStructureCSharpGenerator
    {
        public static IEnumerable<CodeArtifact> Generate(JsonStructureCodeGenerationContext context)
        {
            return Generate(context, new CSharpGeneratorSettings());
        }

        public static IEnumerable<CodeArtifact> Generate(JsonStructureCodeGenerationContext context, CSharpGeneratorSettings settings)
        {
            if (!context.Models.Any())
            {
                yield break;
            }

            var resolver = new JsonStructureCSharpTypeResolver(settings);
            var types = context.Models.SelectMany(entry => entry.Types)
                .SelectMany(type => CollectTypes(type, new HashSet<JsonStructureCodeGenerationNamedType>()))
                .ToList();
            foreach (var type in types)
            {
                var model = new JsonStructureCSharpTypeTemplateModel(type, context, resolver, settings);
                var template = settings.TemplateFactory.CreateTemplate("CSharp", "JsonStructure.Class", model);
                var code = template.Render();
                var namespacePath = context.GetNamespacePath(type);
                for (var index = namespacePath.Count - 1; index >= 0; index--)
                {
                    code = "namespace " + namespacePath[index] + "\n{\n" +
                        string.Join("\n", code.Split('\n').Select(line => "    " + line)) +
                        "\n}\n";
                }
                yield return new CodeArtifact(context.GetName(type), CodeArtifactType.Class,
                    CodeArtifactLanguage.CSharp, CodeArtifactCategory.Contract, code);
            }

            yield return new CodeArtifact("JsonStructureConverters", CodeArtifactType.Class,
                CodeArtifactLanguage.CSharp, CodeArtifactCategory.Contract, JsonStructureCSharpConverters.Generate(settings, types, context));
        }

        private static IEnumerable<JsonStructureCodeGenerationNamedType> CollectTypes(JsonStructureCodeGenerationNamedType type, ISet<JsonStructureCodeGenerationNamedType> seen)
        {
            if (!seen.Add(type))
            {
                yield break;
            }

            yield return type;
            foreach (var choice in type.Choices)
            {
                foreach (var nested in CollectTypes(choice.Type, seen))
                {
                    yield return nested;
                }
            }
            foreach (var property in type.Properties)
            {
                foreach (var nested in CollectTypes(property.Type, seen))
                {
                    yield return nested;
                }
            }
        }

        private static IEnumerable<JsonStructureCodeGenerationNamedType> CollectTypes(JsonStructureCodeGenerationTypeReference reference, ISet<JsonStructureCodeGenerationNamedType> seen)
        {
            if (reference == null)
            {
                yield break;
            }

            if (reference.InlineType != null)
            {
                foreach (var nested in CollectTypes(reference.InlineType, seen))
                {
                    yield return nested;
                }
            }

            foreach (var nestedReference in reference.TupleElements.Concat(reference.Union))
            {
                foreach (var nested in CollectTypes(nestedReference, seen))
                {
                    yield return nested;
                }
            }

            foreach (var nested in new[] { reference.ElementType, reference.ValueType })
            {
                foreach (var type in CollectTypes(nested, seen))
                {
                    yield return type;
                }
            }
        }
    }
}
