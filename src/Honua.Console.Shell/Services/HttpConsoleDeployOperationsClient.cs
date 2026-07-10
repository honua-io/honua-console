using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds <see cref="IConsoleDeployOperationsClient"/> to a real honua-server through the
/// deploy-control admin endpoints. Mirrors <see cref="HttpConsoleDeployApprovalClient"/>'s
/// auth/error-mapping pattern; unlike that client, a 404 here means "this server build does
/// not register the route" (these are collection/gate-level GETs and a speculative POST, none
/// keyed by an id that could itself be "not found"), so it maps to
/// <see cref="OperateSectionStatus.Unsupported"/> — the feature-detection signal console#290's
/// acceptance criteria require for the list, preflight, and converge surfaces.
/// </summary>
public sealed class HttpConsoleDeployOperationsClient : IConsoleDeployOperationsClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to read deploy operations.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    public HttpConsoleDeployOperationsClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    public async Task<OperateSectionResult<DeployOperationListView>> ListAsync(
        DeployOperationListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<DeployOperationListView>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var route = DeployControlAdminRoutes.OperationsList(query.Status, query.Kind, query.Page, query.PageSize);

        return await SendAsync(
            profile.ServerBaseUri,
            route,
            DeployControlJsonContext.Default.DeployOperationListResponse,
            DeployOperationListMapper.Map,
            "The honua-server deploy-operations list API",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperateSectionResult<DeployPreflightView>> GetPreflightAsync(
        bool includeDiagnostics = true,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<DeployPreflightView>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        return await SendAsync(
            profile.ServerBaseUri,
            DeployControlAdminRoutes.PreflightWithDiagnostics(includeDiagnostics),
            DeployControlJsonContext.Default.DeployPreflightResponse,
            DeployPreflightMapper.Map,
            "The honua-server deploy preflight API",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperateSectionResult<PlatformReleaseConvergeView>> ConvergeAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<PlatformReleaseConvergeView>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, DeployControlAdminRoutes.PlatformReleaseConverge));
        AttachAuth(request);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<PlatformReleaseConvergeView>.Denied(
                    MapStatus(response.StatusCode),
                    MapErrorMessage(response.StatusCode, "The honua-server platform-release converge API"));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                DeployControlJsonContext.Default.PlatformReleaseConvergeResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<PlatformReleaseConvergeView>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server platform-release converge API returned an empty response.")
                : OperateSectionResult<PlatformReleaseConvergeView>.Allowed(PlatformReleaseConvergeMapper.Map(value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // A converge response shape mismatch (honua-server#2564 not yet reconciled) degrades
            // to unavailable rather than crashing the cockpit — never fake a converge outcome.
            return OperateSectionResult<PlatformReleaseConvergeView>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server platform-release converge API is unreachable or returned an unreadable response.");
        }
    }

    private async Task<OperateSectionResult<TView>> SendAsync<TWire, TView>(
        Uri baseUri,
        string relativePath,
        JsonTypeInfo<TWire> typeInfo,
        Func<TWire, TView> map,
        string apiLabel,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleServerHttp.BuildUri(baseUri, relativePath));
        AttachAuth(request);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<TView>.Denied(MapStatus(response.StatusCode), MapErrorMessage(response.StatusCode, apiLabel));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<TView>.Denied(OperateSectionStatus.Unavailable, $"{apiLabel} returned an empty response.")
                : OperateSectionResult<TView>.Allowed(map(value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return OperateSectionResult<TView>.Denied(
                OperateSectionStatus.Unavailable,
                $"{apiLabel} is unreachable or returned an unreadable response.");
        }
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }
    }

    private static string MapErrorMessage(HttpStatusCode code, string apiLabel) => code switch
    {
        HttpStatusCode.Forbidden =>
            "The server's approval gate denied this action.",
        HttpStatusCode.NotFound or HttpStatusCode.NotImplemented =>
            $"{apiLabel} is not available on the connected server (older server build, or the capability has not merged yet).",
        _ => $"{apiLabel} returned {(int)code}.",
    };

    // 404 maps to Unsupported (not Missing): every route this client calls is a collection-level
    // GET or a speculative POST, none keyed by an id — a 404 here can only mean "the connected
    // server does not register this route", which is exactly the feature-detection signal
    // console#290's acceptance criteria require (degrade to today's behavior against older
    // servers, and render the converge card's capability-gated unavailable state).
    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound or HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable,
    };
}
