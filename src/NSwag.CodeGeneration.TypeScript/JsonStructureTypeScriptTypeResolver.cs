using NJsonSchema.CodeGeneration.TypeScript;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;

namespace NSwag.CodeGeneration.TypeScript;

internal sealed class JsonStructureTypeScriptTypeResolver
{
    private readonly TypeScriptGeneratorSettings _settings;
    private IReadOnlyDictionary<JsonStructureCodeGenerationNamedType, string> _names;

    public JsonStructureTypeScriptTypeResolver(TypeScriptGeneratorSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void SetNames(IReadOnlyDictionary<JsonStructureCodeGenerationNamedType, string> names)
    {
        _names = names;
    }

    public string GetName(JsonStructureCodeGenerationNamedType type, JsonStructureCodeGenerationContext context)
    {
        return _names != null && _names.TryGetValue(type, out var name) ? name : context.GetName(type);
    }

    public string GetLocalName(JsonStructureCodeGenerationNamedType type, JsonStructureCodeGenerationContext context)
    {
        var name = GetName(type, context);
        return name[(name.LastIndexOf('.') + 1)..];
    }

    public string Resolve(JsonStructureCodeGenerationTypeReference type, JsonStructureCodeGenerationContext context)
    {
        if (type == null) return "any";
        var result =         type.NamedType != null ? GetName(type.NamedType, context) :
            type.InlineType != null ? ResolveInline(type.InlineType, context) :
            type.Union.Count > 0 ? string.Join(" | ", type.Union.Select(t => Resolve(t, context))) :
            type.TupleElements.Count > 0 ? "[" + string.Join(", ", type.TupleElements.Select(t => Resolve(t, context))) + "]" :
            type.ElementType != null ? (type.Kind == JsonStructureTypeKind.Set ? "Set<" : "") + Resolve(type.ElementType, context) + (type.Kind == JsonStructureTypeKind.Set ? ">" : "[]") :
            type.ValueType != null ? "{ [key: string]: " + Resolve(type.ValueType, context) + " }" :
            Resolve(type.Kind);
        if (type.Enumeration.Count > 0)
        {
            result = string.Join(" | ", type.Enumeration.Select(ToLiteral));
        }
        return type.IsNullable && result != "null" ? result + " | null" : result;
    }

    private string ResolveInline(JsonStructureCodeGenerationNamedType type, JsonStructureCodeGenerationContext context)
    {
        return type.Kind switch
        {
            JsonStructureTypeKind.Array => Resolve(type.Items, context) + "[]",
            JsonStructureTypeKind.Set => "Set<" + Resolve(type.Items, context) + ">",
            JsonStructureTypeKind.Map => "{ [key: string]: " + Resolve(type.Values, context) + " }",
            JsonStructureTypeKind.Tuple => "[" + string.Join(", ", type.TupleOrder.Select(n =>
                Resolve(type.Properties.First(p => p.Name == n).Type, context))) + "]",
            JsonStructureTypeKind.Choice => ResolveChoice(type, context),
            JsonStructureTypeKind.Object => "{ " + string.Join(" ", type.Properties.Select(p =>
                Sanitize(p.Name) + ": " + Resolve(p.Type, context) + ";")) + " }",
            _ => Resolve(type.Kind)
        };
    }

    public string Resolve(JsonStructureCodeGenerationNamedType type, JsonStructureCodeGenerationContext context)
    {
        return type.Kind switch
        {
            JsonStructureTypeKind.Tuple => "[" + string.Join(", ", type.TupleOrder.Select(n => Resolve(type.Properties.First(p => p.Name == n).Type, context))) + "]",
            JsonStructureTypeKind.Choice => ResolveChoice(type, context),
            JsonStructureTypeKind.Set => "Set<" + Resolve(type.Items, context) + ">",
            JsonStructureTypeKind.Map => "{ [key: string]: " + Resolve(type.Values, context) + " }",
            JsonStructureTypeKind.Object => "{ " + string.Join(" ", type.Properties.Select(p => p.Name + (p.IsRequired || !_settings.MarkOptionalProperties ? "" : "?") + ": " + Resolve(p.Type, context) + ";")) + " }",
            _ => Resolve(type.Kind)
        };
    }

    private string Resolve(JsonStructureTypeKind kind) => kind switch
    {
        JsonStructureTypeKind.Null => "null",
        JsonStructureTypeKind.Boolean => "boolean",
        JsonStructureTypeKind.String or JsonStructureTypeKind.Uuid or JsonStructureTypeKind.Uri or JsonStructureTypeKind.JsonPointer => "string",
        JsonStructureTypeKind.Date or JsonStructureTypeKind.DateTime or JsonStructureTypeKind.Time => DateTimeType(),
        JsonStructureTypeKind.Duration or JsonStructureTypeKind.Decimal => "string",
        JsonStructureTypeKind.Int64 or JsonStructureTypeKind.UInt64 or JsonStructureTypeKind.Int128 or JsonStructureTypeKind.UInt128 =>
            _settings.TypeScriptVersion >= 4.3m ? "bigint" : "string",
        JsonStructureTypeKind.Number or JsonStructureTypeKind.Integer or JsonStructureTypeKind.Int8 or JsonStructureTypeKind.Int16 or JsonStructureTypeKind.Int32 or JsonStructureTypeKind.UInt8 or JsonStructureTypeKind.UInt16 or JsonStructureTypeKind.UInt32 or JsonStructureTypeKind.Float8 or JsonStructureTypeKind.Float or JsonStructureTypeKind.Double => "number",
        JsonStructureTypeKind.Binary => _settings.TypeScriptVersion >= 5.0m ? "Uint8Array" : "string",
        JsonStructureTypeKind.Array => "any[]",
        JsonStructureTypeKind.Set => "Set<any>",
        JsonStructureTypeKind.Map => "{ [key: string]: any }",
        _ => "any"
    };

    private string ResolveChoice(JsonStructureCodeGenerationNamedType type, JsonStructureCodeGenerationContext context)
    {
        if (type.Choices.Count == 0)
        {
            return "any";
        }

        return string.Join(" | ", type.Choices.Select(choice =>
        {
            var value = Resolve(choice.Type, context);
            if (string.IsNullOrEmpty(type.Selector))
            {
                return value;
            }

            return "(" + value + " & { " + Sanitize(type.Selector) + ": " +
                   (choice.Discriminator?.ToString(Newtonsoft.Json.Formatting.None) ??
                    "\"" + choice.DiscriminatorName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"") + " })";
        }));
    }

    private string DateTimeType() => _settings.DateTimeType switch
    {
        TypeScriptDateTimeType.Date => "Date",
        TypeScriptDateTimeType.MomentJS or TypeScriptDateTimeType.OffsetMomentJS => "moment.Moment",
        TypeScriptDateTimeType.Luxon => "DateTime",
        TypeScriptDateTimeType.DayJS => "dayjs.Dayjs",
        _ => "string"
    };

    private static string Sanitize(string name) => string.IsNullOrWhiteSpace(name) ? "value" :
        new(name.Select((c, i) => (char.IsLetterOrDigit(c) || c == '_') && (i > 0 || !char.IsDigit(c)) ? c : '_').ToArray());

    private static string ToLiteral(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
    };
}
