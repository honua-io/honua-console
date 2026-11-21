using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A 403 from the server approval gate is Forbidden and is not bypassed.
public sealed partial class HttpConsoleDeployApprovalClient : IConsoleDeployApprovalClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to review deploy approvals.";

    private const string HumanApprovalRefusedMessage =
        "The server refused this deploy approval. Submitting or promoting a deploy requires the 'approve' grant and a "
        + "live signed-in browser session on the Honua API's own origin; API keys, MCP tokens and cross-origin bearer access cannot approve.";

    private readonly HttpClient _http;
    private readonly IConsoleAuthorityBindingValidator? _authority;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly string? _adminApiKey;
    private readonly IConsoleOperatorBearerProvider _operatorBearerProvider;
    private readonly ConsoleServerCredentialMode _credentialMode;

    public HttpConsoleDeployApprovalClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore,
        string? adminApiKey = null,
        IConsoleOperatorBearerProvider? operatorBearerProvider = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive,
        IConsoleAuthorityBindingValidator? authority = null)
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
        _authority = authority;
    }

    public async Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        return await SendOperationAsync(
            profile,
            HttpMethod.Get,
            DeployControlAdminRoutes.Operation(operationId.Trim()),
            content: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
        IReadOnlyList<string> operationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationIds);

        var ids = operationIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed([]);
        }

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        var reads = await BoundedFanOut
            .RunAsync(ids, GetOperationAsync, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var pending = reads
            .Where(r => r.IsAllowed && r.Value is not null && !r.Value.IsTerminal)
            .Select(r => r.Value!)
            .OrderByDescending(p => p.IsAwaitingApproval)
            .ThenByDescending(p => p.UpdatedAt)
            .ToArray();

        var anyFailed = reads.Any(r => !r.IsAllowed);
        return OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed(
            pending,
            partialResult: anyFailed,
            message: anyFailed ? "One or more deploy operations could not be read and were omitted." : string.Empty);
    }

    public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        SubmitAsync(operationId, reason, reviewed: null, cancellationToken);

    public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        ConsoleDeployReviewedOperation? reviewed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var id = operationId.Trim();
        var body = JsonSerializer.Serialize(
            new SubmitDeployOperationRequest { Reason = NormalizeReason(reason) },
            DeployControlJsonContext.Default.SubmitDeployOperationRequest);
        return ApproveDeployActionAsync(
            new DeployApprovalAction(id, "submit", DeployControlAdminRoutes.Submit(id), body, reviewed),
            cancellationToken);
    }

    public Task<OperateSectionResult<DeployOperationProposal>> PromoteAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        PromoteAsync(operationId, reason, reviewed: null, cancellationToken);

    public Task<OperateSectionResult<DeployOperationProposal>> PromoteAsync(
        string operationId,
        string? reason,
        ConsoleDeployReviewedOperation? reviewed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var id = operationId.Trim();
        var body = JsonSerializer.Serialize(
            new PromoteDeployOperationRequest { Reason = NormalizeReason(reason) },
            DeployControlJsonContext.Default.PromoteDeployOperationRequest);
        return ApproveDeployActionAsync(
            new DeployApprovalAction(id, "promote", DeployControlAdminRoutes.Promote(id), body, reviewed),
            cancellationToken);
    }

    public async Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var body = JsonSerializer.Serialize(
            new RollbackDeployOperationRequest { Reason = NormalizeReason(reason) },
            DeployControlJsonContext.Default.RollbackDeployOperationRequest);

        return await SendOperationAsync(
            profile,
            HttpMethod.Post,
            DeployControlAdminRoutes.Rollback(operationId.Trim()),
            body,
            cancellationToken).ConfigureAwait(false);
    }

    // The server requires a live human browser session (no bearer or API key) and a review capability
    // bound to the current deploy operation, so the capability is opened immediately before submit/promote.
    private async Task<OperateSectionResult<DeployOperationProposal>> ApproveDeployActionAsync(
        DeployApprovalAction action,
        CancellationToken cancellationToken)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var reviewRoute = DeployControlAdminRoutes.ApprovalReview(action.OperationId, action.ActionName);
        var review = await SendHumanApprovalAsync(
            profile,
            new HumanApprovalCall(HttpMethod.Get, reviewRoute, Content: null, Capability: null),
            DeployControlJsonContext.Default.DeployHumanApprovalReviewResponse,
            cancellationToken).ConfigureAwait(false);

        if (review.Status == OperateSectionStatus.Missing)
        {
            // Older servers without the approval-review route answer 404; fall back to the plain POST.
            return await SendOperationAsync(
                profile,
                HttpMethod.Post,
                action.TargetRoute,
                action.RequestBody,
                cancellationToken).ConfigureAwait(false);
        }

        if (!review.IsAllowed)
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(review.Status, review.Message, review.Detail);
        }

        var refusal = CheckReviewGrant(review.Value, action);
        if (refusal is not null)
        {
            return refusal;
        }

        var result = await SendHumanApprovalAsync(
            profile,
            new HumanApprovalCall(HttpMethod.Post, action.TargetRoute, action.RequestBody, review.Value!.Review!.Capability),
            MetadataReleaseJsonContext.Default.DeployOperationResponse,
            cancellationToken).ConfigureAwait(false);

        return ToProposalResult(result);
    }

    private static OperateSectionResult<DeployOperationProposal>? CheckReviewGrant(
        DeployHumanApprovalReviewResponse? review,
        DeployApprovalAction action)
    {
        var grant = review?.Review;
        var actionMismatch = !string.IsNullOrWhiteSpace(review?.Action)
            && !string.Equals(review.Action, action.ActionName, StringComparison.OrdinalIgnoreCase);
        if (grant is null
            || string.IsNullOrWhiteSpace(grant.Capability)
            || !string.Equals(grant.ProposalId, action.OperationId, StringComparison.Ordinal)
            || actionMismatch)
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable,
                "The server did not open a human approval review for this deploy operation.");
        }

        var reviewed = action.Reviewed;
        if (reviewed is null)
        {
            return null;
        }

        var versionChanged = reviewed.Version > 0 && grant.ProposalVersion != reviewed.Version;
        var hashChanged = !string.IsNullOrWhiteSpace(reviewed.PlanHash)
            && !string.Equals(grant.PlanHash, reviewed.PlanHash, StringComparison.Ordinal);
        var currentRevision = review?.Operation?.MetadataRelease?.DesiredRevision
            ?? review?.Operation?.Target?.DesiredRevision;
        var revisionChanged = !string.IsNullOrWhiteSpace(reviewed.DesiredRevision)
            && !string.IsNullOrWhiteSpace(currentRevision)
            && !string.Equals(currentRevision, reviewed.DesiredRevision, StringComparison.Ordinal);

        return versionChanged || hashChanged || revisionChanged
            ? OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable,
                "The deploy operation changed after you opened it. Review the current plan before approving.")
            : null;
    }

    private async Task<OperateSectionResult<DeployOperationProposal>> SendOperationAsync(
        ConsoleEnvironmentProfile profile,
        HttpMethod method,
        string relativePath,
        string? content,
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
                return OperateSectionResult<DeployOperationProposal>.Denied(
                    OperateSectionStatus.Forbidden,
                    authentication.Message);
            }
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        var result = await ExchangeAsync(
            request,
            MetadataReleaseJsonContext.Default.DeployOperationResponse,
            code => MapErrorMessage(code, method, relativePath),
            cancellationToken).ConfigureAwait(false);
        return ToProposalResult(result);
    }

    private async Task<OperateSectionResult<T>> SendHumanApprovalAsync<T>(
        ConsoleEnvironmentProfile profile,
        HumanApprovalCall call,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(call.Method, BuildUri(profile.ServerBaseUri, call.RelativePath));
        if (!string.IsNullOrWhiteSpace(call.Capability))
        {
            request.Headers.TryAddWithoutValidation(ProposalAdminRoutes.ApprovalCapabilityHeader, call.Capability);
        }

        if (call.Content is not null)
        {
            request.Content = new StringContent(call.Content, Encoding.UTF8, "application/json");
        }

        return await ExchangeAsync(
            request,
            typeInfo,
            code => code is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                ? HumanApprovalRefusedMessage
                : MapErrorMessage(code, call.Method, call.RelativePath),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> ExchangeAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
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
                    "The honua-server deploy-control API returned an empty response.")
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
                "The honua-server deploy-control API is unreachable or returned an unreadable response.");
        }
    }

    private static OperateSectionResult<DeployOperationProposal> ToProposalResult(
        OperateSectionResult<DeployOperationResponse> result) =>
        result.IsAllowed && result.Value is not null
            ? OperateSectionResult<DeployOperationProposal>.Allowed(DeployApprovalMapper.MapProposal(result.Value))
            : OperateSectionResult<DeployOperationProposal>.Denied(
                result.Status,
                string.IsNullOrWhiteSpace(result.Message)
                    ? "The honua-server deploy-control API returned an empty response."
                    : result.Message,
                result.Detail);

    private static string MapErrorMessage(HttpStatusCode code, HttpMethod method, string relativePath) => code switch
    {
        HttpStatusCode.Forbidden =>
            "The server's approval gate denied this action. A data-affecting rollback requires explicit approval; "
            + "this credential is not permitted to approve it.",
        HttpStatusCode.Conflict =>
            "The deploy operation is not in a state that allows this action (it may already be submitted, rolled back, or terminal).",
        HttpStatusCode.NotFound =>
            "The deploy-control operation was not found on the connected server.",
        HttpStatusCode.NotImplemented =>
            "The connected server does not expose the deploy-control approval endpoints.",
        _ => $"The honua-server deploy-control API returned {(int)code} for {method.Method} {relativePath}.",
    };

    private static string? NormalizeReason(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable,
    };

    private sealed record DeployApprovalAction(
        string OperationId,
        string ActionName,
        string TargetRoute,
        string RequestBody,
        ConsoleDeployReviewedOperation? Reviewed);

    private sealed record HumanApprovalCall(
        HttpMethod Method,
        string RelativePath,
        string? Content,
        string? Capability);
}
