using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the human-in-the-loop deploy approval surface to a real honua-server through
/// the deploy-control admin endpoints (<c>GET /api/v1/admin/deploy/operations/{id}</c>,
/// <c>POST .../submit</c>, <c>POST .../rollback</c>). The active operator bearer is
/// required so server audit records retain the human identity; a configured admin API
/// key is available only in explicit, sessionless headless/service mode. The server's <c>OperatorApprovalGate</c> remains
/// the real gate: a data-affecting rollback that the gate denies returns 403, which this client surfaces
/// as a <see cref="OperateSectionStatus.Forbidden"/> result — the UI never bypasses it.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this client
/// never returns seeded data. When no environment profile is bound, every call returns a
/// missing-binding result.
/// </summary>
public sealed class HttpConsoleDeployApprovalClient : IConsoleDeployApprovalClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to review deploy approvals.";

    private readonly HttpClient _http;
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

        return await SendAsync(
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
            // Nothing to project; an empty allowed result is honest (no operations in view).
            return OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed([]);
        }

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        // Bound the per-operation detail fan-out: the id set is server-driven (no upstream clamp),
        // so an unbounded Task.WhenAll would open one admin socket per operation at once — the
        // connection-exhaustion cliff BoundedFanOut exists to prevent.
        var reads = await BoundedFanOut
            .RunAsync(ids, GetOperationAsync, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Independent reads: keep the ones that resolved and are still pending/actionable.
        // A failed individual read is skipped (partial result) rather than failing the list.
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

    public async Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
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
            new SubmitDeployOperationRequest { Reason = NormalizeReason(reason) },
            DeployControlJsonContext.Default.SubmitDeployOperationRequest);

        return await SendAsync(
            profile,
            HttpMethod.Post,
            DeployControlAdminRoutes.Submit(operationId.Trim()),
            body,
            cancellationToken).ConfigureAwait(false);
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

        // A data-affecting rollback the server gate denies returns 403 -> Forbidden here.
        // The UI must require explicit confirmation before calling this; the gate is the
        // authority and is never bypassed.
        return await SendAsync(
            profile,
            HttpMethod.Post,
            DeployControlAdminRoutes.Rollback(operationId.Trim()),
            body,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<DeployOperationProposal>> SendAsync(
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

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<DeployOperationProposal>.Denied(
                    MapStatus(response.StatusCode),
                    MapErrorMessage(response.StatusCode, method, relativePath));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                MetadataReleaseJsonContext.Default.DeployOperationResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<DeployOperationProposal>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server deploy-control API returned an empty response.")
                : OperateSectionResult<DeployOperationProposal>.Allowed(DeployApprovalMapper.MapProposal(value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server deploy-control API is unreachable or returned an unreadable response.");
        }
    }

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
}
