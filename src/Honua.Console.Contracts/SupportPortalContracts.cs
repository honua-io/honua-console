using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class SupportPortalRoutes
{
    public const string UpstreamPreflight = "api/v1/intake/preflight";

    public const string UpstreamTickets = "api/v1/tickets";

    public const string UpstreamSecurityAdvisories = "api/v1/security/advisories";

    public const string UpstreamFleetInstances = "api/v1/fleet/instances";

    public const string BffPreflight = "api/support/preflight";

    public const string BffTickets = "api/support/tickets";

    public const string BffSecurityAdvisories = "api/support/security/advisories";

    public const string BffFleetInstances = "api/support/fleet/instances";

    public static string UpstreamApproveTicket(string id) =>
        $"{UpstreamTickets}/{Uri.EscapeDataString(id)}/approve";

    public static string UpstreamRevokeAccess(string id) =>
        $"{UpstreamTickets}/{Uri.EscapeDataString(id)}/revoke-access";

    public static string BffApproveTicket(string id) =>
        $"{BffTickets}/{Uri.EscapeDataString(id)}/approve";

    public static string BffRevokeAccess(string id) =>
        $"{BffTickets}/{Uri.EscapeDataString(id)}/revoke-access";
}

public sealed record SupportClientContextRequest
{
    public string? ClientName { get; init; }

    public string? ClientVersion { get; init; }

    public string? Runtime { get; init; }

    public string? ServerVersion { get; init; }
}

public sealed record SupportPreflightRequest
{
    public string? Severity { get; init; }

    public string? Environment { get; init; }

    public string? Service { get; init; }

    public string? Symptoms { get; init; }

    public string? ProductArea { get; init; }

    public string? IssueKind { get; init; }

    public SupportClientContextRequest? ClientContext { get; init; }

    public SupportContextRequest? Context { get; init; }
}

public sealed record SupportPreflightCompatibilityResponse
{
    public bool Deflected { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Confidence { get; init; } = string.Empty;

    public IReadOnlyList<string> GuidedCommands { get; init; } = [];

    public IReadOnlyList<string> ValidationSteps { get; init; } = [];
}

public sealed record SupportPreflightKnownIssueResponse
{
    public string Component { get; init; } = string.Empty;

    public string? Fingerprint { get; init; }

    public string? PatchStage { get; init; }

    public string? FixedInServerVersion { get; init; }

    public string? FixedInSdkVersion { get; init; }

    public string? ReleaseVersion { get; init; }

    public string? ReleaseNotesUrl { get; init; }
}

public sealed record SupportPreflightEnvelopePreviewResponse
{
    public string Method { get; init; } = string.Empty;

    public string NormalizedPath { get; init; } = string.Empty;

    public int? StatusCode { get; init; }

    public int HeadersDropped { get; init; }

    public IReadOnlyList<string> RetainedHeaderNames { get; init; } = [];

    public string? RequestBodyPreview { get; init; }

    public string? ResponseBodyPreview { get; init; }
}

public sealed record SupportPreflightRedactionPreviewResponse
{
    public int HeadersDropped { get; init; }

    public int BodiesRedacted { get; init; }

    public bool ScopedKeyWillBeBrokered { get; init; }

    public bool CustomerDataIncluded { get; init; }

    public IReadOnlyList<SupportPreflightEnvelopePreviewResponse> SanitizedEnvelopes { get; init; } = [];
}

public sealed record SupportPreflightResponse
{
    public string ProductArea { get; init; } = string.Empty;

    public string IssueKind { get; init; } = string.Empty;

    public string ClassificationProvenance { get; init; } = string.Empty;

    public SupportPreflightCompatibilityResponse? Compatibility { get; init; }

    public IReadOnlyList<SupportPreflightKnownIssueResponse> KnownIssues { get; init; } = [];

    public SupportPreflightRedactionPreviewResponse? RedactionPreview { get; init; }

    public string? RecommendedEvidenceCommand { get; init; }
}

public sealed record SupportApproveTicketRequest
{
    public string? AccessMode { get; init; }

    public int? TtlMinutes { get; init; }

    public string? Reason { get; init; }
}

public sealed record SupportRevokeAccessRequest
{
    public string? Reason { get; init; }
}

public sealed record SupportRevokeAccessResponse
{
    public string TicketId { get; init; } = string.Empty;

    public bool ScopedCredentialRevoked { get; init; }

    public bool AwsGrantRevoked { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public SupportTicketResponse? Ticket { get; init; }
}

public sealed record SupportAdvisoryInstanceView
{
    public string InstanceId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? ObservedVersion { get; init; }

    public string? ObservedDigest { get; init; }

    public DateTimeOffset? PatchedAt { get; init; }

    public string? VerifiedBy { get; init; }
}

public sealed record SupportSecurityAdvisoryItem
{
    public string AdvisoryId { get; init; } = string.Empty;

    public string? CveId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public bool CisaKev { get; init; }

    public string Component { get; init; } = string.Empty;

    public string VexStatus { get; init; } = string.Empty;

    public string? VexJustification { get; init; }

    public string? CustomerStatus { get; init; }

    public IReadOnlyList<SupportAdvisoryInstanceView> Instances { get; init; } = [];

    public string? FixedVersion { get; init; }

    public string? FixedDigest { get; init; }

    public string? ContainmentGuidance { get; init; }

    public string? ContainmentActionId { get; init; }

    public string? UpgradeCommand { get; init; }

    public string? ReleaseNotesUrl { get; init; }

    public string? LinkedTicketId { get; init; }

    public bool Embargoed { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record SupportSecurityAdvisoriesResponse
{
    public int Count { get; init; }

    public IReadOnlyList<SupportSecurityAdvisoryItem> Advisories { get; init; } = [];
}

public sealed record SupportFleetInstanceItem
{
    public string CustomerId { get; init; } = string.Empty;

    public string InstanceId { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string? Version { get; init; }

    public string? ArtifactDigest { get; init; }

    public string? PlatformLockDigest { get; init; }

    public string? EnvKind { get; init; }

    public string? InstanceUrl { get; init; }

    public DateTimeOffset FirstSeenAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    public string LastSource { get; init; } = string.Empty;
}

public sealed record SupportFleetInstancesResponse
{
    public int Count { get; init; }

    public IReadOnlyList<SupportFleetInstanceItem> Instances { get; init; } = [];
}

public sealed record SupportPortalErrorPayload
{
    public string? Error { get; init; }

    public string? Message { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SupportClientContextRequest))]
[JsonSerializable(typeof(SupportPreflightRequest))]
[JsonSerializable(typeof(SupportPreflightResponse))]
[JsonSerializable(typeof(SupportApproveTicketRequest))]
[JsonSerializable(typeof(SupportRevokeAccessRequest))]
[JsonSerializable(typeof(SupportRevokeAccessResponse))]
[JsonSerializable(typeof(SupportSecurityAdvisoriesResponse))]
[JsonSerializable(typeof(SupportFleetInstancesResponse))]
[JsonSerializable(typeof(SupportPortalErrorPayload))]
[JsonSerializable(typeof(CreateSupportTicketRequest))]
[JsonSerializable(typeof(SupportTicketResponse))]
[JsonSerializable(typeof(IReadOnlyList<SupportTicketResponse>))]
[JsonSerializable(typeof(SupportTicketResponse[]))]
public sealed partial class SupportPortalJsonContext : JsonSerializerContext;
