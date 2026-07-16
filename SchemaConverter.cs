using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EAJsonModelImporter;

internal sealed class SchemaConverter
{
    private readonly Dictionary<string, ImportClass> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImportEnum> _enums = new(StringComparer.OrdinalIgnoreCase);
    private ImportModel _model = null!;

    public ImportModel Convert(JsonNode root, string fallbackName)
    {
        var obj = root as JsonObject ?? throw new InvalidDataException("The root must be an object.");
        string name = TypeName(Text(obj, "title") ?? fallbackName);
        _model = new ImportModel
        {
            Name = name,
            Description = Text(obj, "description") ?? "",
            Version = Text(obj, "version") ?? Text(obj, "$version") ?? ""
        };

        if (LooksLikeSchema(obj)) ConvertSchema(obj, name);
        else InferObject(obj, name, "");
        _model.Classes.AddRange(_classes.Values.OrderBy(x => x.Name));
        _model.Enums.AddRange(_enums.Values.OrderBy(x => x.Name));
        return _model;
    }

    private void ConvertSchema(JsonObject root, string rootName)
    {
        foreach (var containerName in new[] { "$defs", "definitions" })
        {
            if (root[containerName] is not JsonObject definitions) continue;
            foreach (var (name, node) in definitions)
                if (node is JsonObject schema) ParseClass(TypeName(name), schema);
        }
        ParseClass(rootName, root);
    }

    private ImportClass ParseClass(string name, JsonObject schema)
    {
        name = TypeName(Text(schema, "title") ?? name);
        if (_classes.TryGetValue(name, out var existing)) return existing;
        var cls = new ImportClass { Name = name, Description = Text(schema, "description") ?? "" };
        _classes[name] = cls;

        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var part in allOf.OfType<JsonObject>())
            {
                if (Text(part, "$ref") is { } parent) cls.Parents.Add(ReferenceName(parent));
                else ParseProperties(cls, part);
            }
        }
        ParseProperties(cls, schema);
        return cls;
    }

    private void ParseProperties(ImportClass cls, JsonObject schema)
    {
        var required = schema["required"] is JsonArray array
            ? array.Select(x => x?.GetValue<string>() ?? "").ToHashSet(StringComparer.Ordinal)
            : [];
        if (schema["properties"] is not JsonObject properties) return;
        foreach (var (propertyName, node) in properties)
        {
            if (node is not JsonObject property) continue;
            cls.Properties.Add(ParseSchemaProperty(cls.Name, propertyName, property, required.Contains(propertyName)));
        }
    }

    private ImportProperty ParseSchemaProperty(string owner, string name, JsonObject schema, bool required)
    {
        string description = Text(schema, "description") ?? "";
        if (Text(schema, "$ref") is { } reference)
            return Property(name, ReferenceName(reference), description, required, false, true);

        if (schema["enum"] is JsonArray values)
        {
            string enumName = UniqueDefinitionName(name + " Enum", owner);
            if (!_enums.ContainsKey(enumName))
            {
                var item = new ImportEnum { Name = enumName, Description = description };
                item.Values.AddRange(values.Select(x => x?.ToString() ?? "null"));
                _enums[enumName] = item;
            }
            return Property(name, enumName, description, required, false, false);
        }

        string type = Text(schema, "type") ?? (schema["properties"] is not null ? "object" : "string");
        if (type == "array")
        {
            var items = schema["items"] as JsonObject ?? new JsonObject { ["type"] = "string" };
            var inner = ParseSchemaProperty(owner, name, items, required);
            return Property(name, inner.Type, description.Length > 0 ? description : inner.Description, required, true, inner.IsReference);
        }
        if (type == "object")
        {
            string nested = Text(schema, "title") is { } title
                ? TypeName(title)
                : UniqueDefinitionName(name, owner);
            ParseClass(nested, schema);
            return Property(name, nested, description, required, false, true);
        }
        if (schema["oneOf"] is JsonArray oneOf) return Choice(owner, name, oneOf, description, required);
        if (schema["anyOf"] is JsonArray anyOf) return Choice(owner, name, anyOf, description, required);
        return Property(name, Primitive(type, Text(schema, "format")), description, required, false, false);
    }

    private ImportProperty Choice(string owner, string name, JsonArray choices, string description, bool required)
    {
        var refs = choices.OfType<JsonObject>().Select(x => Text(x, "$ref")).Where(x => x is not null).Select(x => ReferenceName(x!)).ToList();
        if (refs.Count == 1) return Property(name, refs[0], description, required, false, true);
        string union = UniqueDefinitionName(name + " Choice", owner);
        var cls = GetClass(union);
        foreach (var choice in refs) if (!cls.Parents.Contains(choice)) cls.Parents.Add(choice);
        return Property(name, union, description, required, false, true);
    }

    private ImportClass InferObject(JsonObject obj, string name, string description)
    {
        var cls = GetClass(name);
        cls.Description = Text(obj, "description") ?? description;
        if (Text(obj, "is_a") is { } parent && !cls.Parents.Contains(TypeName(parent)))
            cls.Parents.Add(TypeName(parent));
        foreach (var (propertyName, value) in obj)
        {
            if (propertyName.Equals("attributes", StringComparison.OrdinalIgnoreCase) && value is JsonObject attributes)
            {
                ParseAttributeDefinitions(cls, attributes);
                continue;
            }
            if (IsDefinitionContainer(propertyName) && value is JsonObject definitions)
            {
                foreach (var (definitionName, definitionNode) in definitions)
                    if (definitionNode is JsonObject definition)
                        InferObject(definition, TypeName(definitionName), "Inferred from the '" + propertyName + "' definition container.");
                continue;
            }
            if (IsClassMetadata(propertyName)) continue;
            cls.Properties.Add(InferProperty(name, propertyName, value));
        }
        return cls;
    }

    private void ParseAttributeDefinitions(ImportClass cls, JsonObject attributes)
    {
        foreach (var (name, node) in attributes)
        {
            if (node is not JsonObject definition)
            {
                cls.Properties.Add(InferProperty(cls.Name, name, node));
                continue;
            }
            string range = Text(definition, "range") ?? Text(definition, "type") ?? "string";
            bool required = Boolean(definition, "required") || Integer(definition, "minimum_cardinality") > 0;
            bool many = Boolean(definition, "multivalued") || Integer(definition, "maximum_cardinality") > 1;
            string primitive = LinkMlPrimitive(range);
            bool reference = primitive.Length == 0;
            cls.Properties.Add(Property(name, reference ? TypeName(range) : primitive,
                Text(definition, "description") ?? "", required, many, reference));
        }
    }

    private ImportProperty InferProperty(string owner, string name, JsonNode? value)
    {
        if (value is JsonObject child)
        {
            string target = UniqueDefinitionName(name, owner);
            InferObject(child, target, "Inferred from JSON/YAML object.");
            return Property(name, target, "", true, false, true);
        }
        if (value is JsonArray array)
        {
            var sample = array.FirstOrDefault(x => x is not null);
            if (sample is JsonObject sampleObject)
            {
                string target = UniqueDefinitionName(Singular(name), owner);
                InferObject(sampleObject, target, "Inferred from array items.");
                return Property(name, target, "", true, true, true);
            }
            return Property(name, InferredPrimitive(sample), "", true, true, false);
        }
        return Property(name, InferredPrimitive(value), "", true, false, false);
    }

    private ImportClass GetClass(string name)
    {
        name = TypeName(name);
        if (!_classes.TryGetValue(name, out var cls)) _classes[name] = cls = new ImportClass { Name = name };
        return cls;
    }

    private string UniqueDefinitionName(string preferred, string owner)
    {
        string candidate = TypeName(preferred);
        if (!_classes.ContainsKey(candidate) && !_enums.ContainsKey(candidate)) return candidate;
        string qualified = IsStructuralOwner(owner)
            ? candidate + "Type"
            : TypeName(owner + " " + preferred);
        if (!_classes.ContainsKey(qualified) && !_enums.ContainsKey(qualified)) return qualified;
        int suffix = 2;
        while (_classes.ContainsKey(qualified + suffix) || _enums.ContainsKey(qualified + suffix)) suffix++;
        return qualified + suffix;
    }

    private static bool IsDefinitionContainer(string name) => name.Equals("classes", StringComparison.OrdinalIgnoreCase)
        || name.Equals("definitions", StringComparison.OrdinalIgnoreCase)
        || name.Equals("$defs", StringComparison.OrdinalIgnoreCase)
        || name.Equals("schemas", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuralOwner(string name) => IsDefinitionContainer(name)
        || name.Equals("properties", StringComparison.OrdinalIgnoreCase)
        || name.Equals("components", StringComparison.OrdinalIgnoreCase);

    private static bool IsClassMetadata(string name) => name.Equals("description", StringComparison.OrdinalIgnoreCase)
        || name.Equals("is_a", StringComparison.OrdinalIgnoreCase)
        || name.Equals("class_uri", StringComparison.OrdinalIgnoreCase)
        || name.Equals("abstract", StringComparison.OrdinalIgnoreCase)
        || name.Equals("mixins", StringComparison.OrdinalIgnoreCase)
        || name.Equals("slots", StringComparison.OrdinalIgnoreCase);

    private static bool Boolean(JsonObject obj, string name) => obj[name] is JsonValue value
        && value.TryGetValue<bool>(out var result) && result;
    private static int Integer(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return 0;
        if (value.TryGetValue<int>(out var integer)) return integer;
        return value.TryGetValue<long>(out var longer) && longer <= int.MaxValue ? (int)longer : 0;
    }
    private static string LinkMlPrimitive(string range) => range.ToLowerInvariant() switch
    {
        "string" or "str" or "uriorcurie" or "uri" or "ncname" => "String",
        "integer" or "int" or "long" => "Integer",
        "float" or "double" or "decimal" => "Real",
        "boolean" or "bool" => "Boolean",
        "date" => "Date",
        "datetime" or "date_time" => "DateTime",
        "time" => "Time",
        _ => ""
    };

    private static ImportProperty Property(string name, string type, string description, bool required, bool many, bool reference) =>
        new() { Name = name, Type = type, Description = description, Required = required, Many = many, IsReference = reference };
    private static bool LooksLikeSchema(JsonObject o) => o.ContainsKey("$schema") || o.ContainsKey("$defs") || o.ContainsKey("definitions") || o.ContainsKey("properties") || o.ContainsKey("allOf");
    private static string? Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static string ReferenceName(string value) => TypeName(Uri.UnescapeDataString(value.Split('/').Last()));
    private static string TypeName(string value) => string.Concat(Regex.Split(value, "[^A-Za-z0-9]+").Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    private static string Singular(string value) => value.EndsWith("ies") ? value[..^3] + "y" : value.EndsWith("s") && value.Length > 1 ? value[..^1] : value;
    private static string Primitive(string type, string? format) => format?.ToLowerInvariant() switch { "date" => "Date", "date-time" => "DateTime", "uri" or "uri-reference" => "String", _ => type.ToLowerInvariant() switch { "integer" => "Integer", "number" => "Real", "boolean" => "Boolean", "null" => "Object", _ => "String" } };
    private static string InferredPrimitive(JsonNode? node) => node is null ? "Object" : node.GetValueKind() switch { System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "Boolean", System.Text.Json.JsonValueKind.Number => node.ToString().Contains('.') ? "Real" : "Integer", _ => "String" };
}
