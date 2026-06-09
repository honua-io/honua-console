using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer subtypes + attribute-rules operation. Reads and authors a layer's subtype
/// set and attribute rules on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps the result
/// (or rejection). It never fabricates success — every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsoleLayerSubtypesOperation : IConsoleLayerSubtypesOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerSubtypesOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ---- Subtypes ----

    public async Task<ConsoleLayerSubtypes> GetSubtypesAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerSubtypesAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerSubtypes
            {
                Bound = true,
                LayerId = data.LayerId,
                SubtypeField = data.SubtypeField,
                DefaultSubtypeCode = JsonToText(data.DefaultSubtypeCode),
                Subtypes = (data.Subtypes ?? []).Select(Map).ToArray(),
            };
        }

        return ConsoleLayerSubtypes.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return subtypes for this layer.");
    }

    public async Task<ConsoleSetSubtypesResult> SetSubtypesAsync(
        int layerId,
        string? subtypeField,
        string? defaultSubtypeCode,
        bool clear,
        IReadOnlyList<ConsoleLayerSubtype> subtypes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subtypes);

        HonuaAdminLayerSubtypesUpdate request;
        if (clear)
        {
            request = new HonuaAdminLayerSubtypesUpdate { Clear = true };
        }
        else
        {
            request = new HonuaAdminLayerSubtypesUpdate
            {
                Clear = false,
                SubtypeField = string.IsNullOrWhiteSpace(subtypeField) ? null : subtypeField,
                DefaultSubtypeCode = ParseJson(defaultSubtypeCode),
                Subtypes = subtypes.Select(ToAdmin).ToArray(),
            };
        }

        var result = await _client.UpdateLayerSubtypesAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetSubtypesResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = clear
                    ? "Cleared the subtype set on this layer."
                    : $"Saved {subtypes.Count} subtype(s) on this layer.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetSubtypesResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the subtypes update.",
        };
    }

    // ---- Attribute rules ----

    public async Task<ConsoleLayerAttributeRules> GetAttributeRulesAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerAttributeRulesAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerAttributeRules
            {
                Bound = true,
                LayerId = data.LayerId,
                Rules = (data.Rules ?? []).Select(Map).ToArray(),
            };
        }

        return ConsoleLayerAttributeRules.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return attribute rules for this layer.");
    }

    public async Task<ConsoleSetAttributeRulesResult> SetAttributeRulesAsync(
        int layerId,
        IReadOnlyList<ConsoleAttributeRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var request = new HonuaAdminLayerAttributeRulesUpdate
        {
            Rules = rules.Select(rule => new HonuaAdminAttributeRule
            {
                Name = rule.Name,
                Type = rule.Type,
                FieldName = rule.FieldName,
                ScriptExpression = rule.ScriptExpression,
                TriggeringEvents = rule.TriggeringEvents ?? [],
                ErrorMessage = rule.ErrorMessage,
                IsEnabled = rule.IsEnabled,
            }).ToArray(),
        };

        var result = await _client.UpdateLayerAttributeRulesAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetAttributeRulesResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = rules.Count == 0
                    ? "Cleared all attribute rules on this layer."
                    : $"Saved {rules.Count} attribute rule(s) on this layer.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetAttributeRulesResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the attribute-rules update.",
        };
    }

    // ---- Mapping helpers ----

    private static ConsoleLayerSubtype Map(HonuaAdminLayerSubtype subtype) => new()
    {
        Code = JsonToText(subtype.Code),
        Name = subtype.Name,
        FieldOverrides = (subtype.FieldOverrides ?? new Dictionary<string, HonuaAdminSubtypeFieldOverride>())
            .Select(pair => new ConsoleSubtypeFieldOverride
            {
                FieldName = pair.Key,
                DefaultValueJson = JsonToText(pair.Value.DefaultValue),
                DomainJson = JsonToText(pair.Value.Domain),
            })
            .ToArray(),
    };

    private static HonuaAdminLayerSubtype ToAdmin(ConsoleLayerSubtype subtype)
    {
        var overrides = new Dictionary<string, HonuaAdminSubtypeFieldOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var ovr in subtype.FieldOverrides ?? [])
        {
            if (string.IsNullOrWhiteSpace(ovr.FieldName))
            {
                continue;
            }

            overrides[ovr.FieldName] = new HonuaAdminSubtypeFieldOverride
            {
                DefaultValue = ParseJson(ovr.DefaultValueJson),
                Domain = ParseJson(ovr.DomainJson),
            };
        }

        return new HonuaAdminLayerSubtype
        {
            Code = ParseJson(subtype.Code),
            Name = subtype.Name,
            FieldOverrides = overrides,
        };
    }

    private static ConsoleAttributeRule Map(HonuaAdminAttributeRule rule) => new()
    {
        Name = rule.Name,
        Type = rule.Type,
        FieldName = rule.FieldName,
        ScriptExpression = rule.ScriptExpression,
        TriggeringEvents = rule.TriggeringEvents ?? [],
        ErrorMessage = rule.ErrorMessage,
        IsEnabled = rule.IsEnabled,
    };

    /// <summary>
    /// Parses operator-typed text into a JSON scalar passthrough. A blank value is omitted (null). Bare text
    /// that is not already valid JSON (e.g. a code like <c>active</c>) is treated as a JSON string so the
    /// operator does not have to type quotes for the common case.
    /// </summary>
    private static JsonElement? ParseJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Not valid JSON as typed — wrap it as a JSON string literal.
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(trimmed));
            return document.RootElement.Clone();
        }
    }

    /// <summary>Renders a JSON passthrough back to compact text for editing; a string is shown unquoted.</summary>
    private static string? JsonToText(JsonElement? element)
    {
        if (element is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }
}
