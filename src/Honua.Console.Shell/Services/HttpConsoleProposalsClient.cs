using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Human decisions never use the forwardable credentials used for reads and rejection.
public sealed class HttpConsoleProposalsClient : IConsoleProposalsClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to review approvals.";

    private readonly IConsoleHumanProposalSession? _humanSession;
    private readonly ConsoleServerAuthority _authority;
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
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive,
        IConsoleHumanProposalSession? humanSession = null,
        ConsoleServerAuthority authority = ConsoleServerAuthority.Serving)
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
        _humanSession = humanSession;
        _authority = authority;
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
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        OperateSectionResult<ConsoleHumanProposalReview>? review = null;
        if (_humanSession is not null && profile is not null)
        {
            review = await _humanSession.OpenReviewAsync(
                new ConsoleHumanProposalReviewRequest(profile, _authority, proposalId.Trim()), cancellationToken)
                .ConfigureAwait(false);
            if (review.IsAllowed && review.Value is { } opened)
                return OperateSectionResult<ConsoleProposalDetail>.Allowed(MapDetail(opened.Proposal) with
                {
                    HumanReviewEnvironment = opened.EnvironmentReference,
                    HumanReviewExpiresAt = opened.ExpiresAt,
                    HumanApprovalMessage = opened.ApprovalMessage,
                    HumanApprovalSignInPath = opened.Proposal.RequiresReproposal ? null : BuildSignInPath(profile, proposalId.Trim()),
                });
        }
        var result = await SendDetailAsync(HttpMethod.Get, ProposalAdminRoutes.Detail(proposalId.Trim()), content: null, cancellationToken)
            .ConfigureAwait(false);
        return result.Value is { IsAwaitingApproval: true } detail
            ? result with { Value = detail with { HumanApprovalMessage = detail.HumanApprovalMessage
                ?? review?.Message ?? "Sign in to this server, then refresh this review to approve.",
                HumanApprovalSignInPath = !detail.RequiresReproposal && profile is not null
                    && (review is null || review.Status == OperateSectionStatus.Forbidden)
                    ? BuildSignInPath(profile, proposalId.Trim()) : null } }
            : result;
    }

    public async Task<OperateSectionResult<ConsoleProposalDetail>> GetStatusAsync(
        string proposalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (_humanSession is not null && profile is not null)
        {
            var status = await _humanSession.ReadStatusAsync(
                new ConsoleHumanProposalReviewRequest(profile, _authority, proposalId.Trim()), cancellationToken).ConfigureAwait(false);
            if (status.IsAllowed && status.Value is { } proposal)
                return OperateSectionResult<ConsoleProposalDetail>.Allowed(MapDetail(proposal).AsStatusOnly());
        }
        var result = await SendDetailAsync(HttpMethod.Get, ProposalAdminRoutes.Detail(proposalId.Trim()), null, cancellationToken).ConfigureAwait(false);
        return result.Value is { } detail ? result with { Value = detail.AsStatusOnly() } : result;
    }

    public async Task<OperateSectionResult<ConsoleProposalDetail>> ApproveReviewedAsync(
        ConsoleProposalDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision.ProposalId);
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        if (_humanSession is null)
            return OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Forbidden,
                "Sign in to the server and reopen this review to approve.");
        var result = await _humanSession.ApproveAsync(
            new ConsoleHumanProposalApprovalRequest(profile, _authority, decision), cancellationToken).ConfigureAwait(false);
        return result.IsAllowed && result.Value is { } proposal
            ? OperateSectionResult<ConsoleProposalDetail>.Allowed(MapDetail(proposal))
            : OperateSectionResult<ConsoleProposalDetail>.Denied(result.Status, result.Message, result.Detail);
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
        Version = wire.Version,
        PlanHash = wire.PlanHash,
        RequiresReproposal = wire.RequiresReproposal,
        HumanApprovalMessage = wire.ApprovalRemediation,
        FindingId = wire.FindingId,
        AutonomyRule = wire.AutonomyRule,
        ActionDiscriminator = wire.ActionDiscriminator,
    };

    private static string MapErrorMessage(HttpStatusCode code, HttpMethod method, string relativePath) => code switch
    {
        HttpStatusCode.Forbidden when method == HttpMethod.Post =>
            "The server denied this request. Rejection requires its own current 'approve' permission and bearer scope.",
        HttpStatusCode.Forbidden =>
            "The server denied this read. Check the current proposal read grant and scope.",
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

    private string BuildSignInPath(ConsoleEnvironmentProfile profile, string proposalId)
    {
        var ownerId = _authority == ConsoleServerAuthority.Management ? ConsoleProposalIdentity.ManagementPrefix + proposalId : proposalId;
        var returnTo = "/inbox?proposalId=" + Uri.EscapeDataString(ownerId);
        return "/auth/server/login?profileId=" + Uri.EscapeDataString(profile.Id)
            + "&authority=" + _authority + "&returnTo=" + Uri.EscapeDataString(returnTo);
    }

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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProposalSummaryWire))]
[JsonSerializable(typeof(ProposalDetailWire))]
[JsonSerializable(typeof(ProposalListWire))]
[JsonSerializable(typeof(RejectProposalWire))]
internal sealed partial class ProposalJsonContext : JsonSerializerContext;
