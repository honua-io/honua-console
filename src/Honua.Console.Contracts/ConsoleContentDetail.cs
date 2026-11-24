namespace Honua.Console.Contracts;

public sealed record ConsoleContentDetail
{
    public ConsoleContentSummary Summary { get; init; } = new();

    public string Description { get; init; } = string.Empty;

    public string? License { get; init; }

    public string[] Capabilities { get; init; } = [];

    public ConsoleContentVersion[] Versions { get; init; } = [];

    public ConsoleContentLineageLink[] Lineage { get; init; } = [];

    public ConsoleContentBinding[] Bindings { get; init; } = [];

    public ConsoleContentPublication[] Publications { get; init; } = [];

    public ConsoleContentPermission[] Permissions { get; init; } = [];

    public ConsoleContentActivity[] Activity { get; init; } = [];

    public ConsoleContentUsage[] Usage { get; init; } = [];

    // False means no usage source was bound, so the catalog must show missing-binding rather than an empty consumer list.
    public bool UsageBound { get; init; }
}

public sealed record ConsoleContentPublication(string Route, string Visibility, bool Embeddable, string Status)
{
    public string? Format { get; init; }
}
