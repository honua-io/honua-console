using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A 403 from the server approve grant or human-session gate is Forbidden and is not bypassed.
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

    public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        CancellationToken cancellationToken = default) =>
        ApproveAsync(proposalId, reviewed: null, cancellationToken);

    // The server requires a live human browser session (no bearer or API key) and a review capability
    // bound to the current plan, so the capability is opened immediately before the decision and never cached.
    public async Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        ConsoleProposalReviewedPlan? reviewed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var id = proposalId.Trim();
        var review = await SendHumanApprovalAsync(
            profile, HttpMethod.Get, ProposalAdminRoutes.ApprovalReview(id),
            ProposalJsonContext.Default.ProposalHumanReviewWire, capability: null, cancellationToken).ConfigureAwait(false);
        if (!review.IsAllowed)
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(review.Status, review.Message, review.Detail);
        }

        var grant = review.Value?.Review;
        var refusal = CheckGrant(grant, id, reviewed);
        if (refusal is not null)
        {
            return refusal;
        }

        var result = await SendHumanApprovalAsync(
            profile, HttpMethod.Post, ProposalAdminRoutes.Approve(id),
            ProposalJsonContext.Default.ProposalDetailWire, grant!.Capability, cancellationToken).ConfigureAwait(false);
        return ToDetailResult(result);
    }

    private static OperateSectionResult<ConsoleProposalDetail>? CheckGrant(
        ProposalHumanReviewGrantWire? grant, string proposalId, ConsoleProposalReviewedPlan? reviewed)
    {
        if (grant is null || string.IsNullOrWhiteSpace(grant.Capability)
            || !string.Equals(grant.ProposalId, proposalId, StringComparison.Ordinal))
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Unavailable,
                "The server did not open a human approval review for this proposal.");
        }

        var changed = reviewed is not null
            && (grant.ProposalVersion != reviewed.Version
                || (reviewed.PlanHash is not null && !string.Equals(grant.PlanHash, reviewed.PlanHash, StringComparison.Ordinal)));
        return changed
            ? OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Unavailable,
                "The proposal changed after you opened it. Review the current plan before approving.")
            : null;
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

        return ToDetailResult(result);
    }

    private static OperateSectionResult<ConsoleProposalDetail> ToDetailResult(OperateSectionResult<ProposalDetailWire> result) =>
        result.IsAllowed && result.Value is not null
            ? OperateSectionResult<ConsoleProposalDetail>.Allowed(MapDetail(result.Value))
            : OperateSectionResult<ConsoleProposalDetail>.Denied(
                result.Status,
                string.IsNullOrWhiteSpace(result.Message)
                    ? "The honua-server proposals API returned an empty response."
                    : result.Message,
                result.Detail);

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

        return await ExchangeAsync(request, typeInfo, code => MapErrorMessage(code, method, relativePath), cancellationToken)
            .ConfigureAwait(false);
    }

    // The server refuses any explicit credential on human approval; only the ambient browser session may decide.
    private async Task<OperateSectionResult<T>> SendHumanApprovalAsync<T>(
        ConsoleEnvironmentProfile profile,
        HttpMethod method,
        string relativePath,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string? capability,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(profile.ServerBaseUri, relativePath));
        if (capability is not null)
        {
            request.Headers.TryAddWithoutValidation(ProposalAdminRoutes.ApprovalCapabilityHeader, capability);
        }

        return await ExchangeAsync(
            request,
            typeInfo,
            code => code is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                ? HumanApprovalRefusedMessage
                : MapErrorMessage(code, method, relativePath),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> ExchangeAsync<T>(
        HttpRequestMessage request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Func<HttpStatusCode, string> fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var problem = await ConsoleServerHttp.ReadProblemAsync(
                    response,
                    fallbackMessage(response.StatusCode),
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
        Version = wire.Version,
        PlanHash = wire.PlanHash,
    };

    private static string MapErrorMessage(HttpStatusCode code, HttpMethod method, string relativePath) => code switch
    {
        HttpStatusCode.Forbidden =>
            "The server's approval gate denied this action. Approving or rejecting a proposal requires the "
            + "'approve' permission.",
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

    private const string HumanApprovalRefusedMessage =
        "The server refused this approval. Approving requires the 'approve' grant and a live signed-in browser "
        + "session on the Honua API's own origin; API keys, MCP tokens and cross-origin bearer access cannot approve.";

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

public sealed record ProposalDetailWire
{
    public string? ProposalId { get; init; }
    public long Version { get; init; }
    public string? PlanHash { get; init; }
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

public sealed record ProposalHumanReviewWire
{
    public ProposalDetailWire? Proposal { get; init; }
    public ProposalHumanReviewGrantWire? Review { get; init; }
}

// The capability is opaque to Console and lives only for the approve request that follows.
public sealed record ProposalHumanReviewGrantWire
{
    public string? ProposalId { get; init; }
    public long ProposalVersion { get; init; }
    public string? PlanHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string? Capability { get; init; }
}

public sealed record ProposalListWire
{
    public IReadOnlyList<ProposalSummaryWire>? Proposals { get; init; }
}

// The reject body must include a reason.
public sealed record RejectProposalWire
{
    public string? Reason { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProposalSummaryWire))]
[JsonSerializable(typeof(ProposalDetailWire))]
[JsonSerializable(typeof(ProposalListWire))]
[JsonSerializable(typeof(ProposalHumanReviewWire))]
[JsonSerializable(typeof(RejectProposalWire))]
internal sealed partial class ProposalJsonContext : JsonSerializerContext;
