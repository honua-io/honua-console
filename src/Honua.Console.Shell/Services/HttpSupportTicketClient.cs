using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the in-product support surface to the honua-support API through the
/// <c>/api/v1/tickets</c> contracts. The support service base address is supplied
/// at registration; the bearer token comes from the active
/// <see cref="ConsoleEnvironmentProfile"/>'s account session, reusing the
/// connection pattern from <see cref="HttpConsoleOperateObservabilityClient"/>
/// (one shared <see cref="HttpClient"/>, per-request absolute URI + auth header).
/// Serialization is source-generated for trim/AOT safety.
/// </summary>
public sealed class HttpSupportTicketClient : IConsoleSupportTicketClient
{
    private readonly HttpClient _http;
    private readonly Uri _supportBaseUri;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;

    public HttpSupportTicketClient(
        HttpClient http,
        Uri supportBaseUri,
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _supportBaseUri = supportBaseUri ?? throw new ArgumentNullException(nameof(supportBaseUri));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public async Task<SupportTicketResult> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri(SupportTicketRoutes.Tickets))
        {
            Content = JsonContent.Create(request, SupportTicketJsonContext.Default.CreateSupportTicketRequest)
        };
        await AttachAuthorizationAsync(message, profile, cancellationToken).ConfigureAwait(false);
        return await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SupportTicketResult> GetTicketAsync(
        string ticketId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(SupportTicketRoutes.Ticket(ticketId)));
        await AttachAuthorizationAsync(message, profile, cancellationToken).ConfigureAwait(false);
        return await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SupportTicketResult> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SupportTicketResult.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-support API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer
                .DeserializeAsync(stream, SupportTicketJsonContext.Default.SupportTicketResponse, cancellationToken)
                .ConfigureAwait(false);
            return value is null
                ? SupportTicketResult.Denied(OperateSectionStatus.Unavailable, "The honua-support API returned an empty response.")
                : SupportTicketResult.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return SupportTicketResult.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-support API is unreachable or returned an unreadable response.");
        }
    }

    private async Task AttachAuthorizationAsync(
        HttpRequestMessage message,
        ConsoleEnvironmentProfile? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null || profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return;
        }

        var session = await _sessionStore.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(session?.AccessToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }
    }

    private Uri BuildUri(string relativePath)
    {
        var normalizedBase = _supportBaseUri.AbsoluteUri.EndsWith('/')
            ? _supportBaseUri
            : new Uri(_supportBaseUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBase, relativePath);
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}
