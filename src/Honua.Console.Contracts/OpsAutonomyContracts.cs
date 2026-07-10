using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): honua-server's graduated ops-autonomy policy API
// (honua-server#2557, Features/Admin/OpsObservabilityEndpoints.cs) is not yet
// projected by the public .NET SDK. These records mirror only the shipped HTTP
// wire contract. Responses are bare camelCase JSON, not ApiResponse envelopes.

/// <summary>Concrete v1 routes for the graduated ops-autonomy policy API.</summary>
public static class OpsAutonomyRoutes
{
    /// <summary>Lists effective per-rule policies and their graduation evidence.</summary>
    [OpsParityRoute("GET")]
    public const string Policies = "api/v1/admin/observability/autonomy/policies";

    /// <summary>Addresses one rule's durable policy.</summary>
    [OpsParityRoute("PUT")]
    public const string PolicyTemplate = Policies + "/{rule}";

    /// <summary>Reads the global autonomy settings.</summary>
    [OpsParityRoute("GET")]
    public const string Settings = "api/v1/admin/observability/autonomy/settings";

    /// <summary>Updates the global autonomy settings.</summary>
    [OpsParityRoute("PUT")]
    public const string SettingsUpdate = Settings;

    /// <summary>Builds the policy route for a server-issued rule identifier.</summary>
    public static string Policy(string rule) =>
        PolicyTemplate.Replace("{rule}", Uri.EscapeDataString(rule), StringComparison.Ordinal);
}

/// <summary>Response from the per-rule autonomy policy list.</summary>
public sealed record OpsAutonomyPolicyListResponse
{
    /// <summary>Gets the UTC time the list was generated.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Gets the effective policy rows reported by the connected server.</summary>
    [JsonPropertyName("policies")]
    public IReadOnlyList<OpsAutonomyPolicyResponse> Policies { get; init; } = [];
}

/// <summary>One server-confirmed per-rule autonomy policy.</summary>
public sealed record OpsAutonomyPolicyResponse
{
    /// <summary>Gets the deterministic finding rule identifier.</summary>
    [JsonPropertyName("rule")]
    public string Rule { get; init; } = string.Empty;

    /// <summary>Gets the mode (<c>ProposeOnly</c> or <c>AutoApply</c>).</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    /// <summary>Gets the rolling-window auto-action cap.</summary>
    [JsonPropertyName("maxAutoActionsPerWindow")]
    public int MaxAutoActionsPerWindow { get; init; }

    /// <summary>Gets the rolling-window duration in seconds.</summary>
    [JsonPropertyName("windowSeconds")]
    public int WindowSeconds { get; init; }

    /// <summary>Gets the maximum server-allowed blast radius.</summary>
    [JsonPropertyName("maxBlastRadius")]
    public int MaxBlastRadius { get; init; }

    /// <summary>Gets the last update time.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Gets the last update actor.</summary>
    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    /// <summary>
    /// Gets whether this row is a durable override. Newer servers return false for an
    /// effective config/default projection; null means an older server omitted the marker.
    /// </summary>
    [JsonPropertyName("isPersisted")]
    public bool? IsPersisted { get; init; }

    /// <summary>Gets the server-owned graduation evidence counters.</summary>
    [JsonPropertyName("trackRecord")]
    public OpsAutonomyTrackRecordResponse TrackRecord { get; init; } = new();
}

/// <summary>Server-confirmed global autonomy settings.</summary>
public sealed record OpsAutonomySettingsResponse
{
    /// <summary>Gets whether every rule is forced to propose-only at route time.</summary>
    [JsonPropertyName("killSwitchEnabled")]
    public bool KillSwitchEnabled { get; init; }

    /// <summary>Gets the last settings update time.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Gets the last settings update actor.</summary>
    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }
}

/// <summary>Aggregate per-rule evidence used to justify autonomy graduation.</summary>
public sealed record OpsAutonomyTrackRecordResponse
{
    /// <summary>Gets the number of proposals raised.</summary>
    [JsonPropertyName("proposalsRaised")]
    public long ProposalsRaised { get; init; }

    /// <summary>Gets the number of proposals approved.</summary>
    [JsonPropertyName("proposalsApproved")]
    public long ProposalsApproved { get; init; }

    /// <summary>Gets the number of proposals rejected.</summary>
    [JsonPropertyName("proposalsRejected")]
    public long ProposalsRejected { get; init; }

    /// <summary>Gets the number of successful autonomous outcomes.</summary>
    [JsonPropertyName("autoApplied")]
    public long AutoApplied { get; init; }

    /// <summary>Gets the number of rolled-back autonomous outcomes.</summary>
    [JsonPropertyName("rolledBack")]
    public long RolledBack { get; init; }

    /// <summary>Gets the number of failed autonomous outcomes.</summary>
    [JsonPropertyName("failed")]
    public long Failed { get; init; }

    /// <summary>Gets the first known rule activity time.</summary>
    [JsonPropertyName("firstActivityAt")]
    public DateTimeOffset? FirstActivityAt { get; init; }

    /// <summary>Gets the most recent rule activity time.</summary>
    [JsonPropertyName("lastActivityAt")]
    public DateTimeOffset? LastActivityAt { get; init; }
}

/// <summary>Request to change one rule's policy mode.</summary>
public sealed record OpsAutonomyPolicyUpdateRequest
{
    /// <summary>Gets the requested mode.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    /// <summary>Gets the operator reason persisted in server audit.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>Request to change the global kill switch.</summary>
public sealed record OpsAutonomySettingsUpdateRequest
{
    /// <summary>Gets the requested kill-switch state.</summary>
    [JsonPropertyName("killSwitchEnabled")]
    public bool KillSwitchEnabled { get; init; }

    /// <summary>Gets the operator reason persisted in server audit.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>Source-generated JSON metadata for the autonomy shim.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpsAutonomyPolicyListResponse))]
[JsonSerializable(typeof(OpsAutonomyPolicyResponse))]
[JsonSerializable(typeof(OpsAutonomySettingsResponse))]
[JsonSerializable(typeof(OpsAutonomyPolicyUpdateRequest))]
[JsonSerializable(typeof(OpsAutonomySettingsUpdateRequest))]
public sealed partial class OpsAutonomyJsonContext : JsonSerializerContext;
