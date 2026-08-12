using NJsonSchema.CodeGeneration.CSharp;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;

namespace NSwag.CodeGeneration.CSharp
{
    /// <summary>Resolves JSON Structure types to C# type names.</summary>
    public sealed class JsonStructureCSharpTypeResolver
    {
        private readonly CSharpGeneratorSettings _settings;

        /// <summary>Initializes a new instance of the <see cref="JsonStructureCSharpTypeResolver"/> class.</summary>
        /// <param name="settings">The C# generator settings.</param>
        public JsonStructureCSharpTypeResolver(CSharpGeneratorSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Resolves a JSON Structure type reference.</summary>
        /// <param name="type">The type reference.</param>
        /// <param name="context">The code-generation context used for named types.</param>
        /// <returns>The C# type name.</returns>
        public string Resolve(JsonStructureCodeGenerationTypeReference type, JsonStructureCodeGenerationContext context)
        {
            if (type == null)
            {
                return "object";
            }

            var result = ResolveCore(type, context);
            if (type.IsNullable && CanBeNullable(result))
            {
                result += "?";
            }

            return result;
        }

        /// <summary>Resolves a JSON Structure primitive type.</summary>
        /// <param name="kind">The JSON Structure type kind.</param>
        /// <returns>The C# type name.</returns>
        public string Resolve(JsonStructureTypeKind kind)
        {
            return ResolveCore(kind);
        }

        private string ResolveCore(JsonStructureCodeGenerationTypeReference type, JsonStructureCodeGenerationContext context)
        {
            if (type.NamedType != null)
            {
                return context.GetName(type.NamedType);
            }

            if (type.InlineType != null && JsonStructureTypeKinds.IsNameable(type.InlineType.Kind))
            {
                return context.GetName(type.InlineType);
            }

            if (type.Union.Count > 1)
            {
                return "object";
            }

            if (type.ElementType != null)
            {
                var elementType = Resolve(type.ElementType, context);
                return type.Kind == JsonStructureTypeKind.Set
                    ? "System.Collections.Generic.HashSet<" + elementType + ">"
                    : _settings.ArrayType + "<" + elementType + ">";
            }

            if (type.ValueType != null)
            {
                return _settings.DictionaryType + "<string, " + Resolve(type.ValueType, context) + ">";
            }

            if (type.TupleElements.Count > 0)
            {
                return type.InlineType != null
                    ? context.GetName(type.InlineType)
                    : "System.ValueTuple<" + string.Join(", ", type.TupleElements.Select(t => Resolve(t, context))) + ">";
            }

            return ResolveCore(type.Kind);
        }

        private string ResolveCore(JsonStructureTypeKind kind)
        {
            return kind switch
            {
                JsonStructureTypeKind.Null => "void",
                JsonStructureTypeKind.Boolean => "bool",
                JsonStructureTypeKind.String => "string",
                JsonStructureTypeKind.Int8 => "sbyte",
                JsonStructureTypeKind.UInt8 => "byte",
                JsonStructureTypeKind.Int16 => "short",
                JsonStructureTypeKind.UInt16 => "ushort",
                JsonStructureTypeKind.Int32 or JsonStructureTypeKind.Integer => "int",
                JsonStructureTypeKind.UInt32 => "uint",
                JsonStructureTypeKind.Int64 => "long",
                JsonStructureTypeKind.UInt64 => "ulong",
                JsonStructureTypeKind.Int128 => "System.Int128",
                JsonStructureTypeKind.UInt128 => "System.UInt128",
                JsonStructureTypeKind.Float8 or JsonStructureTypeKind.Float => "float",
                JsonStructureTypeKind.Double or JsonStructureTypeKind.Number => "double",
                JsonStructureTypeKind.Decimal => "decimal",
                JsonStructureTypeKind.Binary => "byte[]",
                JsonStructureTypeKind.Date => _settings.DateType,
                JsonStructureTypeKind.DateTime => _settings.DateTimeType,
                JsonStructureTypeKind.Time => _settings.TimeType,
                JsonStructureTypeKind.Duration => _settings.TimeSpanType,
                JsonStructureTypeKind.Uuid => "System.Guid",
                JsonStructureTypeKind.Uri => "System.Uri",
                JsonStructureTypeKind.JsonPointer => "string",
                JsonStructureTypeKind.Any or JsonStructureTypeKind.None => "object",
                _ => "object"
            };
        }

        private bool CanBeNullable(string type)
        {
            if (type == "void")
            {
                return false;
            }

            return IsValueType(type) || _settings.GenerateNullableReferenceTypes;
        }

        private static bool IsValueType(string type)
        {
            return type is "bool" or "sbyte" or "byte" or "short" or "ushort" or "int" or "uint"
                or "long" or "ulong" or "float" or "double" or "decimal" or "System.Int128"
                or "System.UInt128" or "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
                or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan"
                || type.StartsWith("System.ValueTuple<", StringComparison.Ordinal);
        }
    }
}
