namespace Honua.Console.Shell.Models;

public sealed record ConsoleServiceImportResult : ConsoleOperationResult<ConsoleServiceImportResult>
{
    public string? ServiceType { get; init; }

    public string? ServiceName { get; init; }

    public int? Srid { get; init; }

    public bool IsCatalog { get; init; }

    public IReadOnlyList<ConsoleServiceImportLayer> Layers { get; init; } = [];

    public IReadOnlyList<ConsoleServiceImportService> Services { get; init; } = [];
}

public sealed record ConsoleServiceImportService
{
    public required string ServiceName { get; init; }

    public string? ServiceType { get; init; }

    public string? FolderPath { get; init; }

    public string? ServiceUrl { get; init; }

    public int? Srid { get; init; }

    public IReadOnlyList<ConsoleServiceImportLayer> Layers { get; init; } = [];
}

public sealed record ConsoleServiceImportLayer
{
    public int? LayerId { get; init; }

    public required string Name { get; init; }

    public string? GeometryType { get; init; }

    public int? FeatureCount { get; init; }

    public string? ServiceUrl { get; init; }
}

public sealed record ConsoleServiceImportRunRequest
{
    public required string ServiceUrl { get; init; }

    public required int LayerId { get; init; }

    public required string TableName { get; init; }

    public string? TargetSchema { get; init; }

    public bool OverwriteExisting { get; init; }

    public bool AutoPublish { get; init; } = true;

    public string? ServiceName { get; init; }

    public ConsoleServiceImportAuth? Auth { get; init; }
}

public sealed record ConsoleServiceImportRun
{
    public bool Succeeded { get; init; }

    public string? JobId { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }
}

public sealed record ConsoleServiceImportJob
{
    public required string JobId { get; init; }

    public required string Status { get; init; }

    public bool Terminal { get; init; }

    public bool Succeeded { get; init; }

    // A transport failure or HTTP 5xx leaves the import job active for another poll.
    public bool TransientFailure { get; init; }

    public int FeaturesProcessed { get; init; }

    public int? EstimatedTotalFeatures { get; init; }

    public double? PercentComplete { get; init; }

    public string? TableName { get; init; }

    public int? PublishedLayerId { get; init; }

    public string? Detail { get; init; }
}

// Used for discovery and layer-import requests. The console does not persist these secrets.
public sealed record ConsoleServiceImportAuth
{
    // One of anonymous, arcgis-token, token, basic, or oauth.
    public required string Mode { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Token { get; init; }

    public string? TokenUrl { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? Referer { get; init; }

    public bool RequiresCredentials =>
        !string.IsNullOrWhiteSpace(Mode) &&
        !string.Equals(Mode, "anonymous", StringComparison.OrdinalIgnoreCase);
}
