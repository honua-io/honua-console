using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Calls the Console host's /api/support/* BFF routes only; never holds or sends a honua-support API key.
public sealed class HttpConsoleSupportPortalClient : IConsoleSupportPortalClient
{
    private const string UnconfiguredErrorCode = "support_portal_unconfigured";

    private readonly HttpClient _http;
    private readonly Uri _bffBaseUri;

    public HttpConsoleSupportPortalClient(HttpClient http, Uri bffBaseUri)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _bffBaseUri = bffBaseUri ?? throw new ArgumentNullException(nameof(bffBaseUri));
    }

    public Task<SupportPortalResult<SupportPreflightResponse>> RunPreflightAsync(
        SupportPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var message = CreatePost(
            SupportPortalRoutes.BffPreflight,
            request,
            SupportPortalJsonContext.Default.SupportPreflightRequest);
        return SendAsync(message, SupportPortalJsonContext.Default.SupportPreflightResponse, cancellationToken);
    }

    public Task<SupportPortalResult<IReadOnlyList<SupportTicketResponse>>> ListTicketsAsync(
        CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(SupportPortalRoutes.BffTickets));
        return SendAsync(message, SupportPortalJsonContext.Default.IReadOnlyListSupportTicketResponse, cancellationToken);
    }

    public Task<SupportPortalResult<SupportTicketResponse>> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var message = CreatePost(
            SupportPortalRoutes.BffTickets,
            request,
            SupportPortalJsonContext.Default.CreateSupportTicketRequest);
        return SendAsync(message, SupportPortalJsonContext.Default.SupportTicketResponse, cancellationToken);
    }

    public Task<SupportPortalResult<SupportTicketResponse>> ApproveTicketAsync(
        string ticketId,
        SupportApproveTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentNullException.ThrowIfNull(request);
        var message = CreatePost(
            SupportPortalRoutes.BffApproveTicket(ticketId.Trim()),
            request,
            SupportPortalJsonContext.Default.SupportApproveTicketRequest);
        return SendAsync(message, SupportPortalJsonContext.Default.SupportTicketResponse, cancellationToken);
    }

    public Task<SupportPortalResult<SupportRevokeAccessResponse>> RevokeAccessAsync(
        string ticketId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        var payload = new SupportRevokeAccessRequest
        {
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        };
        var message = CreatePost(
            SupportPortalRoutes.BffRevokeAccess(ticketId.Trim()),
            payload,
            SupportPortalJsonContext.Default.SupportRevokeAccessRequest);
        return SendAsync(message, SupportPortalJsonContext.Default.SupportRevokeAccessResponse, cancellationToken);
    }

    public Task<SupportPortalResult<SupportSecurityAdvisoriesResponse>> ListAdvisoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(SupportPortalRoutes.BffSecurityAdvisories));
        return SendAsync(message, SupportPortalJsonContext.Default.SupportSecurityAdvisoriesResponse, cancellationToken);
    }

    public Task<SupportPortalResult<SupportFleetInstancesResponse>> ListFleetInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(SupportPortalRoutes.BffFleetInstances));
        return SendAsync(message, SupportPortalJsonContext.Default.SupportFleetInstancesResponse, cancellationToken);
    }

    private HttpRequestMessage CreatePost<TBody>(string route, TBody body, JsonTypeInfo<TBody> typeInfo) =>
        new(HttpMethod.Post, BuildUri(route))
        {
            Content = JsonContent.Create(body, typeInfo)
        };

    private async Task<SupportPortalResult<T>> SendAsync<T>(
        HttpRequestMessage message,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken)
    {
        using (message)
        {
            try
            {
                using var response = await _http
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return await ReadErrorAsync<T>(response, cancellationToken).ConfigureAwait(false);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var value = await JsonSerializer.DeserializeAsync(stream, responseType, cancellationToken).ConfigureAwait(false);
                return value is null
                    ? SupportPortalResult<T>.Denied(OperateSectionStatus.Unavailable, "The Support Portal proxy returned an empty response.")
                    : SupportPortalResult<T>.Allowed(value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                return SupportPortalResult<T>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The Support Portal proxy is unreachable or returned an unreadable response.");
            }
        }
    }

    private static async Task<SupportPortalResult<T>> ReadErrorAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var payload = TryParseError(body);
        var status = ResolveFailureStatus(response.StatusCode, payload);
        var message = !string.IsNullOrWhiteSpace(payload?.Message)
            ? payload.Message!
            : $"The Support Portal proxy returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        return SupportPortalResult<T>.Denied(status, message);
    }

    private static SupportPortalErrorPayload? TryParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(body, SupportPortalJsonContext.Default.SupportPortalErrorPayload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OperateSectionStatus ResolveFailureStatus(
        HttpStatusCode code,
        SupportPortalErrorPayload? payload)
    {
        if (code == HttpStatusCode.ServiceUnavailable
            && string.Equals(payload?.Error, UnconfiguredErrorCode, StringComparison.OrdinalIgnoreCase))
        {
            return OperateSectionStatus.Unsupported;
        }

        return code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
            HttpStatusCode.NotFound => OperateSectionStatus.Missing,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => OperateSectionStatus.Rejected,
            HttpStatusCode.Conflict => OperateSectionStatus.Conflict,
            HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
            _ => OperateSectionStatus.Unavailable
        };
    }

    private Uri BuildUri(string relativePath) =>
        ConsoleServerHttp.BuildUri(_bffBaseUri, relativePath);
}
