using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// Responses are the payload itself, not an admin response envelope.
public static class OpsAutonomyRoutes
{
    [OpsParityRoute("GET")]
    public const string Policies = "api/v1/admin/observability/autonomy/policies";

    [OpsParityRoute("PUT")]
    public const string PolicyTemplate = Policies + "/{rule}";

    [OpsParityRoute("GET")]
    public const string Settings = "api/v1/admin/observability/autonomy/settings";

    [OpsParityRoute("PUT")]
    public const string SettingsUpdate = Settings;

    public static string Policy(string rule) =>
        PolicyTemplate.Replace("{rule}", Uri.EscapeDataString(rule), StringComparison.Ordinal);
}

public sealed record OpsAutonomyPolicyListResponse
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("policies")]
    public IReadOnlyList<OpsAutonomyPolicyResponse> Policies { get; init; } = [];
}

public sealed record OpsAutonomyPolicyResponse
{
    [JsonPropertyName("rule")]
    public string Rule { get; init; } = string.Empty;

    // Mode is ProposeOnly or AutoApply.
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    [JsonPropertyName("maxAutoActionsPerWindow")]
    public int MaxAutoActionsPerWindow { get; init; }

    [JsonPropertyName("windowSeconds")]
    public int WindowSeconds { get; init; }

    [JsonPropertyName("maxBlastRadius")]
    public int MaxBlastRadius { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    // Null means the marker was absent; false means an effective default rather than a stored override.
    [JsonPropertyName("isPersisted")]
    public bool? IsPersisted { get; init; }

    [JsonPropertyName("trackRecord")]
    public OpsAutonomyTrackRecordResponse TrackRecord { get; init; } = new();
}

public sealed record OpsAutonomySettingsResponse
{
    // When set, every rule is forced to propose-only.
    [JsonPropertyName("killSwitchEnabled")]
    public bool KillSwitchEnabled { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }
}

public sealed record OpsAutonomyTrackRecordResponse
{
    [JsonPropertyName("proposalsRaised")]
    public long ProposalsRaised { get; init; }

    [JsonPropertyName("proposalsApproved")]
    public long ProposalsApproved { get; init; }

    [JsonPropertyName("proposalsRejected")]
    public long ProposalsRejected { get; init; }

    [JsonPropertyName("autoApplied")]
    public long AutoApplied { get; init; }

    [JsonPropertyName("rolledBack")]
    public long RolledBack { get; init; }

    [JsonPropertyName("failed")]
    public long Failed { get; init; }

    [JsonPropertyName("firstActivityAt")]
    public DateTimeOffset? FirstActivityAt { get; init; }

    [JsonPropertyName("lastActivityAt")]
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed record OpsAutonomyPolicyUpdateRequest
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed record OpsAutonomySettingsUpdateRequest
{
    [JsonPropertyName("killSwitchEnabled")]
    public bool KillSwitchEnabled { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpsAutonomyPolicyListResponse))]
[JsonSerializable(typeof(OpsAutonomyPolicyResponse))]
[JsonSerializable(typeof(OpsAutonomySettingsResponse))]
[JsonSerializable(typeof(OpsAutonomyPolicyUpdateRequest))]
[JsonSerializable(typeof(OpsAutonomySettingsUpdateRequest))]
public sealed partial class OpsAutonomyJsonContext : JsonSerializerContext;
