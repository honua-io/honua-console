using System.Net;
using System.Net.Http;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Findings responses are bare JSON, and a proposed action requires the operator bearer unless the profile is an explicit sessionless headless service.
public sealed partial class HttpConsoleOpsFindingsClient : IConsoleOpsFindingsClient, IConsoleAlertInboxClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load ops findings.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly string? _adminApiKey;
    private readonly IConsoleOperatorBearerProvider _operatorBearerProvider;
    private readonly ConsoleServerCredentialMode _credentialMode;

    public HttpConsoleOpsFindingsClient(
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

    public async Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsFindingsListResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, OpsFindingsRoutes.List));
        await ConsoleServerHttp.AttachAuthenticationAsync(
            request,
            _sessionStore,
            profile,
            _adminApiKey,
            cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<OpsFindingsListResponse>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server ops-findings API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                OpsFindingsJsonContext.Default.OpsFindingsListResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<OpsFindingsListResponse>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-findings API returned an empty response.")
                : OperateSectionResult<OpsFindingsListResponse>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<OpsFindingsListResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-findings API is unreachable or returned an unreadable response.");
        }
    }

    public async Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(
        string findingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsFindingProposeResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, OpsFindingsRoutes.Propose(findingId)));
        var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(
            request,
            _operatorBearerProvider,
            profile,
            _adminApiKey,
            _credentialMode,
            cancellationToken).ConfigureAwait(false);
        if (!authentication.IsAuthenticated)
        {
            return OperateSectionResult<OpsFindingProposeResponse>.Denied(
                OperateSectionStatus.Forbidden,
                authentication.Message);
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A 404 means the finding's condition cleared (or it never carried an
                // action) between listing and proposing — the page treats this as a
                // stale-finding refresh cue rather than a hard failure.
                var message = response.StatusCode == HttpStatusCode.NotFound
                    ? "This finding's condition has cleared or it no longer has a recommended action. The list has been refreshed."
                    : $"The honua-server ops-findings API returned {(int)response.StatusCode} {response.ReasonPhrase}.";
                return OperateSectionResult<OpsFindingProposeResponse>.Denied(MapStatus(response.StatusCode), message);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                OpsFindingsJsonContext.Default.OpsFindingProposeResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<OpsFindingProposeResponse>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-findings API returned an empty propose response.")
                : OperateSectionResult<OpsFindingProposeResponse>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<OpsFindingProposeResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-findings API is unreachable or returned an unreadable response.");
        }
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.Conflict => OperateSectionStatus.Conflict,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}
