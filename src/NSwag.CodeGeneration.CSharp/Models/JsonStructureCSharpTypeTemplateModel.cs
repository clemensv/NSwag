using NJsonSchema.CodeGeneration.CSharp;
using Newtonsoft.Json.Linq;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;
using System.Security;

namespace NSwag.CodeGeneration.CSharp;

internal sealed class JsonStructureCSharpTypeTemplateModel
{
    public JsonStructureCSharpTypeTemplateModel(
        JsonStructureCodeGenerationNamedType type,
        JsonStructureCodeGenerationContext context,
        JsonStructureCSharpTypeResolver resolver,
        CSharpGeneratorSettings settings)
    {
        Name = context.GetLocalName(type);
        UseSystemTextJson = settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson;
        IsTuple = type.Kind == JsonStructureTypeKind.Tuple;
        IsChoice = type.Kind == JsonStructureTypeKind.Choice;
        IsInlineChoice = type.IsInlineChoice;
        Selector = type.Selector;
        ChoiceDiscriminatorProperty = IsChoice && !IsInlineChoice
            ? type.Properties.FirstOrDefault(p => p.Const != null)?.Name
                ?? type.Choices.Select(c => (c.Type.NamedType ?? c.Type.InlineType)?.Properties.FirstOrDefault(p => p.Const != null)?.Name)
                    .FirstOrDefault(p => p != null)
            : null;
        ChoiceVariants = type.Choices.Select(choice => new JsonStructureCSharpChoiceVariantTemplateModel(
            choice.Name,
            resolver.Resolve(choice.Type, context),
            GetDiscriminatorProperty(choice.Type),
            GetDiscriminatorValue(choice))).ToList();
        TupleAttribute = IsTuple
            ? settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
                ? "[global::System.Text.Json.Serialization.JsonConverter(typeof(TupleJsonConverter<" + Name + ">))]"
                : "[global::Newtonsoft.Json.JsonConverter(typeof(TupleJsonConverter<" + Name + ">))]"
            : null;
        IsAbstract = type.IsAbstract;
        BaseType = type.BaseType == null ? null : context.GetName(type.BaseType);
        Attributes = settings.GenerateDataAnnotations
            ? JsonStructureCSharpPropertyTemplateModel.BuildAttributes(type.Annotations, settings, type.Description)
            : [];
        var properties = type.TupleOrder.Count == 0 ? type.Properties :
            type.TupleOrder.Select(name => type.Properties.First(property => property.Name == name)).ToList();
        var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        Properties = properties.Select(property => new JsonStructureCSharpPropertyTemplateModel(
            property, context, resolver, settings, Name, usedPropertyNames, Selector)).ToList();
        WrapperType = type.BaseType == null ? "object" : context.GetName(type.BaseType);
        ChoiceConverterAttribute = IsInlineChoice && type.Choices.Any(choice =>
                (choice.Type.NamedType ?? choice.Type.InlineType)?.Properties.Count > 0)
            ? settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
                ? "[global::System.Text.Json.Serialization.JsonConverter(typeof(" + Name + "JsonConverter))]"
                : "[global::Newtonsoft.Json.JsonConverter(typeof(" + Name + "JsonConverter))]"
            : null;
        ChoiceConverterAttribute = IsChoice && !IsInlineChoice && settings.JsonLibrary == CSharpJsonLibrary.NewtonsoftJson
            ? "[global::Newtonsoft.Json.JsonConverter(typeof(" + Name + "JsonConverter))]"
            : ChoiceConverterAttribute;
    }

    public string Name { get; }
    public bool IsTuple { get; }
    public bool IsChoice { get; }
    public bool IsInlineChoice { get; }
    public string Selector { get; }
    public string ChoiceDiscriminatorProperty { get; }
    public IReadOnlyList<JsonStructureCSharpChoiceVariantTemplateModel> ChoiceVariants { get; }
    public string WrapperType { get; }
    public string ChoiceConverterAttribute { get; }
    public bool UseSystemTextJson { get; }
    public string TupleAttribute { get; }
    public bool IsAbstract { get; }
    public string BaseType { get; }
    public IReadOnlyList<string> Attributes { get; }
    public IReadOnlyList<JsonStructureCSharpPropertyTemplateModel> Properties { get; }

    private static string GetDiscriminatorProperty(JsonStructureCodeGenerationTypeReference type)
    {
        var named = type?.NamedType ?? type?.InlineType;
        return named?.Properties.FirstOrDefault(p => p.Const != null)?.Name;
    }

    private static string GetDiscriminatorValue(JsonStructureCodeGenerationChoiceVariant choice)
    {
        return choice.Discriminator?.Type == JTokenType.String
            ? choice.Discriminator.Value<string>()
            : choice.Name;
    }
}

internal sealed class JsonStructureCSharpChoiceVariantTemplateModel
{
    public JsonStructureCSharpChoiceVariantTemplateModel(string name, string type, string propertyName)
    {
        Name = name;
        Type = type;
        PropertyName = propertyName;
        DiscriminatorValue = null;
    }

    public JsonStructureCSharpChoiceVariantTemplateModel(string name, string type, string propertyName, string discriminatorValue)
    {
        Name = name;
        Type = type;
        PropertyName = propertyName;
        DiscriminatorValue = discriminatorValue;
    }

    public string Name { get; }
    public string Type { get; }
    public string PropertyName { get; }
    public string DiscriminatorValue { get; }
}

internal sealed class JsonStructureCSharpPropertyTemplateModel
{
    public JsonStructureCSharpPropertyTemplateModel(
        JsonStructureCodeGenerationProperty property,
        JsonStructureCodeGenerationContext context,
        JsonStructureCSharpTypeResolver resolver,
        CSharpGeneratorSettings settings,
        string typeName,
        ISet<string> usedNames,
        string selector)
    {
        Name = Sanitize(property.Name, typeName, usedNames);
        Type = resolver.Resolve(property.Type, context);
        if (!property.IsRequired && settings.GenerateNullableReferenceTypes && !Type.EndsWith('?'))
        {
            Type += "?";
        }

        IsRequired = property.IsRequired && settings.UseRequiredKeyword;
        JsonIgnoreSelector = property.Name == selector
            ? settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
                ? "[global::System.Text.Json.Serialization.JsonIgnore]"
                : "[global::Newtonsoft.Json.JsonIgnore]"
            : null;
        Attributes = settings.GenerateDataAnnotations ? BuildAttributes(property.Annotations, settings, null) : [];
        if (property.Const != null)
        {
            DefaultValue = GetCSharpLiteral(property.Const, Type);
        }
        else
        {
            DefaultValue = null;
        }
        if (property.Type?.Kind == JsonStructureTypeKind.Binary &&
            property.Annotations.TryGetValue("contentEncoding", out var encoding) &&
            encoding.Type == JTokenType.String &&
            !string.Equals(encoding.Value<string>(), "base64", StringComparison.OrdinalIgnoreCase))
        {
            var converter = encoding.Value<string>().Equals("base64url", StringComparison.OrdinalIgnoreCase)
                ? "BinaryBase64UrlConverter"
                : encoding.Value<string>().Equals("hex", StringComparison.OrdinalIgnoreCase)
                    ? "BinaryHexConverter" : null;
            if (converter != null)
            {
                Attributes = Attributes.Concat([settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
                    ? $"[global::System.Text.Json.Serialization.JsonConverter(typeof({converter}))]"
                    : $"[global::Newtonsoft.Json.JsonConverter(typeof({converter}))]"]).ToList();
            }
        }
        Documentation = settings.GenerateDataAnnotations ? BuildDocumentation(property.Annotations) : null;
        HasGetter = !settings.GenerateDataAnnotations || !IsTrue(property.Annotations, "writeOnly");
        HasSetter = !settings.GenerateDataAnnotations || !IsTrue(property.Annotations, "readOnly");
    }

    public string Name { get; }
    public string Type { get; }
    public bool IsRequired { get; }
    public string JsonIgnoreSelector { get; }
    public IReadOnlyList<string> Attributes { get; }
    public string DefaultValue { get; }
    public string Documentation { get; }
    public bool HasGetter { get; }
    public bool HasSetter { get; }

    internal static IReadOnlyList<string> BuildAttributes(IReadOnlyDictionary<string, JToken> annotations, CSharpGeneratorSettings settings, string description)
    {
        var result = new List<string>();
        if (IsTrue(annotations, "deprecated"))
        {
            result.Add("[global::System.Obsolete]");
        }

        if (annotations.TryGetValue("altnames", out var alternateNames))
        {
            var name = GetAlternateName(alternateNames, settings.JsonLibrary);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var escaped = Escape(name);
                result.Add(settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
                    ? $"[global::System.Text.Json.Serialization.JsonPropertyName(\"{escaped}\")]"
                    : $"[global::Newtonsoft.Json.JsonProperty(\"{escaped}\")]");
            }
        }

        if (annotations.TryGetValue("format", out var format) && string.Equals(format.Value<string>(), "email", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("[global::System.ComponentModel.DataAnnotations.EmailAddress]");
        }
        if (annotations.TryGetValue("pattern", out var pattern) && pattern.Type == JTokenType.String)
        {
            result.Add($"[global::System.ComponentModel.DataAnnotations.RegularExpression(\"{Escape(pattern.Value<string>())}\")]");
        }

        var minLength = GetInt(annotations, "minLength");
        var maxLength = GetInt(annotations, "maxLength");
        if (maxLength.HasValue)
        {
            result.Add(minLength.HasValue
                ? $"[global::System.ComponentModel.DataAnnotations.StringLength({maxLength.Value}, MinimumLength = {minLength.Value})]"
                : $"[global::System.ComponentModel.DataAnnotations.MaxLength({maxLength.Value})]");
        }
        else if (minLength.HasValue)
        {
            result.Add($"[global::System.ComponentModel.DataAnnotations.MinLength({minLength.Value})]");
        }

        var minimum = GetDecimal(annotations, "minimum");
        var maximum = GetDecimal(annotations, "maximum");
        if (minimum.HasValue || maximum.HasValue)
        {
            var min = minimum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "double.MinValue";
            var max = maximum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "double.MaxValue";
            result.Add($"[global::System.ComponentModel.DataAnnotations.Range({min}, {max})]");
        }

        return result;
    }

    private static string BuildDocumentation(IReadOnlyDictionary<string, JToken> annotations)
    {
        if (!annotations.TryGetValue("unit", out var unit) || unit.Type != JTokenType.String)
        {
            return null;
        }

        return "/// <remarks>Unit: " + SecurityElement.Escape(unit.Value<string>()) + "</remarks>";
    }

    private static bool IsTrue(IReadOnlyDictionary<string, JToken> annotations, string key) =>
        annotations.TryGetValue(key, out var token) && token.Type == JTokenType.Boolean && token.Value<bool>();

    private static int? GetInt(IReadOnlyDictionary<string, JToken> annotations, string key) =>
        annotations.TryGetValue(key, out var token) && token.Type == JTokenType.Integer ? token.Value<int>() : null;

    private static decimal? GetDecimal(IReadOnlyDictionary<string, JToken> annotations, string key) =>
        annotations.TryGetValue(key, out var token) && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            ? token.Value<decimal>() : null;

    private static string GetAlternateName(JToken token, CSharpJsonLibrary library)
    {
        if (token.Type == JTokenType.String)
        {
            return token.Value<string>();
        }

        if (token is JObject obj)
        {
            return obj.Value<string>(library == CSharpJsonLibrary.SystemTextJson ? "json" : "json")
                ?? obj.Value<string>("csharp")
                ?? obj.Properties().Select(p => p.Value.Value<string>()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        return null;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string GetCSharpLiteral(JToken token, string type)
    {
        return token.Type switch
        {
            JTokenType.String => "\"" + Escape(token.Value<string>()) + "\"",
            JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
            JTokenType.Integer => token.ToString(Newtonsoft.Json.Formatting.None),
            JTokenType.Float => token.ToString(Newtonsoft.Json.Formatting.None) + "m",
            JTokenType.Null => "null",
            _ => "default"
        };
    }

    private static string Sanitize(string name, string typeName, ISet<string> usedNames)
    {
        var result = string.IsNullOrWhiteSpace(name) ? "Value" :
            new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result = "_" + result;
        }
        if (CSharpKeywords.Contains(result))
        {
            result = "@" + result;
        }

        if (typeName == result || !usedNames.Add(result))
        {
            var baseName = result;
            var index = 2;
            do
            {
                result = baseName + "_" + index++;
            }
            while (!usedNames.Add(result));
        }

        return result;
    }

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "file",
        "required", "scoped"
    ];
}
