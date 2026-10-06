using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer field-configuration operation. Reads and writes a layer's field
/// configuration (coded-value domains) on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps
/// the result (or rejection). It never fabricates success.
/// </summary>
public sealed class HonuaServerConsoleLayerFieldsOperation : IConsoleLayerFieldsOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerFieldsOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerFieldsAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerFields
            {
                Bound = true,
                LayerId = data.LayerId,
                Fields = (data.Fields ?? [])
                    .Where(field => !string.IsNullOrWhiteSpace(field.Name))
                    .Select(field => new ConsoleLayerField
                    {
                        Name = field.Name!,
                        Type = field.Type,
                        Alias = field.Alias,
                        DomainName = field.Domain?.Name,
                        DomainKind = MapDomainKind(field.Domain),
                        CodedValues = (field.Domain?.CodedValues ?? [])
                            .Select(cv => new ConsoleCodedValue(FormatCode(cv.Code), cv.Name ?? string.Empty))
                            .ToArray(),
                        RangeMin = field.Domain?.Range is { Count: >= 1 } range ? range[0] : null,
                        RangeMax = field.Domain?.Range is { Count: >= 2 } range2 ? range2[1] : null,
                        MergePolicy = field.Domain?.MergePolicy,
                        SplitPolicy = field.Domain?.SplitPolicy,
                        DefaultValueText = FormatDefaultValue(field.DefaultValue),
                        Hidden = field.Hidden,
                    })
                    .ToArray(),
            };
        }

        return ConsoleLayerFields.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return field configuration for this layer.");
    }

    public async Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
        int layerId,
        string fieldName,
        string domainName,
        IReadOnlyList<ConsoleCodedValue> codedValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var currentResult = await GetCurrentFieldAsync(layerId, fieldName, cancellationToken).ConfigureAwait(false);
        if (currentResult.Field is not { } current)
        {
            return currentResult.Failure!;
        }

        if (codedValues.Any(cv => !CanParseCode(cv.Code, current.Type)))
        {
            return Failure("Invalid", $"Every code for numeric field '{fieldName}' must be a JSON number.");
        }

        // An empty coded-value list clears the domain (Domain = null); otherwise set a codedValue domain.
        HonuaAdminFieldDomain? domain = codedValues.Count == 0
            ? null
            : new HonuaAdminFieldDomain
            {
                Name = string.IsNullOrWhiteSpace(domainName) ? fieldName : domainName,
                Type = "codedValue",
                CodedValues = codedValues
                    .Select(cv => new HonuaAdminCodedValue { Code = ParseCode(cv.Code, current.Type), Name = cv.Label })
                    .ToArray(),
            };

        var request = new HonuaAdminLayerFieldsUpdate
        {
            Fields = [new HonuaAdminLayerFieldUpdate { Name = fieldName, Alias = current.Alias, Domain = domain }],
        };

        var result = await _client.UpdateLayerFieldsAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetDomainResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = domain is null
                    ? $"Cleared the domain on '{fieldName}'."
                    : $"Set a coded-value domain with {codedValues.Count} value(s) on '{fieldName}'.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetDomainResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the field-domain update.",
        };
    }

    public async Task<ConsoleSetDomainResult> SetDomainAsync(
        int layerId,
        ConsoleDomainAuthoring authoring,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authoring);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoring.FieldName);

        var fieldName = authoring.FieldName;
        var mergePolicy = string.IsNullOrWhiteSpace(authoring.MergePolicy) ? null : authoring.MergePolicy.Trim();
        var splitPolicy = string.IsNullOrWhiteSpace(authoring.SplitPolicy) ? null : authoring.SplitPolicy.Trim();

        if (authoring.DefaultValueIntent != ConsoleDefaultValueIntent.Unchanged)
        {
            return Failure("Unsupported",
                $"The field metadata endpoint does not support changing the default value on '{fieldName}'.");
        }

        var currentResult = await GetCurrentFieldAsync(layerId, fieldName, cancellationToken).ConfigureAwait(false);
        if (currentResult.Field is not { } current)
        {
            return currentResult.Failure!;
        }

        if (authoring.Kind == ConsoleDomainKind.CodedValue &&
            authoring.CodedValues.Any(cv => !CanParseCode(cv.Code, current.Type)))
        {
            return Failure("Invalid", $"Every code for numeric field '{fieldName}' must be a JSON number.");
        }

        HonuaAdminFieldDomain? domain = authoring.Kind switch
        {
            // An empty coded-value list clears the domain (Domain = null) — mirrors SetCodedValueDomainAsync.
            ConsoleDomainKind.CodedValue when authoring.CodedValues.Count > 0 => new HonuaAdminFieldDomain
            {
                Name = string.IsNullOrWhiteSpace(authoring.DomainName) ? fieldName : authoring.DomainName,
                Type = "codedValue",
                CodedValues = authoring.CodedValues
                    .Select(cv => new HonuaAdminCodedValue { Code = ParseCode(cv.Code, current.Type), Name = cv.Label })
                    .ToArray(),
                MergePolicy = mergePolicy,
                SplitPolicy = splitPolicy,
            },
            ConsoleDomainKind.Range when authoring.RangeMin is { } min && authoring.RangeMax is { } max =>
                new HonuaAdminFieldDomain
                {
                    Name = string.IsNullOrWhiteSpace(authoring.DomainName) ? fieldName : authoring.DomainName,
                    Type = "range",
                    Range = [min, max],
                    MergePolicy = mergePolicy,
                    SplitPolicy = splitPolicy,
                },
            _ => null,
        };

        // Validate a range domain before issuing the PUT so the operator gets an honest local message rather
        // than a server round-trip for an obviously invalid bound.
        if (authoring.Kind == ConsoleDomainKind.Range)
        {
            if (authoring.RangeMin is not { } rmin || authoring.RangeMax is not { } rmax)
            {
                return Failure("Invalid", $"Enter both a minimum and maximum for the range domain on '{fieldName}'.");
            }

            if (rmin > rmax)
            {
                return Failure("Invalid", $"The range minimum must be ≤ the maximum on '{fieldName}'.");
            }
        }

        var update = new HonuaAdminLayerFieldUpdate
        {
            Name = fieldName,
            Alias = current.Alias,
            Domain = domain,
        };

        var request = new HonuaAdminLayerFieldsUpdate { Fields = [update] };
        var result = await _client.UpdateLayerFieldsAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetDomainResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = DescribeAuthoring(fieldName, authoring, domain),
            };
        }

        var issue = result.Issue;
        return new ConsoleSetDomainResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the field-domain update.",
        };
    }

    public async Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
        int layerId,
        string fieldName,
        string? alias,
        bool hidden,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var currentResult = await GetCurrentFieldAsync(layerId, fieldName, cancellationToken).ConfigureAwait(false);
        if (currentResult.Field is not { } current)
        {
            return currentResult.Failure!;
        }

        // The endpoint replaces alias and domain together, so carry the domain read above while changing the
        // alias/visibility. An empty alias is normalized to null to explicitly clear the prior override.
        var normalizedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        var request = new HonuaAdminLayerFieldsUpdate
        {
            Fields =
            [
                new HonuaAdminLayerFieldUpdate
                {
                    Name = fieldName,
                    Alias = normalizedAlias,
                    Domain = current.Domain,
                    Hidden = hidden,
                }
            ],
        };

        var result = await _client.UpdateLayerFieldsAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetDomainResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = $"Set alias '{normalizedAlias ?? "(none)"}' and {(hidden ? "hidden" : "visible")} on '{fieldName}'.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetDomainResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the field configuration update.",
        };
    }

    private static ConsoleSetDomainResult Failure(string state, string detail) =>
        new() { Succeeded = false, State = state, Detail = detail };

    private async Task<(HonuaAdminLayerField? Field, ConsoleSetDomainResult? Failure)> GetCurrentFieldAsync(
        int layerId, string fieldName, CancellationToken cancellationToken)
    {
        var result = await _client.GetLayerFieldsAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            return (null, Failure(result.Issue?.State ?? "Unavailable",
                result.Issue?.Detail ?? "The current field configuration could not be read before updating it."));
        }

        var field = (result.Data.Fields ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, fieldName, StringComparison.Ordinal));
        return field is null
            ? (null, Failure("Missing", $"Field '{fieldName}' was not returned by the field metadata endpoint."))
            : (field, null);
    }

    private static string FormatCode(JsonElement code) => code.ValueKind switch
    {
        JsonValueKind.String => code.GetString() ?? string.Empty,
        JsonValueKind.Undefined => string.Empty,
        _ => code.GetRawText(),
    };

    private static bool CanParseCode(string code, string? fieldType) =>
        !IsNumericField(fieldType) || ParseJsonScalar(code)?.ValueKind == JsonValueKind.Number;

    private static JsonElement ParseCode(string code, string? fieldType)
    {
        if (IsNumericField(fieldType))
        {
            return ParseJsonScalar(code)!.Value;
        }

        return ParseJsonScalar(JsonSerializer.Serialize(code))!.Value;
    }

    private static bool IsNumericField(string? fieldType) => fieldType is
        "esriFieldTypeSmallInteger" or "esriFieldTypeInteger" or "esriFieldTypeSingle" or
        "esriFieldTypeDouble" or "esriFieldTypeOID";

    private static ConsoleDomainKind MapDomainKind(HonuaAdminFieldDomain? domain) =>
        domain?.Type switch
        {
            null => ConsoleDomainKind.None,
            var t when string.Equals(t, "range", StringComparison.OrdinalIgnoreCase) => ConsoleDomainKind.Range,
            var t when string.Equals(t, "codedValue", StringComparison.OrdinalIgnoreCase) => ConsoleDomainKind.CodedValue,
            // An unknown/absent type with coded values still reads as coded-value so the table renders it.
            _ when domain.CodedValues.Count > 0 => ConsoleDomainKind.CodedValue,
            _ when domain.Range is { Count: > 0 } => ConsoleDomainKind.Range,
            _ => ConsoleDomainKind.None,
        };

    // Renders a persisted JSON scalar default value back to the text the editor binds. JSON strings are shown
    // without quotes (the operator typed them unquoted); other scalars use their JSON text.
    private static string? FormatDefaultValue(JsonElement? value)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();
    }

    private static JsonElement? ParseJsonScalar(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribeAuthoring(string fieldName, ConsoleDomainAuthoring authoring, HonuaAdminFieldDomain? domain)
    {
        var parts = new List<string>();
        parts.Add(domain switch
        {
            { Type: "range", Range: { } r } => string.Create(
                CultureInfo.InvariantCulture, $"Set a range domain [{r[0]}, {r[1]}]"),
            { Type: "codedValue", CodedValues: { } cv } => $"Set a coded-value domain with {cv.Count} value(s)",
            null when authoring.Kind == ConsoleDomainKind.None => "Cleared the domain",
            _ => "Updated the domain",
        });

        if (!string.IsNullOrWhiteSpace(authoring.MergePolicy) || !string.IsNullOrWhiteSpace(authoring.SplitPolicy))
        {
            parts.Add($"policies (merge={authoring.MergePolicy ?? "—"}, split={authoring.SplitPolicy ?? "—"})");
        }

        parts.Add(authoring.DefaultValueIntent switch
        {
            ConsoleDefaultValueIntent.Set => $"default '{authoring.DefaultValueText}'",
            ConsoleDefaultValueIntent.Clear => "cleared default",
            _ => null,
        } ?? string.Empty);

        var description = string.Join(", ", parts.Where(p => p.Length > 0));
        return $"{description} on '{fieldName}'.";
    }
}
