using System.Reflection;
using System.Text.Json;

namespace Honua.Console.Shell.Diagnostics;

/// <summary>
/// Validates a diagnostic bundle against the canonical, pinned JSON Schema
/// (<c>contracts/diagnostics/diagnostic-bundle.v1.json</c>, embedded into this assembly).
/// Console runs every emitted bundle through this before download/upload so the schema —
/// string-length, array-count, and body-size bounds and required/enum/type/const rules —
/// is the authoritative gate, not merely documented (honua-console#307).
///
/// This is a deliberately small validator covering exactly the JSON Schema subset the
/// canonical document uses (type, required, additionalProperties:false, properties, items,
/// enum, const, min/maxLength, min/maxItems, minimum/maximum, and internal <c>$ref</c> into
/// <c>$defs</c>). It is a byte-for-byte behavioural port of the support-owned validator so
/// the same language-neutral conformance corpus — including each case's expected error text —
/// passes identically on both sides, without taking a third-party dependency.
/// </summary>
public sealed class DiagnosticBundleSchema
{
    /// <summary>Manifest resource name of the embedded canonical schema.</summary>
    public const string ResourceName = "Honua.Console.Shell.Diagnostics.diagnostic-bundle.v1.json";

    private const int MaxErrors = 50;

    private readonly JsonDocument _schema;
    private readonly JsonElement _root;

    public DiagnosticBundleSchema()
        : this(LoadEmbeddedSchemaJson())
    {
    }

    public DiagnosticBundleSchema(string schemaJson)
    {
        _schema = JsonDocument.Parse(schemaJson);
        _root = _schema.RootElement;
    }

    /// <summary>The raw canonical schema JSON this validator enforces.</summary>
    public static string CanonicalSchemaJson => LoadEmbeddedSchemaJson();

    /// <summary>
    /// Validate an instance against the schema. Returns an empty list when valid, otherwise a
    /// bounded list of human-readable violation messages.
    /// </summary>
    public IReadOnlyList<string> Validate(JsonElement instance)
    {
        List<string> errors = [];
        ValidateNode(_root, instance, "$", errors);
        return errors;
    }

    private void ValidateNode(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        if (errors.Count >= MaxErrors)
            return;

        // Resolve internal $ref before anything else.
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out JsonElement refElement))
        {
            JsonElement resolved = ResolveRef(refElement.GetString());
            ValidateNode(resolved, instance, path, errors);
            return;
        }

        string? type = schema.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : null;

        if (type is not null && !MatchesType(type, instance))
        {
            errors.Add($"{path}: expected type '{type}' but found '{instance.ValueKind}'.");
            return;
        }

        // const / enum
        if (schema.TryGetProperty("const", out JsonElement constElement) && !JsonEquals(constElement, instance))
            errors.Add($"{path}: value must equal the constant '{constElement.GetRawText()}'.");

        if (schema.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Array)
        {
            bool matched = false;
            foreach (JsonElement allowed in enumElement.EnumerateArray())
            {
                if (JsonEquals(allowed, instance)) { matched = true; break; }
            }
            if (!matched)
                errors.Add($"{path}: value '{Preview(instance)}' is not one of the allowed values.");
        }

        switch (instance.ValueKind)
        {
            case JsonValueKind.String:
                ValidateString(schema, instance, path, errors);
                break;
            case JsonValueKind.Number:
                ValidateNumber(schema, instance, path, errors);
                break;
            case JsonValueKind.Array:
                ValidateArray(schema, instance, path, errors);
                break;
            case JsonValueKind.Object:
                ValidateObject(schema, instance, path, errors);
                break;
        }
    }

    private static void ValidateString(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        string value = instance.GetString() ?? string.Empty;
        if (schema.TryGetProperty("maxLength", out JsonElement maxLen) && value.Length > maxLen.GetInt32())
            errors.Add($"{path}: string length {value.Length} exceeds maxLength {maxLen.GetInt32()}.");
        if (schema.TryGetProperty("minLength", out JsonElement minLen) && value.Length < minLen.GetInt32())
            errors.Add($"{path}: string length {value.Length} is below minLength {minLen.GetInt32()}.");
    }

    private static void ValidateNumber(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        if (schema.TryGetProperty("type", out JsonElement t) && t.GetString() == "integer" && !IsIntegral(instance))
        {
            errors.Add($"{path}: expected an integer.");
            return;
        }

        double value = instance.GetDouble();
        if (schema.TryGetProperty("minimum", out JsonElement min) && value < min.GetDouble())
            errors.Add($"{path}: value {value} is below minimum {min.GetDouble()}.");
        if (schema.TryGetProperty("maximum", out JsonElement max) && value > max.GetDouble())
            errors.Add($"{path}: value {value} exceeds maximum {max.GetDouble()}.");
    }

    private void ValidateArray(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        int count = instance.GetArrayLength();
        if (schema.TryGetProperty("maxItems", out JsonElement maxItems) && count > maxItems.GetInt32())
            errors.Add($"{path}: array length {count} exceeds maxItems {maxItems.GetInt32()}.");
        if (schema.TryGetProperty("minItems", out JsonElement minItems) && count < minItems.GetInt32())
            errors.Add($"{path}: array length {count} is below minItems {minItems.GetInt32()}.");

        if (schema.TryGetProperty("items", out JsonElement itemsSchema))
        {
            int index = 0;
            foreach (JsonElement element in instance.EnumerateArray())
            {
                if (errors.Count >= MaxErrors) break;
                ValidateNode(itemsSchema, element, $"{path}[{index}]", errors);
                index++;
            }
        }
    }

    private void ValidateObject(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties);

        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement req in required.EnumerateArray())
            {
                string name = req.GetString() ?? string.Empty;
                if (!instance.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                    errors.Add($"{path}: missing required property '{name}'.");
            }
        }

        bool additionalAllowed =
            !schema.TryGetProperty("additionalProperties", out JsonElement additional)
            || additional.ValueKind != JsonValueKind.False;

        foreach (JsonProperty member in instance.EnumerateObject())
        {
            if (errors.Count >= MaxErrors) break;

            if (!hasProperties || !properties.TryGetProperty(member.Name, out JsonElement memberSchema))
            {
                if (!additionalAllowed)
                    errors.Add($"{path}: unexpected property '{member.Name}'.");
                continue;
            }

            ValidateNode(memberSchema, member.Value, $"{path}.{member.Name}", errors);
        }
    }

    // JSON Schema treats a number with a zero fractional part (e.g. 42.0) as a valid integer.
    private static bool IsIntegral(JsonElement instance)
    {
        if (instance.TryGetInt64(out _))
            return true;
        return instance.TryGetDouble(out double d) && !double.IsInfinity(d) && d == Math.Floor(d);
    }

    private JsonElement ResolveRef(string? pointer)
    {
        // Only internal pointers of the form "#/$defs/name" are used by the canonical schema.
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '#')
            throw new InvalidOperationException($"Unsupported schema $ref '{pointer}'.");

        JsonElement current = _root;
        foreach (string rawSegment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawSegment == "#")
                continue;
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next))
                throw new InvalidOperationException($"Unresolvable schema $ref '{pointer}'.");
            current = next;
        }
        return current;
    }

    private static bool MatchesType(string type, JsonElement instance) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => true
    };

    private static bool JsonEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            // Treat true/false kinds as booleans for comparison.
            bool bothBool = expected.ValueKind is JsonValueKind.True or JsonValueKind.False
                            && actual.ValueKind is JsonValueKind.True or JsonValueKind.False;
            if (!bothBool)
                return false;
        }

        return expected.ValueKind switch
        {
            JsonValueKind.String => expected.GetString() == actual.GetString(),
            JsonValueKind.Number => expected.GetRawText() == actual.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => expected.GetBoolean() == actual.GetBoolean(),
            JsonValueKind.Null => true,
            _ => expected.GetRawText() == actual.GetRawText()
        };
    }

    private static string Preview(JsonElement instance)
    {
        string raw = instance.ToString();
        return raw.Length > 60 ? raw[..60] + "…" : raw;
    }

    private static string LoadEmbeddedSchemaJson()
    {
        Assembly assembly = typeof(DiagnosticBundleSchema).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded schema resource '{ResourceName}' was not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
