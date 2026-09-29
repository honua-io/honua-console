using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the approval surface to a real honua-server through the console approval REST API
/// (honua-server #1694): <c>GET /api/v1/admin/proposals</c>, <c>GET .../{id}</c>,
/// <c>POST .../{id}/approve</c>, <c>POST .../{id}/reject</c>. The active operator bearer
/// is required so server audit records retain the human identity. A configured admin API
/// key is available only in explicit, sessionless headless/service mode. The server's RBAC <c>approve</c> grant and
/// separation-of-duties rule remain the real gate: a denied approve/reject returns 403, surfaced here as a
/// <see cref="OperateSectionStatus.Forbidden"/> result — the UI never bypasses it.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this client
/// never returns seeded data. When no environment profile is bound, every call returns a
/// missing-binding result.
/// </summary>
public sealed class HttpConsoleProposalsClient : IConsoleProposalsClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to review approvals.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly string? _adminApiKey;
    private readonly IConsoleOperatorBearerProvider _operatorBearerProvider;
    private readonly ConsoleServerCredentialMode _credentialMode;

    public HttpConsoleProposalsClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore,
        string? adminApiKey = null,
        IConsoleOperatorBearerProvider? operatorBearerProvider = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
        _operatorBearerProvider = operatorBearerProvider
            ?? new ConsoleOperatorBearerProvider(
                sessionStore,
                new UnavailableConsoleOperatorBearerExchange(),
                TimeProvider.System);
        _credentialMode = credentialMode;
    }

    public async Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        string? requestedBy = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        var query = BuildListQuery(status, kind, requestedBy);

        var result = await SendAsync(
            profile,
            HttpMethod.Get,
            ProposalAdminRoutes.List + query,
            content: null,
            ProposalJsonContext.Default.ProposalListWire,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsAllowed)
        {
            return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(result.Status, result.Message, result.Detail);
        }

        var summaries = (result.Value?.Proposals ?? [])
            .Select(MapSummary)
            .ToArray();

        return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(summaries);
    }

    public async Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        return await SendDetailAsync(HttpMethod.Get, ProposalAdminRoutes.Detail(proposalId.Trim()), content: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        return await SendDetailAsync(
            HttpMethod.Post,
            ProposalAdminRoutes.Approve(proposalId.Trim()),
            content: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            // The server requires a reason (400); fail closed before sending.
            return OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Unavailable,
                "A rejection reason is required.");
        }

        var body = JsonSerializer.Serialize(
            new RejectProposalWire { Reason = reason.Trim() },
            ProposalJsonContext.Default.RejectProposalWire);

        return await SendDetailAsync(
            HttpMethod.Post,
            ProposalAdminRoutes.Reject(proposalId.Trim()),
            body,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<ConsoleProposalDetail>> SendDetailAsync(
        HttpMethod method,
        string relativeSuffix,
        string? content,
        CancellationToken cancellationToken)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var result = await SendAsync(
            profile,
            method,
            relativeSuffix,
            content,
            ProposalJsonContext.Default.ProposalDetailWire,
            cancellationToken).ConfigureAwait(false);

        return result.IsAllowed && result.Value is not null
            ? OperateSectionResult<ConsoleProposalDetail>.Allowed(MapDetail(result.Value))
            : OperateSectionResult<ConsoleProposalDetail>.Denied(
                result.Status,
                string.IsNullOrWhiteSpace(result.Message)
                    ? "The honua-server proposals API returned an empty response."
                    : result.Message,
                result.Detail);
    }

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        ConsoleEnvironmentProfile profile,
        HttpMethod method,
        string relativePath,
        string? content,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(profile.ServerBaseUri, relativePath));
        if (method == HttpMethod.Get)
        {
            await ConsoleServerHttp.AttachAuthenticationAsync(
                request,
                _sessionStore,
                profile,
                _adminApiKey,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(
                request,
                _operatorBearerProvider,
                profile,
                _adminApiKey,
                _credentialMode,
                cancellationToken).ConfigureAwait(false);
            if (!authentication.IsAuthenticated)
            {
                return OperateSectionResult<T>.Denied(
                    OperateSectionStatus.Forbidden,
                    authentication.Message);
            }
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var problem = await ConsoleServerHttp.ReadProblemAsync(
                    response,
                    MapErrorMessage(response.StatusCode, method, relativePath),
                    cancellationToken).ConfigureAwait(false);
                return OperateSectionResult<T>.Denied(
                    MapStatus(response.StatusCode),
                    problem.Message,
                    problem.Detail);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<T>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server proposals API returned an empty response.")
                : OperateSectionResult<T>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server proposals API is unreachable or returned an unreadable response.");
        }
    }

    internal static string BuildListQuery(string? status, string? kind, string? requestedBy)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add("status=" + Uri.EscapeDataString(status.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            parts.Add("kind=" + Uri.EscapeDataString(kind.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(requestedBy))
        {
            parts.Add("requestedBy=" + Uri.EscapeDataString(requestedBy.Trim()));
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    internal static ConsoleProposalSummary MapSummary(ProposalSummaryWire wire) => new(
        ProposalId: wire.ProposalId ?? string.Empty,
        Kind: ConsoleProposalPresentation.MapKind(wire.Kind),
        Status: ConsoleProposalPresentation.MapStatus(wire.Status),
        RequestedBy: wire.RequestedBy,
        RequestedByAgent: wire.RequestedByAgent,
        Summary: wire.Summary ?? string.Empty,
        RiskLevel: ConsoleProposalPresentation.MapRisk(wire.RiskLevel),
        CreatedAt: wire.CreatedAt,
        UpdatedAt: wire.UpdatedAt)
    {
        FindingId = wire.FindingId,
        AutonomyRule = wire.AutonomyRule,
        ActionDiscriminator = wire.ActionDiscriminator,
    };

    internal static ConsoleProposalDetail MapDetail(ProposalDetailWire wire) => new(
        ProposalId: wire.ProposalId ?? string.Empty,
        Kind: ConsoleProposalPresentation.MapKind(wire.Kind),
        Status: ConsoleProposalPresentation.MapStatus(wire.Status),
        RequestedBy: wire.RequestedBy,
        RequestedByAgent: wire.RequestedByAgent,
        Summary: wire.Summary ?? string.Empty,
        Diff: wire.Diff ?? [],
        DryRun: wire.DryRun ?? [],
        RiskLevel: ConsoleProposalPresentation.MapRisk(wire.RiskLevel),
        BlockingReasons: wire.BlockingReasons ?? [],
        Warnings: wire.Warnings ?? [],
        GuardrailTier: wire.GuardrailTier,
        ResolvedBy: wire.ResolvedBy,
        ResolutionReason: wire.ResolutionReason,
        ExecutionOperationId: wire.ExecutionOperationId,
        CreatedAt: wire.CreatedAt,
        UpdatedAt: wire.UpdatedAt,
        ResolvedAt: wire.ResolvedAt)
    {
        FindingId = wire.FindingId,
        AutonomyRule = wire.AutonomyRule,
        ActionDiscriminator = wire.ActionDiscriminator,
    };

    private static string MapErrorMessage(HttpStatusCode code, HttpMethod method, string relativePath) => code switch
    {
        HttpStatusCode.Forbidden =>
            "The server's approval gate denied this action. Approving or rejecting a proposal requires the "
            + "'approve' permission, and a proposal's requester cannot approve their own proposal.",
        HttpStatusCode.Conflict =>
            "The proposal is not in a state that allows this action (it may already be approved, rejected, or terminal).",
        HttpStatusCode.BadRequest =>
            "The request was rejected by the server (a rejection reason is required).",
        HttpStatusCode.NotFound =>
            "The proposal was not found on the connected server.",
        HttpStatusCode.NotImplemented =>
            "The connected server does not expose the proposals approval endpoints.",
        _ => $"The honua-server proposals API returned {(int)code} for {method.Method} {relativePath}.",
    };

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable,
    };
}

/// <summary>Wire shape of the server proposal summary (honua-server #1694).</summary>
public sealed record ProposalSummaryWire
{
    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? RequestedBy { get; init; }
    public string? RequestedByAgent { get; init; }
    public string? Summary { get; init; }
    public string? RiskLevel { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? FindingId { get; init; }
    public string? AutonomyRule { get; init; }
    public string? ActionDiscriminator { get; init; }
}

/// <summary>Wire shape of the server proposal detail (honua-server #1694).</summary>
public sealed record ProposalDetailWire
{
    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? RequestedBy { get; init; }
    public string? RequestedByAgent { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string>? Diff { get; init; }
    public IReadOnlyList<string>? DryRun { get; init; }
    public string? RiskLevel { get; init; }
    public IReadOnlyList<string>? BlockingReasons { get; init; }
    public IReadOnlyList<string>? Warnings { get; init; }
    public string? GuardrailTier { get; init; }
    public string? ResolvedBy { get; init; }
    public string? ResolutionReason { get; init; }
    public string? ExecutionOperationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? FindingId { get; init; }
    public string? AutonomyRule { get; init; }
    public string? ActionDiscriminator { get; init; }
}

/// <summary>Wire wrapper for the proposal list response.</summary>
public sealed record ProposalListWire
{
    public IReadOnlyList<ProposalSummaryWire>? Proposals { get; init; }
}

/// <summary>Wire body for the reject request — reason is required.</summary>
public sealed record RejectProposalWire
{
    public string? Reason { get; init; }
}

/// <summary>Source-generated context for the proposals admin DTOs (camelCase wire).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProposalSummaryWire))]
[JsonSerializable(typeof(ProposalDetailWire))]
[JsonSerializable(typeof(ProposalListWire))]
[JsonSerializable(typeof(RejectProposalWire))]
internal sealed partial class ProposalJsonContext : JsonSerializerContext;
