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
        var code = settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson
            ? GenerateSystemTextJson()
            : GenerateNewtonsoftJson();
        foreach (var type in types.Where(t => t.IsInlineChoice))
        {
            var name = context.GetLocalName(type);
            var wrapper = type.BaseType == null ? "object" : context.GetName(type.BaseType);
            var variants = type.Choices
                .Select(c => (Type: Resolve(c.Type, context), Property: (c.Type.NamedType ?? c.Type.InlineType) is { Properties.Count: > 0 } type ? type.Properties[0].Name : null))
                .Where(c => c.Type != "object" && c.Property != null)
                .ToList();
            if (settings.JsonLibrary == CSharpJsonLibrary.SystemTextJson)
            {
                code += GenerateSystemTextJsonUnion(name, wrapper, variants);
            }
            else
            {
                code += GenerateNewtonsoftJsonUnion(name, wrapper, variants);
            }
        }
        foreach (var type in types.Where(t => t.Kind == JsonStructureTypeKind.Choice && !t.IsInlineChoice))
        {
            var variants = type.Choices
                .Select(c => (Type: Resolve(c.Type, context), Property: GetDiscriminatorProperty(c.Type),
                    Value: c.Discriminator?.Type == Newtonsoft.Json.Linq.JTokenType.String
                        ? c.Discriminator.ToObject<string>() : c.Name))
                .Where(c => c.Type != "object" && c.Property != null)
                .ToList();
            if (settings.JsonLibrary == CSharpJsonLibrary.NewtonsoftJson && variants.Count > 0)
            {
                code += GenerateNewtonsoftJsonPolymorphic(context.GetLocalName(type), variants);
            }
        }
        var gaps = FindUnsupportedGaps(types, settings);
        if (gaps.Count > 0)
        {
            code += "\n" + string.Join("\n", gaps.Select(gap => "#warning " + gap));
        }
        return code;
    }

    private static string Resolve(JsonStructureCodeGenerationTypeReference type, JsonStructureCodeGenerationContext context)
    {
        if (type.NamedType != null) return context.GetName(type.NamedType);
        if (type.InlineType != null) return context.GetLocalName(type.InlineType);
        return "object";
    }

    private static string GenerateSystemTextJsonUnion(string name, string wrapper, IReadOnlyList<(string Type, string Property)> variants)
    {
        var code = new StringBuilder($$"""

public sealed class {{name}}JsonConverter : global::System.Text.Json.Serialization.JsonConverter<{{name}}>
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

    private static string GenerateNewtonsoftJsonUnion(string name, string wrapper, IReadOnlyList<(string Type, string Property)> variants)
    {
        var code = new StringBuilder($$"""

public sealed class {{name}}JsonConverter : global::Newtonsoft.Json.JsonConverter<{{name}}>
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
        List<(string Type, string Property, string Value)> variants)
    {
        var discriminator = variants[0].Property;
        var code = new StringBuilder($$"""

public sealed class {{name}}JsonConverter : global::Newtonsoft.Json.JsonConverter<{{name}}>
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
        return gaps;
    }

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

    private static string GenerateSystemTextJson()
    {
        var code = new StringBuilder("""
public static class JsonStructureConverters
{
    public static global::System.Text.Json.JsonSerializerOptions GetOptions()
    {
        var options = new global::System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new Int64StringConverter());
        options.Converters.Add(new NullableInt64StringConverter());
        options.Converters.Add(new UInt64StringConverter());
        options.Converters.Add(new NullableUInt64StringConverter());
        options.Converters.Add(new Int128StringConverter());
        options.Converters.Add(new NullableInt128StringConverter());
        options.Converters.Add(new UInt128StringConverter());
        options.Converters.Add(new NullableUInt128StringConverter());
        options.Converters.Add(new DecimalStringConverter());
        options.Converters.Add(new NullableDecimalStringConverter());
        options.Converters.Add(new TimeSpanIso8601Converter());
        options.Converters.Add(new NullableTimeSpanIso8601Converter());
        return options;
    }
}
""");
        AppendSystemTextJsonConverter(code, "Int64", "long", "GetInt64");
        AppendSystemTextJsonConverter(code, "UInt64", "ulong", "GetUInt64");
        AppendSystemTextJsonConverter(code, "Int128", "global::System.Int128", null);
        AppendSystemTextJsonConverter(code, "UInt128", "global::System.UInt128", null);
        AppendSystemTextJsonConverter(code, "Decimal", "decimal", "GetDecimal");
        code.AppendLine("""
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
        code.AppendLine("""
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
        code.AppendLine("""
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

    private static string GenerateNewtonsoftJson()
    {
        var code = new StringBuilder("""
public static class JsonStructureConverters
{
    public static global::Newtonsoft.Json.JsonSerializerSettings GetSettings()
    {
        var settings = new global::Newtonsoft.Json.JsonSerializerSettings();
        settings.Converters.Add(new Int64StringConverter());
        settings.Converters.Add(new NullableInt64StringConverter());
        settings.Converters.Add(new UInt64StringConverter());
        settings.Converters.Add(new NullableUInt64StringConverter());
        settings.Converters.Add(new Int128StringConverter());
        settings.Converters.Add(new NullableInt128StringConverter());
        settings.Converters.Add(new UInt128StringConverter());
        settings.Converters.Add(new NullableUInt128StringConverter());
        settings.Converters.Add(new DecimalStringConverter());
        settings.Converters.Add(new NullableDecimalStringConverter());
        settings.Converters.Add(new TimeSpanIso8601Converter());
        settings.Converters.Add(new NullableTimeSpanIso8601Converter());
        return settings;
    }
}
""");
        foreach (var item in new[] { ("Int64", "long"), ("UInt64", "ulong"), ("Int128", "global::System.Int128"), ("UInt128", "global::System.UInt128"), ("Decimal", "decimal") })
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

        code.AppendLine("""
public sealed class TimeSpanIso8601Converter : global::Newtonsoft.Json.JsonConverter<global::System.TimeSpan>
{
    public override global::System.TimeSpan ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, global::System.TimeSpan existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
        => global::System.Xml.XmlConvert.ToTimeSpan((string)reader.Value);
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, global::System.TimeSpan value, global::Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(global::System.Xml.XmlConvert.ToString(value));
}
""");
        code.AppendLine("""
public sealed class NullableTimeSpanIso8601Converter : global::Newtonsoft.Json.JsonConverter<global::System.TimeSpan?>
{
    public override global::System.TimeSpan? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, global::System.TimeSpan? existingValue, bool hasExistingValue, global::Newtonsoft.Json.JsonSerializer serializer)
        => reader.TokenType == global::Newtonsoft.Json.JsonToken.Null ? null : global::System.Xml.XmlConvert.ToTimeSpan((string)reader.Value);
    public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, global::System.TimeSpan? value, global::Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(value.HasValue ? global::System.Xml.XmlConvert.ToString(value.Value) : null);
}
""");
        code.AppendLine("""
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
        return code.ToString();
    }
}
