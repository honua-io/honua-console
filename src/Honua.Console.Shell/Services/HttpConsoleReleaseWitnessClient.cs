using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Focused read client for the candidate receipt UI. Every value is observed
/// from honua-server through the active operator session; the expected IDs are
/// used only to select and validate server-owned records.
/// </summary>
public sealed partial class HttpConsoleReleaseWitnessClient : IConsoleReleaseWitnessClient
{
    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly string? _adminApiKey;

    public HttpConsoleReleaseWitnessClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _adminApiKey = adminApiKey;
    }

    public async Task<OperateSectionResult<ConsoleReleaseWitnessEvidence>> ObserveAsync(
        ConsoleReleaseWitnessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsFamily(request.Family)
            || HasBlank(request.ItemId, request.VersionId, request.ContentHash, request.ProposalId))
        {
            return Denied("The release witness requires exact map/app/dashboard item, version, content, and proposal identifiers.");
        }

        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return Denied("No active environment profile is selected. Connect the candidate environment first.");
        }

        try
        {
            var version = await GetAsync(
                profile,
                "api/v1/admin/version",
                ReleaseWitnessJsonContext.Default.ReleaseVersionEnvelope,
                cancellationToken).ConfigureAwait(false);
            if (!version.IsAllowed) return Denied(version.Message, version.Detail, version.Status);
            var sourceRevision = version.Value?.Data?.SourceRevision?.Trim();
            if (sourceRevision is null || !FullRevision().IsMatch(sourceRevision))
            {
                return Denied("The running server did not expose a full source revision.",
                    "GET /api/v1/admin/version data.sourceRevision must be a lowercase 40-character Git SHA.");
            }

            ReleaseStudioItem? item = null;
            string? cursor = null;
            for (var page = 0; page < 1000 && item is null; page++)
            {
                var path = $"api/v1/studio/content-items?family={Uri.EscapeDataString(request.Family)}&limit=100";
                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    path += $"&cursor={Uri.EscapeDataString(cursor)}";
                }

                var items = await GetAsync(
                    profile,
                    path,
                    ReleaseWitnessJsonContext.Default.ReleaseStudioItemsResponse,
                    cancellationToken).ConfigureAwait(false);
                if (!items.IsAllowed) return Denied(items.Message, items.Detail, items.Status);
                item = items.Value?.Items.SingleOrDefault(candidate =>
                    string.Equals(candidate.ItemId, request.ItemId, StringComparison.Ordinal));
                cursor = items.Value?.NextCursor?.Trim();
                if (string.IsNullOrWhiteSpace(cursor)) break;
            }
            if (item is null)
            {
                return Denied($"Studio {request.Family} item {request.ItemId} was not found on the candidate.");
            }
            if (!string.Equals(item.PublishedVersionId, request.VersionId, StringComparison.Ordinal))
            {
                return Denied($"Studio {request.Family} item {request.ItemId} is not on the expected immutable version.");
            }
            var publicationId = item.Publication?.PublicationId?.Trim();
            if (string.IsNullOrWhiteSpace(publicationId))
            {
                return Denied($"Studio {request.Family} item {request.ItemId} has no active publication.");
            }

            var publication = await GetAsync(
                profile,
                $"api/v1/console/publications/{Uri.EscapeDataString(publicationId)}",
                ReleaseWitnessJsonContext.Default.ReleasePublicationDetail,
                cancellationToken).ConfigureAwait(false);
            if (!publication.IsAllowed) return Denied(publication.Message, publication.Detail, publication.Status);
            var route = publication.Value?.Route;
            if (route is null || !string.Equals(route.PublicationId, publicationId, StringComparison.Ordinal))
            {
                return Denied("The publication detail did not preserve its registry identity.");
            }
            var active = publication.Value!.Versions.SingleOrDefault(versionRow =>
                string.Equals(versionRow.VersionId, route.ActiveVersionId, StringComparison.Ordinal));
            if (active is null
                || !string.Equals(active.SourceContentId, request.ItemId, StringComparison.Ordinal)
                || !string.Equals(active.ContentVersionId, request.VersionId, StringComparison.Ordinal)
                || !string.Equals(active.ContentHash, request.ContentHash, StringComparison.Ordinal))
            {
                return Denied("The active publication version does not match the exact Studio item/version/content hash.");
            }

            var query = "api/v1/admin/observability/audit?resourceType=operation_proposal"
                + $"&resourceId={Uri.EscapeDataString(request.ProposalId)}&action=operation.applied&pageSize=25";
            var audit = await GetAsync(
                profile,
                query,
                OperateObservabilityJsonContext.Default.ObservabilityAuditPageResponse,
                cancellationToken).ConfigureAwait(false);
            if (!audit.IsAllowed) return Denied(audit.Message, audit.Detail, audit.Status);
            var auditRow = audit.Value?.Items.SingleOrDefault(row =>
                string.Equals(row.ResourceId, request.ProposalId, StringComparison.Ordinal)
                && string.Equals(row.Action, "operation.applied", StringComparison.Ordinal)
                && string.Equals(row.Outcome, "success", StringComparison.OrdinalIgnoreCase));
            if (auditRow is null)
            {
                return Denied($"The audit trail omits successful approval evidence for proposal {request.ProposalId}.");
            }
            var auditOperationId = ReadAuditOperationId(auditRow.Details);
            if (auditOperationId is null)
            {
                return Denied("The approval audit evidence omits executionOperationId.",
                    "items[].details must be structured JSON containing executionOperationId; the Console will not copy it from the proposal response.");
            }

            var integrity = await GetAsync(
                profile,
                "api/v1/admin/observability/audit/verify",
                ReleaseWitnessJsonContext.Default.ReleaseAuditIntegrity,
                cancellationToken).ConfigureAwait(false);
            if (!integrity.IsAllowed) return Denied(integrity.Message, integrity.Detail, integrity.Status);
            if (integrity.Value?.Verified is not true)
            {
                return Denied("The candidate audit chain did not verify.");
            }

            var routePath = route.RoutePath?.Trim();
            if (string.IsNullOrWhiteSpace(routePath))
            {
                return Denied("The active publication does not expose a public route.");
            }
            var publicUrl = ConsoleServerHttp.BuildUri(profile.ServerBaseUri, routePath.TrimStart('/')).AbsoluteUri;
            return OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(new(
                sourceRevision,
                request.Family,
                item.ItemId!,
                item.PublishedVersionId!,
                active.ContentHash!,
                auditRow.ResourceId!,
                publicationId,
                publicUrl,
                Required(auditRow.CorrelationId, "audit correlationId"),
                auditOperationId,
                AuditVerified: true));
        }
        catch (InvalidOperationException ex)
        {
            return Denied("Candidate evidence was ambiguous rather than uniquely bound.", ex.Message);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            return Denied("The candidate release evidence could not be read.", ex.Message);
        }
    }

    private async Task<OperateSectionResult<T>> GetAsync<T>(
        Models.ConsoleEnvironmentProfile profile,
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleServerHttp.BuildUri(profile.ServerBaseUri, path));
        await ConsoleServerHttp.AttachAuthenticationAsync(
            request, _sessions, profile, _adminApiKey, cancellationToken).ConfigureAwait(false);
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await ConsoleServerHttp.ReadProblemAsync(
                response, $"The release witness read returned HTTP {(int)response.StatusCode}.", cancellationToken)
                .ConfigureAwait(false);
            return OperateSectionResult<T>.Denied(MapStatus(response.StatusCode), problem.Message, problem.Detail);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
        return value is null
            ? OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, "The release witness read returned an empty response.")
            : OperateSectionResult<T>.Allowed(value);
    }

    private static string? ReadAuditOperationId(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return null;
        try
        {
            using var document = JsonDocument.Parse(details);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("executionOperationId", out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() is { Length: > 0 } operationId ? operationId : null
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFamily(string value) => value is "map" or "app" or "dashboard";
    private static bool HasBlank(params string[] values) => values.Any(string.IsNullOrWhiteSpace);
    private static string Required(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new InvalidOperationException($"Missing {label}.");
    private static OperateSectionResult<ConsoleReleaseWitnessEvidence> Denied(
        string message,
        string? detail = null,
        OperateSectionStatus status = OperateSectionStatus.Unavailable) =>
        OperateSectionResult<ConsoleReleaseWitnessEvidence>.Denied(status, message, detail);
    private static OperateSectionStatus MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable,
    };

    [System.Text.RegularExpressions.GeneratedRegex("^[0-9a-f]{40}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex FullRevision();
}

public sealed record ReleaseVersionEnvelope { public ReleaseVersionData? Data { get; init; } }
public sealed record ReleaseVersionData { public string? SourceRevision { get; init; } }
public sealed record ReleaseStudioItemsResponse
{
    public IReadOnlyList<ReleaseStudioItem> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}
public sealed record ReleaseStudioItem
{
    public string? ItemId { get; init; }
    public string? PublishedVersionId { get; init; }
    public ReleaseStudioPublicationBadge? Publication { get; init; }
}
public sealed record ReleaseStudioPublicationBadge { public string? PublicationId { get; init; } }
public sealed record ReleasePublicationDetail
{
    public ReleasePublicationRoute? Route { get; init; }
    public IReadOnlyList<ReleasePublicationVersion> Versions { get; init; } = [];
}
public sealed record ReleasePublicationRoute
{
    public string? PublicationId { get; init; }
    public string? ActiveVersionId { get; init; }
    public string? RoutePath { get; init; }
}
public sealed record ReleasePublicationVersion
{
    public string? VersionId { get; init; }
    public string? SourceContentId { get; init; }
    public string? ContentVersionId { get; init; }
    public string? ContentHash { get; init; }
}
public sealed record ReleaseAuditIntegrity { public bool Verified { get; init; } }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseVersionEnvelope))]
[JsonSerializable(typeof(ReleaseStudioItemsResponse))]
[JsonSerializable(typeof(ReleasePublicationDetail))]
[JsonSerializable(typeof(ReleaseAuditIntegrity))]
internal partial class ReleaseWitnessJsonContext : JsonSerializerContext;
