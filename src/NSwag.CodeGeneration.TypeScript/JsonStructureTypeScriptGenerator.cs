using System.Text;
using NJsonSchema.CodeGeneration;
using NJsonSchema.CodeGeneration.TypeScript;
using NSwag.JsonStructure.CodeGeneration;
using NSwag.JsonStructure.Model;

namespace NSwag.CodeGeneration.TypeScript;

internal static class JsonStructureTypeScriptGenerator
{
    public static IEnumerable<CodeArtifact> Generate(JsonStructureCodeGenerationContext context, TypeScriptGeneratorSettings settings)
    {
        var resolver = new JsonStructureTypeScriptTypeResolver(settings);
        resolver.SetNames(CreateNames(context));
        return Generate(context, settings, resolver);
    }

    public static IEnumerable<CodeArtifact> Generate(
        JsonStructureCodeGenerationContext context,
        TypeScriptGeneratorSettings settings,
        JsonStructureTypeScriptTypeResolver resolver)
    {
        resolver.SetNames(CreateNames(context));
        var namespaceNames = CreateNamespaceNames(context);
        foreach (var entry in context.Models)
        {
            var code = new StringBuilder();
            var exportPrefix = settings.ExportTypes ? "export " : string.Empty;
            AppendNamespace(code, entry.Model.ScopeName, entry.Types, [entry.Model.ScopeName], namespaceNames,
                exportPrefix, resolver, context, settings);

            yield return new CodeArtifact(
                namespaceNames[entry.Model.ScopeName],
                CodeArtifactType.Class,
                CodeArtifactLanguage.TypeScript,
                CodeArtifactCategory.Contract,
                code.ToString());
        }
    }

    private static void AppendType(StringBuilder code, JsonStructureCodeGenerationNamedType type, string name,
        string exportPrefix, JsonStructureTypeScriptTypeResolver resolver,
        JsonStructureCodeGenerationContext context, TypeScriptGeneratorSettings settings)
    {
        var previousType = resolver.CurrentType;
        resolver.CurrentType = type;
        try
        {
            var isClass = settings.TypeStyle == TypeScriptTypeStyle.Class ||
                          settings.ClassTypes?.Contains(type.Name) == true;
            var rendered = type.Kind switch
            {
                JsonStructureTypeKind.Tuple => Tuple(type, name, exportPrefix, resolver, context),
                JsonStructureTypeKind.Choice => Choice(type, name, exportPrefix, resolver, context),
                _ => isClass
                    ? Class(type, name, exportPrefix, resolver, context, settings)
                    : Interface(type, name, exportPrefix, resolver, context, settings)
            };
            code.AppendLine(rendered);
        }
        finally
        {
            resolver.CurrentType = previousType;
        }
    }

    private static void AppendNamespace(StringBuilder code, string rawName,
        IReadOnlyList<JsonStructureCodeGenerationNamedType> types, string[] path,
        IReadOnlyDictionary<string, string> namespaceNames, string exportPrefix, JsonStructureTypeScriptTypeResolver resolver,
        JsonStructureCodeGenerationContext context, TypeScriptGeneratorSettings settings)
    {
        var name = namespaceNames[string.Join("\u001f", path)];
        var indent = new string(' ', path.Length * 4);
        code.Append(indent).Append(exportPrefix).Append("namespace ").Append(name).AppendLine(" {");
        foreach (var type in types.Where(type => context.GetScopePath(type).SequenceEqual(path)))
        {
            var rendered = new StringBuilder();
            AppendType(rendered, type, resolver.GetLocalName(type, context), exportPrefix, resolver, context, settings);
            foreach (var line in rendered.ToString().Split(Environment.NewLine))
            {
                if (line.Length > 0)
                {
                    code.Append(indent).Append("    ").AppendLine(line);
                }
            }
        }

        var children = types.Where(type => context.GetScopePath(type).Count > path.Length &&
                context.GetScopePath(type).Take(path.Length).SequenceEqual(path))
            .Select(type => context.GetScopePath(type)[path.Length])
            .Distinct(StringComparer.Ordinal);
        foreach (var child in children)
        {
            AppendNamespace(code, child, types, path.Append(child).ToArray(), namespaceNames,
                exportPrefix, resolver, context, settings);
        }
        code.Append(indent).AppendLine("}");
    }

    internal static Dictionary<JsonStructureCodeGenerationNamedType, string> CreateNames(
        JsonStructureCodeGenerationContext context)
    {
        var names = new Dictionary<JsonStructureCodeGenerationNamedType, string>();
        var namespaceNames = CreateNamespaceNames(context);
        var usedByNamespace = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var path in context.Models.SelectMany(entry =>
                     new[] { (IReadOnlyList<string>)new[] { entry.Model.ScopeName } }
                         .Concat(entry.Model.Namespaces.Select(namespaceModel =>
                             (IReadOnlyList<string>)new[] { entry.Model.ScopeName }.Concat(namespaceModel.Path).ToList())))
                     .Where(path => path.Count > 0))
        {
            var parent = string.Join("\u001f", path.Take(path.Count - 1));
            if (!usedByNamespace.TryGetValue(parent, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByNamespace[parent] = used;
            }
            used.Add(namespaceNames[string.Join("\u001f", path)]);
        }
        foreach (var type in context.Models.SelectMany(entry => entry.Types))
        {
            var scopePath = context.GetScopePath(type);
            var key = string.Join("\u001f", scopePath);
            if (!usedByNamespace.TryGetValue(key, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByNamespace[key] = used;
            }
            var local = UniqueIdentifier(context.GetLocalName(type), used);
            var prefix = scopePath.Select((_, index) =>
                namespaceNames[string.Join("\u001f", scopePath.Take(index + 1))]);
            names[type] = string.Join(".", prefix.Append(local));
        }
        return names;
    }

    private static Dictionary<string, string> CreateNamespaceNames(JsonStructureCodeGenerationContext context)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedByParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var rootUsed = new HashSet<string>(
            context.ReservedNames.Select(Sanitize),
            StringComparer.Ordinal);
        usedByParent[string.Empty] = rootUsed;
        foreach (var path in context.Models.SelectMany(entry =>
                     new[] { (IReadOnlyList<string>)new[] { entry.Model.ScopeName } }
                         .Concat(entry.Model.Namespaces.Select(namespaceModel =>
                             (IReadOnlyList<string>)new[] { entry.Model.ScopeName }.Concat(namespaceModel.Path).ToList())))
                     .Where(path => path.Count > 0))
        {
            var key = string.Join("\u001f", path);
            if (names.ContainsKey(key))
            {
                continue;
            }
            var parent = string.Join("\u001f", path.Take(path.Count - 1));
            if (!usedByParent.TryGetValue(parent, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByParent[parent] = used;
            }
            names[key] = UniqueIdentifier(path[path.Count - 1], used);
        }
        return names;
    }

    private static string UniqueIdentifier(string value, HashSet<string> used)
    {
        var baseName = Sanitize(value);
        var name = baseName;
        var index = 2;
        while (!used.Add(name))
        {
            name = baseName + "_" + index++;
        }
        return name;
    }

    private static string Interface(JsonStructureCodeGenerationNamedType type, string name, string exportPrefix,
        JsonStructureTypeScriptTypeResolver resolver, JsonStructureCodeGenerationContext context,
        TypeScriptGeneratorSettings settings)
    {
        var code = new StringBuilder().Append(exportPrefix).Append("interface ").Append(name);
        if (type.BaseType != null)
        {
            code.Append(" extends ").Append(resolver.GetName(type.BaseType, context));
        }

        code.AppendLine(" {");
        AppendProperties(code, type, resolver, context, settings, "    ");
        return code.AppendLine("}").ToString();
    }

    private static string Class(JsonStructureCodeGenerationNamedType type, string name, string exportPrefix,
        JsonStructureTypeScriptTypeResolver resolver, JsonStructureCodeGenerationContext context,
        TypeScriptGeneratorSettings settings)
    {
        var code = new StringBuilder();
        code.Append(exportPrefix).Append("class ").Append(name);
        if (type.IsAbstract)
        {
            code.Insert(exportPrefix.Length, "abstract ");
        }

        if (type.BaseType != null)
        {
            code.Append(" extends ").Append(resolver.GetName(type.BaseType, context));
        }
        code.Append(" implements I").Append(name).AppendLine(" {");
        foreach (var property in type.Properties)
        {
            code.Append("    ").Append(Sanitize(property.Name))
                .Append(property.IsRequired ? "!" : "?")
                .Append(": ").Append(resolver.Resolve(property.Type, context))
                .AppendLine(";");
        }

        code.AppendLine()
            .Append("    constructor(data?: I").Append(name).AppendLine(") {")
            .AppendLine(type.BaseType == null ? string.Empty : "        super(data);")
            .AppendLine("        if (data) {")
            .AppendLine("            for (var property in data) {")
            .AppendLine("                if (data.hasOwnProperty(property))")
            .AppendLine("                    (this as any)[property] = (data as any)[property];")
            .AppendLine("            }")
            .AppendLine("        }")
            .AppendLine("    }");
        AppendInit(code, type, context, resolver);
        if (!type.IsAbstract)
        {
            code.AppendLine()
                .Append("    static fromJS(data: any): ").Append(name).AppendLine(" {")
                .Append("        data = typeof data === 'object' ? data : {};").AppendLine()
                .Append("        let result = new ").Append(name).AppendLine("();")
                .AppendLine("        result.init(data);")
                .AppendLine("        return result;")
                .AppendLine("    }");
        }
        AppendToJson(code, type, context, resolver);
        code.AppendLine("}");

        code.AppendLine().Append(exportPrefix).Append("interface I").Append(name);
        if (type.BaseType != null)
        {
            code.Append(" extends ").Append(GetInterfaceName(resolver.GetName(type.BaseType, context)));
        }
        code.AppendLine(" {");
        AppendProperties(code, type, resolver, context, settings, "    ");
        code.AppendLine("}");
        return code.ToString();
    }

    private static void AppendProperties(StringBuilder code, JsonStructureCodeGenerationNamedType type,
        JsonStructureTypeScriptTypeResolver resolver, JsonStructureCodeGenerationContext context,
        TypeScriptGeneratorSettings settings, string indent)
    {
        foreach (var property in type.Properties)
        {
            code.Append(indent).Append(Sanitize(property.Name))
                .Append(property.IsRequired || !settings.MarkOptionalProperties ? string.Empty : "?")
                .Append(": ").Append(resolver.Resolve(property.Type, context)).AppendLine(";");
        }
    }
    private static void AppendInit(StringBuilder code, JsonStructureCodeGenerationNamedType type,
        JsonStructureCodeGenerationContext context, JsonStructureTypeScriptTypeResolver resolver)
    {
        code.AppendLine().AppendLine("    init(_data?: any) {").AppendLine("        if (_data) {");
        if (type.BaseType != null)
        {
            code.AppendLine("            super.init(_data);");
        }
        foreach (var property in type.Properties)
        {
            var field = Sanitize(property.Name);
            code.Append("            this.").Append(field).Append(" = ")
                .Append(Deserialize(property.Type, "_data[\"" + property.Name + "\"]", context, resolver)).AppendLine(";");
        }
        code.AppendLine("        }").AppendLine("    }");
    }

    private static void AppendToJson(StringBuilder code, JsonStructureCodeGenerationNamedType type,
        JsonStructureCodeGenerationContext context, JsonStructureTypeScriptTypeResolver resolver)
    {
        code.AppendLine().AppendLine("    toJSON(data?: any) {")
            .AppendLine("        data = typeof data === 'object' ? data : {};");
        if (type.BaseType != null)
        {
            code.AppendLine("        super.toJSON(data);");
        }
        foreach (var property in type.Properties)
        {
            code.Append("        data[\"").Append(property.Name).Append("\"] = ")
                .Append(Serialize(type.Properties.First(p => p.Name == property.Name).Type,
                    "this." + Sanitize(property.Name), context, resolver)).AppendLine(";");
        }
            code.AppendLine("        return data;").AppendLine("    }");
    }

        private static string Deserialize(JsonStructureCodeGenerationTypeReference type, string value,
            JsonStructureCodeGenerationContext context, JsonStructureTypeScriptTypeResolver resolver)
        {
            if (type == null) return value;
            if (type.Kind == JsonStructureTypeKind.Binary)
            {
                var encoding = type.Annotations.TryGetValue("contentEncoding", out var token) && token.Type == Newtonsoft.Json.Linq.JTokenType.String
                    ? token.ToString(Newtonsoft.Json.Formatting.None).Trim('"') : "base64";
                if (encoding.Equals("hex", StringComparison.OrdinalIgnoreCase))
                    return value + " ? Uint8Array.from((" + value + ").match(/../g) || [], (x: string) => parseInt(x, 16)) : " + value;
                var normalized = encoding.Equals("base64url", StringComparison.OrdinalIgnoreCase)
                    ? "(" + value + ").replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (" + value + ").length % 4) % 4)"
                    : value;
                return value + " ? Uint8Array.from(Array.prototype.map.call((globalThis as any).atob(" + normalized + "), (x: string) => x.charCodeAt(0)) as number[]) : " + value;
            }
            if (type.InlineType != null && type.InlineType.Kind == JsonStructureTypeKind.Set)
                return "new Set((" + value + " || []).map((x: any) => x))";
            if (type.InlineType != null && type.InlineType.Kind == JsonStructureTypeKind.Map)
                return "Object.keys(" + value + " || {}).reduce((r: any, k: string) => { r[k] = " + value + "[k]; return r; }, {})";
            if (type.NamedType != null)
            {
                var named = type.NamedType;
                if (named.Kind == JsonStructureTypeKind.Set)
                    return "new Set((" + value + " || []).map((x: any) => " + Deserialize(named.Items, "x", context, resolver) + "))";
                if (named.Kind == JsonStructureTypeKind.Map)
                    return "Object.keys(" + value + " || {}).reduce((r: any, k: string) => { r[k] = " +
                        Deserialize(named.Values, value + "[k]", context, resolver) + "; return r; }, {})";
                if (named.Kind == JsonStructureTypeKind.Tuple)
                    return "[" + string.Join(", ", named.TupleOrder.Select((n, i) =>
                        Deserialize(named.Properties.First(p => p.Name == n).Type, value + "[" + i + "]", context, resolver))) + "]";
                if (named.Kind == JsonStructureTypeKind.Choice && !string.IsNullOrEmpty(named.Selector))
                    return resolver.GetName(named, context) + ".fromJS(" + value + ")";
                if (named.Kind == JsonStructureTypeKind.Object)
                    return value + " ? " + resolver.GetName(named, context) + ".fromJS(" + value + ") : undefined";
            }
            if (type.Kind == JsonStructureTypeKind.Set && type.ElementType != null)
            {
                return "new Set((" + value + " || []).map((x: any) => " +
                       Deserialize(type.ElementType, "x", context, resolver) + "))";
            }
            if (type.Kind == JsonStructureTypeKind.Map && type.ValueType != null)
            {
                return "Object.keys(" + value + " || {}).reduce((r: any, k: string) => { r[k] = " +
                       Deserialize(type.ValueType, value + "[k]", context, resolver) + "; return r; }, {})";
            }
            if (type.TupleElements.Count > 0)
            {
                return "[" + string.Join(", ", type.TupleElements.Select((t, i) =>
                    Deserialize(t, value + "[" + i + "]", context, resolver))) + "]";
            }
            if (type.NamedType != null && type.NamedType.Kind == JsonStructureTypeKind.Object)
            {
                var name = resolver.GetName(type.NamedType, context);
                return value + " ? " + name + ".fromJS(" + value + ") : undefined";
            }
            return value;
        }

        private static string Serialize(JsonStructureCodeGenerationTypeReference type, string value,
            JsonStructureCodeGenerationContext context, JsonStructureTypeScriptTypeResolver resolver)
        {
            if (type == null) return value;
            if (type.Kind == JsonStructureTypeKind.Binary)
            {
                var encoding = type.Annotations.TryGetValue("contentEncoding", out var token) && token.Type == Newtonsoft.Json.Linq.JTokenType.String
                    ? token.ToString(Newtonsoft.Json.Formatting.None).Trim('"') : "base64";
                if (encoding.Equals("base64url", StringComparison.OrdinalIgnoreCase))
                    return value + " ? (globalThis as any).btoa(String.fromCharCode(...(Array.from(" + value + " as any) as unknown as number[]))).replace(/\\+/g, '-').replace(/\\//g, '_').replace(/=+$/, '') : " + value;
                if (encoding.Equals("hex", StringComparison.OrdinalIgnoreCase))
                    return value + " ? Array.from(" + value + ").map((x: number) => x.toString(16).padStart(2, '0')).join('') : " + value;
                return value + " ? (globalThis as any).btoa(String.fromCharCode(...(Array.from(" + value + " as any) as unknown as number[]))) : " + value;
            }
            if (type.InlineType != null && type.InlineType.Kind == JsonStructureTypeKind.Set)
                return "Array.from(" + value + " || []).map((x: any) => x)";
            if (type.InlineType != null && type.InlineType.Kind == JsonStructureTypeKind.Map)
                return "Object.keys(" + value + " || {}).reduce((r: any, k: string) => { r[k] = " + value + "[k]; return r; }, {})";
            if (type.NamedType != null)
            {
                var named = type.NamedType;
                if (named.Kind == JsonStructureTypeKind.Set)
                    return "Array.from(" + value + " || []).map((x: any) => " + Serialize(named.Items, "x", context, resolver) + ")";
                if (named.Kind == JsonStructureTypeKind.Map)
                    return "Object.keys(" + value + " || {}).reduce((r: any, k: string) => { r[k] = " +
                        Serialize(named.Values, value + "[k]", context, resolver) + "; return r; }, {})";
                if (named.Kind == JsonStructureTypeKind.Tuple)
                    return "(() => { return [" + string.Join(", ", named.TupleOrder.Select((n, i) =>
                        Serialize(named.Properties.First(p => p.Name == n).Type, value + "[" + i + "]", context, resolver))) + "]; })()";
                if (named.Kind == JsonStructureTypeKind.Choice && !string.IsNullOrEmpty(named.Selector))
                    return resolver.GetName(named, context) + ".toJSON(" + value + ")";
                if (named.Kind == JsonStructureTypeKind.Object)
                    return value + " ? " + value + ".toJSON() : undefined";
            }
            if (type.Kind == JsonStructureTypeKind.Set && type.ElementType != null)
            {
                return "Array.from(" + value + " || []).map((x: any) => " + Serialize(type.ElementType, "x", context, resolver) + ")";
            }
            if (type.Kind == JsonStructureTypeKind.Map && type.ValueType != null)
            {
                return "Object.keys(" + value + " || {}).reduce((r: any, k: string) => { r[k] = " +
                       Serialize(type.ValueType, value + "[k]", context, resolver) + "; return r; }, {})";
            }
            if (type.TupleElements.Count > 0)
            {
                return "(() => { return [" + string.Join(", ", type.TupleElements.Select((t, i) =>
                    Serialize(t, value + "[" + i + "]", context, resolver))) + "]; })()";
            }
            return value;
        }

    private static string Tuple(JsonStructureCodeGenerationNamedType type, string name, string exportPrefix,
        JsonStructureTypeScriptTypeResolver resolver, JsonStructureCodeGenerationContext context)
    {
        var values = type.TupleOrder.Select(n => resolver.Resolve(type.Properties.FirstOrDefault(p => p.Name == n)?.Type, context));
        return exportPrefix + "type " + name + " = [" + string.Join(", ", values) + "];" + Environment.NewLine;
    }

    private static string Choice(JsonStructureCodeGenerationNamedType type, string name, string exportPrefix,
        JsonStructureTypeScriptTypeResolver resolver, JsonStructureCodeGenerationContext context)
    {
        var code = new StringBuilder()
            .Append(exportPrefix).Append("type ").Append(name).Append(" = ")
            .Append(resolver.Resolve(type, context)).AppendLine(";");

        if (!string.IsNullOrEmpty(type.Selector))
        {
            code.AppendLine()
                .Append(exportPrefix).Append("namespace ").Append(name).AppendLine(" {")
                .Append("    export function fromJS(data: any): ").Append(name).AppendLine(" {")
                .AppendLine("        switch (data && data[\"" + type.Selector + "\"]) {");

            foreach (var choice in type.Choices)
            {
                code.Append("            case ")
                    .Append(choice.Discriminator?.ToString(Newtonsoft.Json.Formatting.None) ??
                        "\"" + choice.DiscriminatorName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
                    .AppendLine(":")
                    .Append("                return ")
                    .Append(ChoiceFromJs(choice.Type, "data", context, resolver))
                    .AppendLine(";");
            }

            code.AppendLine("            default:")
                .AppendLine("                return data;")
                .AppendLine("        }")
                .AppendLine("    }")
                .AppendLine()
                .Append("    export function toJSON(data: ").Append(name).AppendLine("): any {")
                .AppendLine("        return data && typeof (data as any).toJSON === \"function\"")
                .AppendLine("            ? (data as any).toJSON()")
                .AppendLine("            : data;")
                .AppendLine("    }")
                .AppendLine("}");
        }

        return code.ToString();
    }

    private static string ChoiceFromJs(JsonStructureCodeGenerationTypeReference type, string value,
        JsonStructureCodeGenerationContext context, JsonStructureTypeScriptTypeResolver resolver)
    {
        if (type?.NamedType?.Kind == JsonStructureTypeKind.Object)
        {
            return resolver.GetName(type.NamedType, context) + ".fromJS(" + value + ")";
        }

        return value;
    }

    private static string Sanitize(string name) => string.IsNullOrWhiteSpace(name) ? "value" :
        TypeScriptReservedNames.Contains(name) ? "_" + name :
        new(name.Select((c, i) => (char.IsLetterOrDigit(c) || c == '_') && (i > 0 || !char.IsDigit(c)) ? c : '_').ToArray());

    private static string GetInterfaceName(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0
            ? "I" + name
            : name[..(separator + 1)] + "I" + name[(separator + 1)..];
    }

    private static readonly HashSet<string> TypeScriptReservedNames =
    [
        "any", "boolean", "break", "case", "class", "const", "constructor", "continue",
        "debugger", "declare", "default", "delete", "do", "else", "enum", "export",
        "extends", "false", "finally", "for", "from", "function", "get", "if",
        "implements", "import", "in", "instanceof", "interface", "let", "module",
        "namespace", "never", "new", "null", "number", "object", "private",
        "protected", "public", "readonly", "require", "return", "set", "static",
        "string", "super", "switch", "symbol", "this", "throw", "true", "try",
        "type", "typeof", "undefined", "unique", "unknown", "var", "void", "while",
        "with", "yield"
    ];
}
