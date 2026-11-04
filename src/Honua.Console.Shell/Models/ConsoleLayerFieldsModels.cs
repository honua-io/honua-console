namespace Honua.Console.Shell.Models;

public sealed record ConsoleLayerFields
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public IReadOnlyList<ConsoleLayerField> Fields { get; init; } = [];

    public static ConsoleLayerFields Unbound(string detail) => new() { Bound = false, Detail = detail };
}

public sealed record ConsoleLayerField
{
    public required string Name { get; init; }

    public string? Type { get; init; }

    public string? Alias { get; init; }

    public string? DomainName { get; init; }

    public ConsoleDomainKind DomainKind { get; init; } = ConsoleDomainKind.None;

    public IReadOnlyList<ConsoleCodedValue> CodedValues { get; init; } = [];

    public double? RangeMin { get; init; }

    public double? RangeMax { get; init; }

    public string? MergePolicy { get; init; }

    public string? SplitPolicy { get; init; }

    public string? DefaultValueText { get; init; }

    public bool Hidden { get; init; }
}

public enum ConsoleDomainKind
{
    None,
    CodedValue,
    Range
}

public sealed record ConsoleCodedValue(string Code, string Label);

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

    // Clear sends JSON null. Set parses DefaultValueText as a JSON scalar. Unchanged leaves the stored default.
    public ConsoleDefaultValueIntent DefaultValueIntent { get; init; } = ConsoleDefaultValueIntent.Unchanged;

    public string? DefaultValueText { get; init; }
}

public enum ConsoleDefaultValueIntent
{
    Unchanged,
    Clear,
    Set
}

public sealed record ConsoleSetDomainResult : ConsoleOperationResult<ConsoleSetDomainResult>;
