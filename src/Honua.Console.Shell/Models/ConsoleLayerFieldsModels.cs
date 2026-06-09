namespace Honua.Console.Shell.Models;

/// <summary>
/// A layer's field configuration as read from honua-server: each field's type, alias, coded-value domain and
/// visibility. <see cref="Bound"/> is false (with <see cref="Detail"/>) when no server is configured or the
/// fields could not be read.
/// </summary>
public sealed record ConsoleLayerFields
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public IReadOnlyList<ConsoleLayerField> Fields { get; init; } = [];

    public static ConsoleLayerFields Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>One layer field's configuration.</summary>
public sealed record ConsoleLayerField
{
    public required string Name { get; init; }

    public string? Type { get; init; }

    public string? Alias { get; init; }

    /// <summary>The domain name, or null when the field has no domain.</summary>
    public string? DomainName { get; init; }

    /// <summary>The domain kind on this field, or <see cref="ConsoleDomainKind.None"/> when there is no domain.</summary>
    public ConsoleDomainKind DomainKind { get; init; } = ConsoleDomainKind.None;

    public IReadOnlyList<ConsoleCodedValue> CodedValues { get; init; } = [];

    /// <summary>Range minimum for a range domain (null unless <see cref="DomainKind"/> is Range).</summary>
    public double? RangeMin { get; init; }

    /// <summary>Range maximum for a range domain (null unless <see cref="DomainKind"/> is Range).</summary>
    public double? RangeMax { get; init; }

    /// <summary>Esri merge-policy token, or null when unset.</summary>
    public string? MergePolicy { get; init; }

    /// <summary>Esri split-policy token, or null when unset.</summary>
    public string? SplitPolicy { get; init; }

    /// <summary>The persisted default value rendered as text (JSON scalar), or null when the field has none.</summary>
    public string? DefaultValueText { get; init; }

    public bool Hidden { get; init; }
}

/// <summary>The kind of domain authored on a field.</summary>
public enum ConsoleDomainKind
{
    None,
    CodedValue,
    Range
}

/// <summary>A single code/label pair in a coded-value domain.</summary>
public sealed record ConsoleCodedValue(string Code, string Label);

/// <summary>
/// One field's domain + default-value authoring intent, sent in a single field update. <see cref="Kind"/>
/// selects the domain shape: coded-value (<see cref="CodedValues"/>) or range
/// (<see cref="RangeMin"/>/<see cref="RangeMax"/>). <see cref="ConsoleDomainKind.None"/> clears the domain.
/// </summary>
public sealed record ConsoleDomainAuthoring
{
    public required string FieldName { get; init; }

    public string? DomainName { get; init; }

    public ConsoleDomainKind Kind { get; init; } = ConsoleDomainKind.None;

    public IReadOnlyList<ConsoleCodedValue> CodedValues { get; init; } = [];

    public double? RangeMin { get; init; }

    public double? RangeMax { get; init; }

    public string? MergePolicy { get; init; }

    public string? SplitPolicy { get; init; }

    /// <summary>
    /// How to treat the per-field default value: leave the persisted default untouched, clear it (send JSON
    /// null), or set it to <see cref="DefaultValueText"/> parsed as a JSON scalar.
    /// </summary>
    public ConsoleDefaultValueIntent DefaultValueIntent { get; init; } = ConsoleDefaultValueIntent.Unchanged;

    /// <summary>The raw text the operator entered for the default value (parsed when intent is Set).</summary>
    public string? DefaultValueText { get; init; }
}

/// <summary>Whether a field update leaves, clears, or sets the per-field default value.</summary>
public enum ConsoleDefaultValueIntent
{
    Unchanged,
    Clear,
    Set
}

/// <summary>Outcome of setting/clearing a field's coded-value domain.</summary>
public sealed record ConsoleSetDomainResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public static ConsoleSetDomainResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}
