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

    /// <summary>The coded-value domain name, or null when the field has no domain.</summary>
    public string? DomainName { get; init; }

    public IReadOnlyList<ConsoleCodedValue> CodedValues { get; init; } = [];

    public bool Hidden { get; init; }
}

/// <summary>A single code/label pair in a coded-value domain.</summary>
public sealed record ConsoleCodedValue(string Code, string Label);

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
