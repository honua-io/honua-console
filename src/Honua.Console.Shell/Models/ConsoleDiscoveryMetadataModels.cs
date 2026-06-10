namespace Honua.Console.Shell.Models;

/// <summary>
/// A layer's or service's discovery / catalog metadata as read from honua-server (title, description,
/// keywords, themes, language, license, attribution, publisher, contact point, links). This drives the OGC
/// API Records / STAC / DCAT / Esri documentInfo output. <see cref="Bound"/> is false (with <see cref="Detail"/>)
/// when no server is configured or the metadata could not be read.
/// </summary>
public sealed record ConsoleDiscoveryMetadata
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<string> Themes { get; init; } = [];

    public string? Language { get; init; }

    public string? License { get; init; }

    public string? Attribution { get; init; }

    public string? Publisher { get; init; }

    public ConsoleDiscoveryContactPoint? ContactPoint { get; init; }

    public IReadOnlyList<ConsoleDiscoveryLink> Links { get; init; } = [];

    public static ConsoleDiscoveryMetadata Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>Discovery contact point (name / email / url).</summary>
public sealed record ConsoleDiscoveryContactPoint
{
    public string? Name { get; init; }

    public string? Email { get; init; }

    public string? Url { get; init; }
}

/// <summary>One discovery link row (href / rel / type / title / hreflang).</summary>
public sealed record ConsoleDiscoveryLink
{
    public string? Href { get; init; }

    public string? Rel { get; init; }

    public string? Type { get; init; }

    public string? Title { get; init; }

    public string? Hreflang { get; init; }
}

/// <summary>Outcome of saving a layer's or service's discovery metadata.</summary>
public sealed record ConsoleSaveDiscoveryResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public static ConsoleSaveDiscoveryResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}
