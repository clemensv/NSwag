using System.Text;
using NJsonSchema.CodeGeneration.CSharp;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;
#pragma warning disable CA1305
namespace NSwag.CodeGeneration.CSharp;

internal static class JsonStructureCSharpConverters
{
    public static string Generate(CSharpGeneratorSettings settings, IEnumerable<JsonStructureCodeGenerationNamedType> types, JsonStructureCodeGenerationContext context)
    {
        var typeList = types.ToList();
        var requirements = Analyze(typeList);
        var code = new StringBuilder();
        if (requirements.HasSerializerConverters)
        {
            code.Append(settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
                ? GenerateSystemTextJson(requirements)
                : GenerateNewtonsoftJson(requirements));
        }

        foreach (var type in typeList.Where(t => t.IsInlineChoice))
        {
            var name = context.GetName(type);
            var converterName = GetConverterName(type, context);
            var wrapper = type.BaseType == null ? "object" : new JsonStructureCSharpTypeResolver(settings).Resolve(type.BaseType, context);
            var variants = type.Choices
                .Select(c => (Type: Resolve(c.Type, context, settings), Property: (c.Type.NamedType ?? c.Type.InlineType) is { Properties.Count: > 0 } type ? type.Properties[0].Name : null))
                .Where(c => c.Type != "object" && c.Property != null)
                .ToList();
            if (settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson)
            {
                code.Append(GenerateSystemTextJsonUnion(name, converterName, wrapper, variants));
            }
            else
            {
                code.Append(GenerateNewtonsoftJsonUnion(name, converterName, wrapper, variants));
            }
        }
        foreach (var type in typeList.Where(t => t.Kind == JsonStructureTypeKind.Choice && !t.IsInlineChoice))
        {
            var variants = type.Choices
                .Select(c => (Type: Resolve(c.Type, context, settings), Property: GetDiscriminatorProperty(c.Type),
                    Value: c.Discriminator?.Type == Newtonsoft.Json.Linq.JTokenType.String
                        ? c.Discriminator.ToObject<string>() : c.Name))
                .Where(c => c.Type != "object" && c.Property != null)
                .ToList();
            if (settings.JsonLibrary == CSharpJsonLibrary.NewtonsoftJson && variants.Count > 0)
            {
                code.Append(GenerateNewtonsoftJsonPolymorphic(
                    context.GetName(type),
                    GetConverterName(type, context),
                    variants));
            }
        }
        var gaps = FindUnsupportedGaps(typeList, settings);
        if (gaps.Count > 0)
        {
            code.Append('\n').Append(string.Join("\n", gaps.Select(gap => "#warning " + gap)));
        }
        return code.ToString();
    }

    private sealed class ConverterRequirements
    {
        public bool Int64 { get; set; }
        public bool UInt64 { get; set; }
        public bool Int128 { get; set; }
        public bool UInt128 { get; set; }
        public bool Decimal { get; set; }
        public bool Duration { get; set; }
        public bool Tuple { get; set; }
        public bool BinaryBase64Url { get; set; }
        public bool BinaryHex { get; set; }

        public bool HasSerializerConverters =>
            Int64 || UInt64 || Int128 || UInt128 || Decimal || Duration || Tuple || BinaryBase64Url || BinaryHex;
    }

    private static ConverterRequirements Analyze(IEnumerable<JsonStructureCodeGenerationNamedType> types)
    {
        var requirements = new ConverterRequirements();
        var visited = new HashSet<JsonStructureCodeGenerationNamedType>();
        foreach (var type in types)
        {
            Analyze(type, requirements, visited);
        }

        return requirements;
    }

    private static void Analyze(
        JsonStructureCodeGenerationNamedType type,
        ConverterRequirements requirements,
        ISet<JsonStructureCodeGenerationNamedType> visited)
    {
        if (type == null || !visited.Add(type))
        {
            return;
        }

        requirements.Tuple |= type.Kind == JsonStructureTypeKind.Tuple;
        AnalyzeAnnotations(type.Annotations, requirements);
        foreach (var property in type.Properties)
        {
            Analyze(property.Type, requirements, visited);
            AnalyzeAnnotations(property.Annotations, requirements);
        }

        foreach (var choice in type.Choices)
        {
            Analyze(choice.Type, requirements, visited);
        }

        Analyze(type.Items, requirements, visited);
        Analyze(type.Values, requirements, visited);
    }

    private static void Analyze(
        JsonStructureCodeGenerationTypeReference reference,
        ConverterRequirements requirements,
        ISet<JsonStructureCodeGenerationNamedType> visited)
    {
        if (reference == null)
        {
            return;
        }

        switch (reference.Kind)
        {
            case JsonStructureTypeKind.Int64:
                requirements.Int64 = true;
                break;
            case JsonStructureTypeKind.UInt64:
                requirements.UInt64 = true;
                break;
            case JsonStructureTypeKind.Int128:
                requirements.Int128 = true;
                break;
            case JsonStructureTypeKind.UInt128:
                requirements.UInt128 = true;
                break;
            case JsonStructureTypeKind.Decimal:
                requirements.Decimal = true;
                break;
            case JsonStructureTypeKind.Duration:
                requirements.Duration = true;
                break;
        }

        AnalyzeAnnotations(reference.Annotations, requirements);
        Analyze(reference.NamedType, requirements, visited);
        Analyze(reference.InlineType, requirements, visited);
        Analyze(reference.ElementType, requirements, visited);
        Analyze(reference.ValueType, requirements, visited);
        foreach (var element in reference.TupleElements.Concat(reference.Union))
        {
            Analyze(element, requirements, visited);
        }
    }

    private static void AnalyzeAnnotations(
        IReadOnlyDictionary<string, Newtonsoft.Json.Linq.JToken> annotations,
        ConverterRequirements requirements)
    {
        if (annotations == null || !annotations.TryGetValue("contentEncoding", out var encoding) ||
            encoding?.Type != Newtonsoft.Json.Linq.JTokenType.String)
        {
            return;
        }

        if (string.Equals(encoding.ToObject<string>(), "base64url", StringComparison.OrdinalIgnoreCase))
        {
            requirements.BinaryBase64Url = true;
        }
        else if (string.Equals(encoding.ToObject<string>(), "hex", StringComparison.OrdinalIgnoreCase))
        {
            requirements.BinaryHex = true;
        }
    }

    private static string Resolve(
        JsonStructureCodeGenerationTypeReference type,
        JsonStructureCodeGenerationContext context,
        CSharpGeneratorSettings settings)
    {
        if (type.NamedType != null) return new JsonStructureCSharpTypeResolver(settings).Resolve(type.NamedType, context);
        if (type.InlineType != null) return context.GetLocalName(type.InlineType);
        return "object";
    }

    private static string GenerateSystemTextJsonUnion(
        string name,
        string converterName,
        string wrapper,
        IReadOnlyList<(string Type, string Property)> variants)
    {
        var code = new StringBuilder($$"""

public sealed class {{converterName}} : global::System.Text.Json.Serialization.JsonConverter<{{name}}>
{
    public override {{name}} Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
        using var document = global::System.Text.Json.JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != global::System.Text.Json.JsonValueKind.Object) throw new global::System.Text.Json.JsonException("Expected an object for {{name}}.");
""");
        foreach (var variant in variants)
        {
            code.AppendLine($"        if (element.TryGetProperty(\"{Escape(variant.Property)}\", out _)) return new {name} {{ Value = global::System.Text.Json.JsonSerializer.Deserialize<{variant.Type}>(element.GetRawText(), options)! }};");
        }
        code.AppendLine($"        throw new global::System.Text.Json.JsonException(\"No known variant property found for {name}.\");");
        code.AppendLine("    }");
        code.AppendLine($"    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, {name} value, global::System.Text.Json.JsonSerializerOptions options)");
        code.AppendLine($"        => global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value, value.Value?.GetType() ?? typeof({wrapper}), options);");
        code.AppendLine("}");
        return code.ToString();
    }

    private static string GenerateNewtonsoftJsonUnion(
        string name,
        string converterName,
        string wrapper,
        IReadOnlyList<(string Type, string Property)> variants)
    {
        var code = new StringBuilder($$"""

public sealed class {{converterName}} : global::Newtonsoft.Json.JsonConverter<{{name}}>
{
    public override {{name}} ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, {{name}} existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
    {
        var objectValue = global::Newtonsoft.Json.Linq.JObject.Load(reader);
""");
        foreach (var variant in variants)
        {
            code.AppendLine($"        if (objectValue.Property(\"{Escape(variant.Property)}\") != null) return new {name} {{ Value = objectValue.ToObject<{variant.Type}>(serializer)! }};");
        }
        code.AppendLine($"        throw new global::Newtonsoft.Json.JsonSerializationException(\"No known variant property found for {name}.\");");
        code.AppendLine("    }");
        code.AppendLine($"    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, {name} value, global::Newtonsoft.Json.JsonSerializer serializer)");
        code.AppendLine("        => serializer.Serialize(writer, value.Value);");
        code.AppendLine("}");
        return code.ToString();
    }

    private static string GenerateNewtonsoftJsonPolymorphic(
        string name,
        string converterName,
        List<(string Type, string Property, string Value)> variants)
    {
        var discriminator = variants[0].Property;
        var code = new StringBuilder($$"""

public sealed class {{converterName}} : global::Newtonsoft.Json.JsonConverter<{{name}}>
{
    public override {{name}} ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, {{name}} existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
    {
        var objectValue = global::Newtonsoft.Json.Linq.JObject.Load(reader);
        var discriminator = (string)objectValue.Property("{{Escape(discriminator)}}")?.Value;
        switch (discriminator)
        {
""");
        foreach (var variant in variants)
        {
            code.AppendLine($"            case \"{Escape(variant.Value)}\": return objectValue.ToObject<{variant.Type}>(serializer);");
        }
        code.AppendLine($$"""
            default: throw new global::Newtonsoft.Json.JsonSerializationException("Unknown discriminator value for {{name}}.");
        }
    }
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, {{name}} value, global::Newtonsoft.Json.JsonSerializer serializer)
        => serializer.Serialize(writer, value, value?.GetType() ?? typeof({{name}}));
}
""");
        return code.ToString();
    }

    internal static string GetConverterName(
        JsonStructureCodeGenerationNamedType type,
        JsonStructureCodeGenerationContext context)
    {
        return context.GetName(type)
            .Replace(".", "_", StringComparison.Ordinal)
            .Replace("@", string.Empty, StringComparison.Ordinal) + "JsonConverter";
    }

    private static string GetDiscriminatorProperty(JsonStructureCodeGenerationTypeReference type)
    {
        var named = type?.NamedType ?? type?.InlineType;
        return named?.Properties.FirstOrDefault(p => p.Const != null)?.Name;
    }

    private static List<string> FindUnsupportedGaps(
        IEnumerable<JsonStructureCodeGenerationNamedType> types,
        CSharpGeneratorSettings settings)
    {
        var gaps = new List<string>();
        foreach (var type in types.Where(t => t.Kind == JsonStructureTypeKind.Choice && !t.IsInlineChoice))
        {
            var variants = type.Choices
                .Select(choice => (choice.Type.NamedType ?? choice.Type.InlineType, GetDiscriminatorProperty(choice.Type)))
                .ToList();
            if (settings.JsonLibrary == CSharpJsonLibrary.NewtonsoftJson &&
                (variants.Count == 0 || variants.Any(v => v.Item1 == null || v.Item2 == null)))
            {
                gaps.Add($"JSON Structure tagged choice '{type.Name}' has variants without object discriminator properties; Newtonsoft.Json polymorphism was not generated. Use object variants with a const discriminator.");
            }
        }
        if (types.SelectMany(GetReferences).Any(reference => reference.Union.Count > 1))
        {
            gaps.Add("JSON Structure unions with more than one non-null member have no lossless C# JSON converter and are generated as object; inspect the schema before relying on serialization.");
        }
        foreach (var annotation in types.SelectMany(GetAnnotations))
        {
            if (annotation.TryGetValue("contentCompression", out _))
                gaps.Add("JSON Structure contentCompression is descriptive only; generated C# serializers do not transparently compress or decompress payloads.");
            if (annotation.TryGetValue("contentMediaType", out _))
                gaps.Add("JSON Structure contentMediaType is descriptive only; generated C# serializers preserve the value but do not transcode media types.");
            if (annotation.TryGetValue("contentEncoding", out var encoding) &&
                encoding.Type == Newtonsoft.Json.Linq.JTokenType.String &&
                !SupportedEncodings.Contains(encoding.ToString(Newtonsoft.Json.Formatting.None).Trim('"'), StringComparer.OrdinalIgnoreCase))
                gaps.Add("JSON Structure contentEncoding '" + encoding.ToString(Newtonsoft.Json.Formatting.None).Trim('"') + "' has no generated C# converter; the binary wire format is unsupported.");
        }
        return gaps;
    }

    private static IEnumerable<IReadOnlyDictionary<string, Newtonsoft.Json.Linq.JToken>> GetAnnotations(JsonStructureCodeGenerationNamedType type)
    {
        yield return type.Annotations;
        foreach (var property in type.Properties)
        {
            yield return property.Annotations;
            if (property.Type?.InlineType != null) foreach (var nested in GetAnnotations(property.Type.InlineType)) yield return nested;
        }

    }

    private static readonly string[] SupportedEncodings = ["base64", "base64url", "hex"];

    private static IEnumerable<JsonStructureCodeGenerationTypeReference> GetReferences(JsonStructureCodeGenerationNamedType type)
    {
        foreach (var property in type.Properties)
        {
            if (property.Type != null)
            {
                yield return property.Type;
            }
        }
        foreach (var choice in type.Choices)
        {
            yield return choice.Type;
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string GenerateSystemTextJson(ConverterRequirements requirements)
    {
        var code = new StringBuilder("""
public static class JsonStructureConverters
{
    public static global::System.Text.Json.JsonSerializerOptions GetOptions()
    {
        var options = new global::System.Text.Json.JsonSerializerOptions();
""");
        if (requirements.Int64) code.AppendLine("        options.Converters.Add(new Int64StringConverter());").AppendLine("        options.Converters.Add(new NullableInt64StringConverter());");
        if (requirements.UInt64) code.AppendLine("        options.Converters.Add(new UInt64StringConverter());").AppendLine("        options.Converters.Add(new NullableUInt64StringConverter());");
        if (requirements.Int128) code.AppendLine("        options.Converters.Add(new Int128StringConverter());").AppendLine("        options.Converters.Add(new NullableInt128StringConverter());");
        if (requirements.UInt128) code.AppendLine("        options.Converters.Add(new UInt128StringConverter());").AppendLine("        options.Converters.Add(new NullableUInt128StringConverter());");
        if (requirements.Decimal) code.AppendLine("        options.Converters.Add(new DecimalStringConverter());").AppendLine("        options.Converters.Add(new NullableDecimalStringConverter());");
        if (requirements.Duration) code.AppendLine("        options.Converters.Add(new TimeSpanIso8601Converter());").AppendLine("        options.Converters.Add(new NullableTimeSpanIso8601Converter());");
        if (requirements.BinaryBase64Url) code.AppendLine("        options.Converters.Add(new BinaryBase64UrlConverter());");
        if (requirements.BinaryHex) code.AppendLine("        options.Converters.Add(new BinaryHexConverter());");
        code.AppendLine("        return options;");
        code.AppendLine("    }");
        code.AppendLine("}");
        if (requirements.Int64) AppendSystemTextJsonConverter(code, "Int64", "long", "GetInt64");
        if (requirements.UInt64) AppendSystemTextJsonConverter(code, "UInt64", "ulong", "GetUInt64");
        if (requirements.Int128) AppendSystemTextJsonConverter(code, "Int128", "global::System.Int128", null);
        if (requirements.UInt128) AppendSystemTextJsonConverter(code, "UInt128", "global::System.UInt128", null);
        if (requirements.Decimal) AppendSystemTextJsonConverter(code, "Decimal", "decimal", "GetDecimal");
        if (requirements.BinaryBase64Url || requirements.BinaryHex) code.AppendLine(GenerateSystemTextBinaryConverters(requirements));
        if (requirements.Duration) code.AppendLine("""
public sealed class TimeSpanIso8601Converter : global::System.Text.Json.Serialization.JsonConverter<global::System.TimeSpan>
{
    public override global::System.TimeSpan Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value != null && value.StartsWith("P", global::System.StringComparison.OrdinalIgnoreCase)
            ? global::System.Xml.XmlConvert.ToTimeSpan(value)
            : global::System.TimeSpan.Parse(value ?? string.Empty, global::System.Globalization.CultureInfo.InvariantCulture);
    }
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, global::System.TimeSpan value, global::System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(global::System.Xml.XmlConvert.ToString(value));
}
""");
        if (requirements.Duration) code.AppendLine("""
public sealed class NullableTimeSpanIso8601Converter : global::System.Text.Json.Serialization.JsonConverter<global::System.TimeSpan?>
{
    public override global::System.TimeSpan? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;
        var value = reader.GetString();
        return value != null && value.StartsWith("P", global::System.StringComparison.OrdinalIgnoreCase)
            ? global::System.Xml.XmlConvert.ToTimeSpan(value)
            : global::System.TimeSpan.Parse(value ?? string.Empty, global::System.Globalization.CultureInfo.InvariantCulture);
    }
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, global::System.TimeSpan? value, global::System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.HasValue ? global::System.Xml.XmlConvert.ToString(value.Value) : null);
}
""");
        if (requirements.Tuple) code.AppendLine("""
public class TupleJsonConverter<T> : global::System.Text.Json.Serialization.JsonConverter<T> where T : struct
{
    private readonly global::System.Reflection.PropertyInfo[] _properties;
    private readonly global::System.Reflection.ConstructorInfo _constructor;
    public TupleJsonConverter()
    {
        _constructor = global::System.Linq.Enumerable.FirstOrDefault(typeof(T).GetConstructors())
            ?? throw new global::System.InvalidOperationException("No constructor found for tuple type " + typeof(T).Name);
        _properties = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(_constructor.GetParameters(), parameter => typeof(T).GetProperty(
            parameter.Name!, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance |
            global::System.Reflection.BindingFlags.IgnoreCase) ?? throw new global::System.InvalidOperationException(
            "Property not found for constructor parameter '" + parameter.Name + "' in tuple type " + typeof(T).Name)));
    }
    public override T Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException("Expected array for tuple type " + typeToConvert.Name);
        var values = new object[_properties.Length];
        var index = 0;
        while (reader.Read() && reader.TokenType != global::System.Text.Json.JsonTokenType.EndArray)
        {
            if (index >= values.Length) throw new global::System.Text.Json.JsonException("Too many tuple elements.");
            values[index] = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, _properties[index].PropertyType, options);
            index++;
        }
        if (index != values.Length) throw new global::System.Text.Json.JsonException("Wrong number of tuple elements.");
        return (T)_constructor.Invoke(values);
    }
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, T value, global::System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var property in _properties) global::System.Text.Json.JsonSerializer.Serialize(writer, property.GetValue(value), property.PropertyType, options);
        writer.WriteEndArray();
    }
}
""");
        return code.ToString();
    }

    private static void AppendSystemTextJsonConverter(StringBuilder code, string name, string type, string numericReader)
    {
        var read = numericReader == null
            ? $"{type}.Parse(reader.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture)"
            : $"reader.TokenType == global::System.Text.Json.JsonTokenType.String ? {type}.Parse(reader.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture) : reader.{numericReader}()";
        code.AppendLine(string.Concat("public sealed class ", name, "StringConverter : global::System.Text.Json.Serialization.JsonConverter<", type, ">"))
            .AppendLine("{")
            .AppendLine(string.Concat("    public override ", type, " Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options) => ", read, ";"))
            .AppendLine(string.Concat("    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, ", type, " value, global::System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));"))
            .AppendLine("}");
        var nullableRead = numericReader == null
            ? $"reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : {type}.Parse(reader.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture)"
            : $"reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : ({type}?)(reader.TokenType == global::System.Text.Json.JsonTokenType.String ? {type}.Parse(reader.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture) : reader.{numericReader}())";
        code.AppendLine(string.Concat("public sealed class Nullable", name, "StringConverter : global::System.Text.Json.Serialization.JsonConverter<", type, "?>"))
            .AppendLine("{")
            .AppendLine(string.Concat("    public override ", type, "? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options) => ", nullableRead, ";"))
            .AppendLine(string.Concat("    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, ", type, "? value, global::System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.HasValue ? value.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : null);"))
            .AppendLine("}");
    }

    private static string GenerateNewtonsoftJson(ConverterRequirements requirements)
    {
        var code = new StringBuilder("""
public static class JsonStructureConverters
{
    public static global::Newtonsoft.Json.JsonSerializerSettings GetSettings()
    {
        var settings = new global::Newtonsoft.Json.JsonSerializerSettings();
""");
        if (requirements.Int64) code.AppendLine("        settings.Converters.Add(new Int64StringConverter());").AppendLine("        settings.Converters.Add(new NullableInt64StringConverter());");
        if (requirements.UInt64) code.AppendLine("        settings.Converters.Add(new UInt64StringConverter());").AppendLine("        settings.Converters.Add(new NullableUInt64StringConverter());");
        if (requirements.Int128) code.AppendLine("        settings.Converters.Add(new Int128StringConverter());").AppendLine("        settings.Converters.Add(new NullableInt128StringConverter());");
        if (requirements.UInt128) code.AppendLine("        settings.Converters.Add(new UInt128StringConverter());").AppendLine("        settings.Converters.Add(new NullableUInt128StringConverter());");
        if (requirements.Decimal) code.AppendLine("        settings.Converters.Add(new DecimalStringConverter());").AppendLine("        settings.Converters.Add(new NullableDecimalStringConverter());");
        if (requirements.Duration) code.AppendLine("        settings.Converters.Add(new TimeSpanIso8601Converter());").AppendLine("        settings.Converters.Add(new NullableTimeSpanIso8601Converter());");
        if (requirements.BinaryBase64Url) code.AppendLine("        settings.Converters.Add(new BinaryBase64UrlConverter());");
        if (requirements.BinaryHex) code.AppendLine("        settings.Converters.Add(new BinaryHexConverter());");
        code.AppendLine("        return settings;");
        code.AppendLine("    }");
        code.AppendLine("}");
        foreach (var item in new[] { ("Int64", "long", requirements.Int64), ("UInt64", "ulong", requirements.UInt64), ("Int128", "global::System.Int128", requirements.Int128), ("UInt128", "global::System.UInt128", requirements.UInt128), ("Decimal", "decimal", requirements.Decimal) }.Where(item => item.Item3))
        {
            code.AppendLine(string.Concat("public sealed class ", item.Item1, "StringConverter : global::Newtonsoft.Json.JsonConverter<", item.Item2, ">"))
                .AppendLine("{")
                .AppendLine(string.Concat("    public override ", item.Item2, " ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, ", item.Item2, " existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer) => ", item.Item2, ".Parse(global::System.Convert.ToString(reader.Value, global::System.Globalization.CultureInfo.InvariantCulture)!, global::System.Globalization.CultureInfo.InvariantCulture);"))
                .AppendLine(string.Concat("    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, ", item.Item2, " value, global::Newtonsoft.Json.JsonSerializer serializer) => writer.WriteValue(value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));"))
                .AppendLine("}");
            code.AppendLine(string.Concat("public sealed class Nullable", item.Item1, "StringConverter : global::Newtonsoft.Json.JsonConverter<", item.Item2, "?>"))
                .AppendLine("{")
                .AppendLine(string.Concat("    public override ", item.Item2, "? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, ", item.Item2, "? existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer) => reader.TokenType == global::Newtonsoft.Json.JsonToken.Null ? null : ", item.Item2, ".Parse(global::System.Convert.ToString(reader.Value, global::System.Globalization.CultureInfo.InvariantCulture)!, global::System.Globalization.CultureInfo.InvariantCulture);"))
                .AppendLine(string.Concat("    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, ", item.Item2, "? value, global::Newtonsoft.Json.JsonSerializer serializer) => writer.WriteValue(value.HasValue ? value.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : null);"))
                .AppendLine("}");
        }

        if (requirements.Duration) code.AppendLine("""
public sealed class TimeSpanIso8601Converter : global::Newtonsoft.Json.JsonConverter<global::System.TimeSpan>
{
    public override global::System.TimeSpan ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, global::System.TimeSpan existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
        => global::System.Xml.XmlConvert.ToTimeSpan((string)reader.Value);
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, global::System.TimeSpan value, global::Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(global::System.Xml.XmlConvert.ToString(value));
}
""");
        if (requirements.Duration) code.AppendLine("""
public sealed class NullableTimeSpanIso8601Converter : global::Newtonsoft.Json.JsonConverter<global::System.TimeSpan?>
{
    public override global::System.TimeSpan? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, global::System.TimeSpan? existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
        => reader.TokenType == global::Newtonsoft.Json.JsonToken.Null ? null : global::System.Xml.XmlConvert.ToTimeSpan((string)reader.Value);
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, global::System.TimeSpan? value, global::Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(value.HasValue ? global::System.Xml.XmlConvert.ToString(value.Value) : null);
}
""");
        if (requirements.Tuple) code.AppendLine("""
public class TupleJsonConverter<T> : global::Newtonsoft.Json.JsonConverter<T> where T : struct
{
    private readonly global::System.Reflection.ConstructorInfo _constructor;
    private readonly global::System.Reflection.PropertyInfo[] _properties;
    public TupleJsonConverter()
    {
        _constructor = global::System.Linq.Enumerable.First(typeof(T).GetConstructors());
        _properties = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(
            _constructor.GetParameters(), parameter => typeof(T).GetProperty(parameter.Name!,
                global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance |
                global::System.Reflection.BindingFlags.IgnoreCase)
            ?? throw new global::System.InvalidOperationException("Property not found for tuple element '" + parameter.Name + "'.")));
    }
    public override T ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, T existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
    {
        var values = serializer.Deserialize<global::Newtonsoft.Json.Linq.JArray>(reader);
        if (values == null || values.Count != _properties.Length) throw new global::Newtonsoft.Json.JsonSerializationException("Wrong number of tuple elements.");
        var arguments = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(_properties,
            (property, index) => values[index].ToObject(property.PropertyType, serializer)));
        return (T)_constructor.Invoke(arguments);
    }
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, T value, global::Newtonsoft.Json.JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var property in _properties) serializer.Serialize(writer, property.GetValue(value));
        writer.WriteEndArray();
    }
}
""");
        if (requirements.BinaryBase64Url || requirements.BinaryHex)
        {
            code.AppendLine(GenerateNewtonsoftBinaryConverters(requirements));
        }
        return code.ToString();
    }

    private static string GenerateSystemTextBinaryConverters(ConverterRequirements requirements)
    {
        var code = new StringBuilder();
        if (requirements.BinaryBase64Url) code.AppendLine("""

public sealed class BinaryBase64UrlConverter : global::System.Text.Json.Serialization.JsonConverter<byte[]>
{
    public override byte[] Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? string.Empty;
        value = value.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
        return global::System.Convert.FromBase64String(value);
    }
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, byte[] value, global::System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(global::System.Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
}
""");
        if (requirements.BinaryHex) code.AppendLine("""
public sealed class BinaryHexConverter : global::System.Text.Json.Serialization.JsonConverter<byte[]>
{
    public override byte[] Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? string.Empty;
        var bytes = new byte[text.Length / 2];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = global::System.Convert.ToByte(text.Substring(i * 2, 2), 16);
        return bytes;
    }
    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, byte[] value, global::System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(global::System.BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant());
}
""");
        return code.ToString();
    }

    private static string GenerateNewtonsoftBinaryConverters(ConverterRequirements requirements)
    {
        var code = new StringBuilder();
        if (requirements.BinaryBase64Url) code.AppendLine("""

public sealed class BinaryBase64UrlConverter : global::Newtonsoft.Json.JsonConverter<byte[]>
{
    public override byte[] ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, byte[] existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
    {
        var value = (string)reader.Value ?? string.Empty;
        value = value.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
        return global::System.Convert.FromBase64String(value);
    }
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, byte[] value, global::Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(global::System.Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
}
""");
        if (requirements.BinaryHex) code.AppendLine("""
public sealed class BinaryHexConverter : global::Newtonsoft.Json.JsonConverter<byte[]>
{
    public override byte[] ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, byte[] existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
    {
        var text = (string)reader.Value ?? string.Empty;
        var bytes = new byte[text.Length / 2];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = global::System.Convert.ToByte(text.Substring(i * 2, 2), 16);
        return bytes;
    }
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, byte[] value, global::Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(global::System.BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant());
}
""");
        return code.ToString();
    }
}
