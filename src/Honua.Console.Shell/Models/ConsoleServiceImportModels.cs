namespace Honua.Console.Shell.Models;

/// <summary>
/// Outcome of discovering a remote Esri/OGC service or catalog. On success, <see cref="Succeeded"/> is
/// <c>true</c> and the service(s)/layers are populated. On failure (missing binding, non-HTTPS URL,
/// unreachable service, rejected credentials) <see cref="State"/> carries the neutral state vocabulary token
/// and <see cref="Detail"/> the reason.
/// </summary>
public sealed record ConsoleServiceImportResult
{
    public bool Succeeded { get; init; }

    /// <summary>State vocabulary token (e.g. "Discovered", "Missing binding", "Rejected", "Unavailable").</summary>
    public required string State { get; init; }

    public string? Detail { get; init; }

    public string? ServiceType { get; init; }

    public string? ServiceName { get; init; }

    public int? Srid { get; init; }

    /// <summary>True when the URL was an ArcGIS catalog root/folder enumerated into multiple services.</summary>
    public bool IsCatalog { get; init; }

    /// <summary>Flattened candidate layers across every discovered service.</summary>
    public IReadOnlyList<ConsoleServiceImportLayer> Layers { get; init; } = [];

    /// <summary>Discovered services (one for a single service URL; many for a catalog), grouped by folder.</summary>
    public IReadOnlyList<ConsoleServiceImportService> Services { get; init; } = [];

    public static ConsoleServiceImportResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail,
    };
}

/// <summary>A single service discovered within a catalog (or the sole service for a single-service URL).</summary>
public sealed record ConsoleServiceImportService
{
    public required string ServiceName { get; init; }

    public string? ServiceType { get; init; }

    /// <summary>Catalog folder the service belongs to, or null at the catalog root / for a single service.</summary>
    public string? FolderPath { get; init; }

    public string? ServiceUrl { get; init; }

    public int? Srid { get; init; }

    public IReadOnlyList<ConsoleServiceImportLayer> Layers { get; init; } = [];
}

/// <summary>A candidate layer discovered on a remote service.</summary>
public sealed record ConsoleServiceImportLayer
{
    public int? LayerId { get; init; }

    public required string Name { get; init; }

    public string? GeometryType { get; init; }

    public int? FeatureCount { get; init; }

    /// <summary>Service root URL the layer belongs to (used as part of the selection identity).</summary>
    public string? ServiceUrl { get; init; }
}

/// <summary>
/// Credentials the operator supplies to authenticate against a protected service or catalog. Held only for
/// the duration of a discovery request; the console never persists secrets.
/// </summary>
public sealed record ConsoleServiceImportAuth
{
    /// <summary>anonymous, arcgis-token, token, basic, or oauth.</summary>
    public required string Mode { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Token { get; init; }

    public string? TokenUrl { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? Referer { get; init; }

    /// <summary>True when the mode is anything other than anonymous.</summary>
    public bool RequiresCredentials =>
        !string.IsNullOrWhiteSpace(Mode) &&
        !string.Equals(Mode, "anonymous", StringComparison.OrdinalIgnoreCase);
}
