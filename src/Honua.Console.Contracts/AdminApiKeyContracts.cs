namespace Honua.Console.Contracts;

// SHIM: API-key models match the merged Admin SDK, but Honua.Sdk.Admin 3.0.0 is not on nuget.org.
// The latest consumable package is 2.2.0. Remove this file when that package restores.
public sealed class CreateAdminApiKeyRequest
{
    public required string Name { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<string>? AllowedReferrers { get; init; }

    public IReadOnlyList<string>? AllowedIpRanges { get; init; }
}

public sealed class AdminApiKeyResponse
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string KeyPrefix { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public required string Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RotatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? CreatedBy { get; init; }

    public IReadOnlyList<string>? AllowedReferrers { get; init; }

    public IReadOnlyList<string>? AllowedIpRanges { get; init; }
}

public sealed class AdminApiKeySecretResponse
{
    public required AdminApiKeyResponse ApiKey { get; init; }

    public required string Key { get; init; }
}
