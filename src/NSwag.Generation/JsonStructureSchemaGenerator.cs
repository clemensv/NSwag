using System.Collections;
using System.Globalization;
using System.Reflection;
using Namotion.Reflection;
using Newtonsoft.Json.Linq;
using NSwag.JsonStructure;
using NSwag.JsonStructure.Model;

namespace NSwag.Generation
{
    /// <summary>Generates native JSON Structure schemas from CLR types.</summary>
    public sealed class JsonStructureSchemaGenerator
    {
        private readonly Dictionary<Type, JsonStructureNamedType> _types = new();
        private JsonStructureDocument _document;

        /// <summary>Initializes a generator using the OpenAPI generator settings.</summary>
        public JsonStructureSchemaGenerator(OpenApiDocumentGeneratorSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Gets the settings used by this generator.</summary>
        public OpenApiDocumentGeneratorSettings Settings { get; }

        /// <summary>Generates a JSON Structure document whose root is <paramref name="type"/>.</summary>
        public JsonStructureDocument Generate(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            _types.Clear();
            _document = new JsonStructureDocument
            {
                SchemaUri = GetDialectUri(Settings.JsonStructureDialect),
                Name = type.Name
            };
            if (TryGetPrimitive(type, out _) || TryGetCollection(type, out _, out _, out _))
            {
                _document.RootSchema = CreateSchema(type);
                _document.SourceJson = CreateSourceJson(type);
                return _document;
            }
            var root = AddNamedType(type);
            _document.RootPointer = root.Pointer;
            _document.SourceJson = CreateSourceJson(type);
            return _document;
        }

        private JObject CreateSourceJson(Type type)
        {
            var schema = CreateSourceSchema(type);
            schema["$schema"] = GetDialectUri(Settings.JsonStructureDialect);
            schema["name"] = type.Name;
            return schema;
        }

        private static JObject CreateSourceSchema(Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type) != null;
            type = Nullable.GetUnderlyingType(type) ?? type;
            var schema = new JObject();
            if (TryGetPrimitive(type, out var primitive))
            {
                schema["type"] = primitive.ToString().ToLowerInvariant();
            }
            else if (TryGetCollection(type, out var collection, out var element, out var value))
            {
                schema["type"] = collection == JsonStructureTypeKind.Map ? "map" : "array";
                schema[collection == JsonStructureTypeKind.Map ? "values" : "items"] =
                    CreateSourceSchema(collection == JsonStructureTypeKind.Map ? value : element);
            }
            else
            {
                schema["type"] = "object";
                var properties = new JObject();
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                             .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod != null))
                {
                    properties[property.Name] = CreateSourceSchema(property.PropertyType);
                }
                schema["properties"] = properties;
            }

            if (nullable)
            {
                schema["type"] = new JArray("null", schema["type"]);
            }

            return schema;
        }

        private static string GetDialectUri(JsonStructureDialect dialect)
        {
            return dialect switch
            {
                JsonStructureDialect.Core => JsonStructureDialects.CoreUri,
                JsonStructureDialect.Validation => JsonStructureDialects.ValidationUri,
                _ => JsonStructureDialects.ExtendedUri
            };
        }

        /// <summary>Generates a JSON Structure document from a contextual CLR type.</summary>
        public JsonStructureDocument Generate(ContextualType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return Generate(type.OriginalType);
        }

        private JsonStructureNamedType AddNamedType(Type type)
        {
            type = UnwrapNullable(type);
            if (_types.TryGetValue(type, out var existing)) return existing;
            var schema = new JsonStructureSchema
            {
                Kind = JsonStructureTypeKind.Object,
                Name = type.Name,
                IsAbstract = type.IsAbstract,
                Pointer = "#/definitions/" + (string.IsNullOrEmpty(PointerPath(type)) ? "" : PointerPath(type) + "/") + Escape(type.Name)
            };
            var ns = GetNamespace(type.Namespace);
            var named = new JsonStructureNamedType(schema.Name, schema, ns);
            ns.AddType(named);
            _types[type] = named;
            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object) && !baseType.IsValueType)
            {
                schema.Extends = AddNamedType(baseType).Pointer;
            }
            PopulateObject(schema, type);
            return named;
        }

        private void PopulateObject(JsonStructureSchema schema, Type type)
        {
            if (type.IsEnum)
            {
                schema.Kind = JsonStructureTypeKind.String;
                foreach (var value in Enum.GetNames(type)) schema.AddEnumerationValue(value);
                return;
            }
            if (IsTuple(type))
            {
                schema.Kind = JsonStructureTypeKind.Tuple;
                foreach (var argument in type.GetGenericArguments())
                {
                    var name = "Item" + (schema.TupleOrder.Count + 1);
                    schema.AddTupleElement(name);
                    schema.AddProperty(new JsonStructureProperty(name, CreateSchema(argument)) { IsRequired = true });
                }
                return;
            }
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod == null) continue;
                var nullable = IsNullable(property);
                schema.AddProperty(new JsonStructureProperty(property.Name, CreateSchema(property.PropertyType, nullable))
                {
                    IsRequired = !nullable
                });
            }
        }

        private JsonStructureSchema CreateSchema(Type type, bool nullable = false)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) { type = underlying; nullable = true; }
            JsonStructureSchema schema;
            if (IsTuple(type))
            {
                schema = new JsonStructureSchema { Kind = JsonStructureTypeKind.Tuple };
                foreach (var argument in type.GetGenericArguments())
                {
                    var name = "Item" + (schema.TupleOrder.Count + 1);
                    schema.AddTupleElement(name);
                    schema.AddProperty(new JsonStructureProperty(name, CreateSchema(argument)) { IsRequired = true });
                }
            }
            else if (TryGetPrimitive(type, out var kind))
            {
                schema = new JsonStructureSchema { Kind = kind };
            }
            else if (TryGetCollection(type, out var collectionKind, out var elementType, out var valueType))
            {
                schema = new JsonStructureSchema { Kind = collectionKind };
                if (collectionKind == JsonStructureTypeKind.Map) schema.Values = CreateSchema(valueType);
                else schema.Items = CreateSchema(elementType);
            }
            else if (type.IsEnum)
            {
                schema = new JsonStructureSchema { Kind = JsonStructureTypeKind.String };
                foreach (var value in Enum.GetNames(type)) schema.AddEnumerationValue(value);
            }
            else
            {
                var named = AddNamedType(type);
                schema = GetDiscriminator(type, out var discriminator)
                    ? CreateChoiceSchema(type, named, discriminator)
                    : new JsonStructureSchema { Reference = named.Pointer };
            }
            if (!nullable) return schema;
            var union = new JsonStructureSchema();
            union.AddUnionMember(new JsonStructureTypeReference(JsonStructureTypeKind.Null));
            union.AddUnionMember(schema.IsReference
                ? new JsonStructureTypeReference(schema.Reference)
                : new JsonStructureTypeReference(schema));
            return union;
        }

        private JsonStructureSchema CreateChoiceSchema(Type type, JsonStructureNamedType named, DiscriminatorInfo discriminator)
        {
            var schema = new JsonStructureSchema
            {
                Kind = JsonStructureTypeKind.Choice,
                Extends = named.Pointer,
                Selector = discriminator.Selector
            };

            foreach (var variant in discriminator.Variants)
            {
                var derived = AddNamedType(variant.Type);
                var reference = new JsonStructureSchema
                {
                    Reference = derived.Pointer,
                    Const = variant.Value
                };
                schema.AddChoice(new JsonStructureChoice(variant.Name, reference));
            }

            return schema;
        }

        private static bool GetDiscriminator(Type type, out DiscriminatorInfo discriminator)
        {
            discriminator = null;
            var selector = (string)null;
            var variants = new List<DiscriminatorVariant>();

            foreach (var attribute in type.GetCustomAttributesData())
            {
                var name = attribute.AttributeType.Name;
                if (name == "JsonPolymorphicAttribute")
                {
                    selector = GetNamedString(attribute, "TypeDiscriminatorPropertyName") ?? "$type";
                }
                else if (name == "JsonDerivedTypeAttribute" && attribute.ConstructorArguments.Count > 0 &&
                         attribute.ConstructorArguments[0].Value is Type jsonDerivedType)
                {
                    var value = attribute.ConstructorArguments.Count > 1
                        ? ToJsonValue(attribute.ConstructorArguments[1].Value)
                        : null;
                    variants.Add(new DiscriminatorVariant(jsonDerivedType, value, GetDiscriminatorName(jsonDerivedType, value)));
                }
                else if (name == "JsonInheritanceConverterAttribute")
                {
                    selector = attribute.ConstructorArguments.Count > 1 &&
                               attribute.ConstructorArguments[1].Value is string discriminatorName
                        ? discriminatorName
                        : "discriminator";
                }
                else if (name == "JsonInheritanceAttribute" && attribute.ConstructorArguments.Count > 1 &&
                         attribute.ConstructorArguments[0].Value is string key &&
                         attribute.ConstructorArguments[1].Value is Type inheritanceClrType)
                {
                    variants.Add(new DiscriminatorVariant(
                        inheritanceClrType,
                        JValue.CreateString(key),
                        key));
                }
            }

            if (variants.Count == 0)
            {
                return false;
            }

            selector ??= "$type";
            discriminator = new DiscriminatorInfo(selector, variants);
            return true;
        }

        private static string GetNamedString(CustomAttributeData attribute, string name)
        {
            return attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name)
                .TypedValue.Value as string;
        }

        private static JToken ToJsonValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is Type)
            {
                return null;
            }

            return value switch
            {
                string text => JValue.CreateString(text),
                char character => JValue.CreateString(character.ToString()),
                bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                    => JToken.FromObject(value),
                IFormattable formattable => JValue.CreateString(formattable.ToString(null, CultureInfo.InvariantCulture)),
                _ => JToken.FromObject(value)
            };
        }

        private static string GetDiscriminatorName(Type type, JToken value)
        {
            if (value is JValue scalar && scalar.Value != null)
            {
                return scalar.Value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : scalar.Value.ToString();
            }

            return type.Name;
        }

        private sealed class DiscriminatorInfo
        {
            public DiscriminatorInfo(string selector, IReadOnlyList<DiscriminatorVariant> variants)
            {
                Selector = selector;
                Variants = variants;
            }

            public string Selector { get; }
            public IReadOnlyList<DiscriminatorVariant> Variants { get; }
        }

        private sealed class DiscriminatorVariant
        {
            public DiscriminatorVariant(Type type, JToken value, string name)
            {
                Type = type;
                Value = value;
                Name = name;
            }

            public Type Type { get; }
            public JToken Value { get; }
            public string Name { get; }
        }

        private static bool TryGetPrimitive(Type type, out JsonStructureTypeKind kind)
        {
            kind = type == typeof(bool) ? JsonStructureTypeKind.Boolean :
                type == typeof(string) || type == typeof(char) ? JsonStructureTypeKind.String :
                type == typeof(sbyte) ? JsonStructureTypeKind.Int8 :
                type == typeof(byte) ? JsonStructureTypeKind.UInt8 :
                type == typeof(short) ? JsonStructureTypeKind.Int16 :
                type == typeof(ushort) ? JsonStructureTypeKind.UInt16 :
                type == typeof(int) ? JsonStructureTypeKind.Int32 :
                type == typeof(uint) ? JsonStructureTypeKind.UInt32 :
                type == typeof(long) ? JsonStructureTypeKind.Int64 :
                type == typeof(ulong) ? JsonStructureTypeKind.UInt64 :
                type == typeof(float) ? JsonStructureTypeKind.Float :
                type == typeof(double) ? JsonStructureTypeKind.Double :
                type == typeof(decimal) ? JsonStructureTypeKind.Decimal :
                type == typeof(Guid) ? JsonStructureTypeKind.Uuid :
                type == typeof(Uri) ? JsonStructureTypeKind.Uri :
                type == typeof(DateTime) || type == typeof(DateTimeOffset) ? JsonStructureTypeKind.DateTime :
                type == typeof(TimeSpan) ? JsonStructureTypeKind.Duration :
                type == typeof(byte[]) ? JsonStructureTypeKind.Binary :
                type.FullName == "System.DateOnly" ? JsonStructureTypeKind.Date :
                type.FullName == "System.TimeOnly" ? JsonStructureTypeKind.Time :
                type == typeof(object) ? JsonStructureTypeKind.Any : JsonStructureTypeKind.None;
            return kind != JsonStructureTypeKind.None;
        }

        private static bool TryGetCollection(Type type, out JsonStructureTypeKind kind, out Type element, out Type value)
        {
            kind = JsonStructureTypeKind.None; element = value = null;
            if (type.IsArray && type != typeof(byte[])) { kind = JsonStructureTypeKind.Array; element = type.GetElementType(); return true; }
            if (!type.IsGenericType) return false;
            var definition = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) ||
                type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
            { kind = JsonStructureTypeKind.Map; value = args[1]; return true; }
            if (definition == typeof(HashSet<>) || definition == typeof(ISet<>) ||
                type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>)))
            { kind = JsonStructureTypeKind.Set; element = args[0]; return true; }
            if (typeof(IEnumerable).IsAssignableFrom(type) && args.Length == 1)
            { kind = JsonStructureTypeKind.Array; element = args[0]; return true; }
            return false;
        }

        private JsonStructureNamespace GetNamespace(string name)
        {
            var current = _document.Definitions;
            foreach (var segment in (name ?? string.Empty).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!current.TryGetNamespace(segment, out var child))
                {
                    child = new JsonStructureNamespace(segment, current);
                    current.AddNamespace(child);
                }
                current = child;
            }
            return current;
        }

        private static bool IsTuple(Type type) => type.IsGenericType && type.GetGenericTypeDefinition().FullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal);
        private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;
        private static string PointerPath(Type type) => string.Join("/", (type.Namespace ?? string.Empty).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Select(Escape));
        private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");

        private static bool IsNullable(PropertyInfo property)
        {
            if (!property.PropertyType.IsValueType) return GetNullableFlag(property) == 2;
            return Nullable.GetUnderlyingType(property.PropertyType) != null;
        }

        private static byte GetNullableFlag(PropertyInfo member)
        {
            foreach (var attribute in member.GetCustomAttributesData())
            {
                if (attribute.AttributeType.Name != "NullableAttribute") continue;
                var value = attribute.ConstructorArguments[0].Value;
                if (value is byte flag) return flag;
                if (value is IList<CustomAttributeTypedArgument> values && values.Count > 0) return (byte)values[0].Value;
            }
            foreach (var attribute in member.DeclaringType?.GetCustomAttributesData() ?? Array.Empty<CustomAttributeData>())
            {
                if (attribute.AttributeType.Name == "NullableContextAttribute")
                    return (byte)attribute.ConstructorArguments[0].Value;
            }
            return 1;
        }
    }
}
