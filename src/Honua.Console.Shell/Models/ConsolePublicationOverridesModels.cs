namespace Honua.Console.Shell.Models;

/// <summary>
/// A publication's overrides as read from honua-server (titleOverride, per-publication field aliases,
/// capabilities, supported formats, isPrimary). A "publication" is a layer's exposure within a service. This
/// drives the publication-overrides authoring page. <see cref="Bound"/> is false (with <see cref="Detail"/>)
/// when no server is configured or the overrides could not be read. Never fabricated (Console Patterns
/// Charter section 11).
/// </summary>
public sealed record ConsolePublicationOverrides
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public string? PublicationId { get; init; }

    public string? TitleOverride { get; init; }

    public IReadOnlyList<ConsolePublicationFieldAlias> FieldAliases { get; init; } = [];

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<string> SupportedFormats { get; init; } = [];

    public bool IsPrimary { get; init; }

    public static ConsolePublicationOverrides Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>One per-publication field alias row (field name → display alias).</summary>
public sealed record ConsolePublicationFieldAlias
{
    public string? Field { get; init; }

    public string? Alias { get; init; }
}

/// <summary>Outcome of saving a publication's overrides.</summary>
public sealed record ConsoleSavePublicationOverridesResult : ConsoleOperationResult<ConsoleSavePublicationOverridesResult>;
