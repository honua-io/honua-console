namespace Honua.Console.Shell.Models;

/// <summary>
/// A layer's subtype set as read from honua-server (subtype field, default subtype code, per-subtype field
/// overrides). <see cref="Bound"/> is false (with <see cref="Detail"/>) when no server is configured or the
/// subtypes could not be read.
/// </summary>
public sealed record ConsoleLayerSubtypes
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public string? SubtypeField { get; init; }

    /// <summary>The default subtype code rendered as a string for editing; null when none is set.</summary>
    public string? DefaultSubtypeCode { get; init; }

    public IReadOnlyList<ConsoleLayerSubtype> Subtypes { get; init; } = [];

    public static ConsoleLayerSubtypes Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>One subtype row: its code, display name, and per-field default/domain overrides as raw JSON text.</summary>
public sealed record ConsoleLayerSubtype
{
    /// <summary>The subtype code rendered as a string for editing (e.g. "1").</summary>
    public string? Code { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<ConsoleSubtypeFieldOverride> FieldOverrides { get; init; } = [];
}

/// <summary>A per-field override on a subtype: the field name plus default value / domain as raw JSON text.</summary>
public sealed record ConsoleSubtypeFieldOverride
{
    public string? FieldName { get; init; }

    /// <summary>The override default value as raw JSON text (e.g. <c>"active"</c>, <c>3</c>); null/blank = none.</summary>
    public string? DefaultValueJson { get; init; }

    /// <summary>The override domain as raw JSON text; null/blank = none.</summary>
    public string? DomainJson { get; init; }
}

/// <summary>Outcome of saving a layer's subtype set.</summary>
public sealed record ConsoleSetSubtypesResult : ConsoleOperationResult<ConsoleSetSubtypesResult>;

/// <summary>
/// A layer's attribute rules as read from honua-server. <see cref="Bound"/> is false (with
/// <see cref="Detail"/>) when no server is configured or the rules could not be read.
/// </summary>
public sealed record ConsoleLayerAttributeRules
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public IReadOnlyList<ConsoleAttributeRule> Rules { get; init; } = [];

    public static ConsoleLayerAttributeRules Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>One attribute-rule row.</summary>
public sealed record ConsoleAttributeRule
{
    public string? Name { get; init; }

    /// <summary>"calculation", "constraint", or "validation".</summary>
    public string? Type { get; init; }

    public string? FieldName { get; init; }

    public string? ScriptExpression { get; init; }

    /// <summary>Any of "insert", "update", "delete".</summary>
    public IReadOnlyList<string> TriggeringEvents { get; init; } = [];

    public string? ErrorMessage { get; init; }

    public bool IsEnabled { get; init; }
}

/// <summary>Outcome of replacing a layer's attribute-rule set.</summary>
public sealed record ConsoleSetAttributeRulesResult : ConsoleOperationResult<ConsoleSetAttributeRulesResult>;
