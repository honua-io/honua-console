using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// The supplied client must already carry the admin session, must be isolated per operator and profile when it owns cookies, and this type never adds an API key or an actor header.
internal sealed class HttpConsoleOperatorBearerExchange : IConsoleOperatorBearerExchange
{
    private const string BearerPath = "api/v1/admin/auth/bearer";
    private const string CsrfPath = "api/v1/admin/auth/csrf";
    private const string CsrfHeaderName = "X-Honua-CSRF";
    private readonly HttpClient _http;

    public HttpConsoleOperatorBearerExchange(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_http.DefaultRequestHeaders.Any(static header =>
                string.Equals(header.Key, "X-API-Key", StringComparison.OrdinalIgnoreCase)
                || header.Key.Contains("Actor", StringComparison.OrdinalIgnoreCase)))
        {
            return ConsoleOperatorBearerExchangeResult.Unavailable(
                "The operator-bearer exchange client is misconfigured with a shared or spoofable identity header.");
        }

        try
        {
            var csrfToken = await TryGetCsrfTokenAsync(profile, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                ConsoleServerHttp.BuildUri(profile.ServerBaseUri, BearerPath));
            if (csrfToken is not null)
            {
                request.Headers.TryAddWithoutValidation(CsrfHeaderName, csrfToken);
            }

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await ReadExchangeResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return ConsoleOperatorBearerExchangeResult.Unavailable(
                "The honua-server operator-bearer exchange is unreachable or returned an unreadable response.");
        }
    }

    // honua-server requires a fresh antiforgery token on session-cookie requests to /bearer. A server
    // without the CSRF endpoint gets the request without the header.
    private async Task<string?> TryGetCsrfTokenAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(ConsoleServerHttp.BuildUri(profile.ServerBaseUri, CsrfPath), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                ConsoleOperatorBearerJsonContext.Default.ConsoleServerCsrfResponseWire,
                cancellationToken).ConfigureAwait(false);
            return value is { RequestToken.Length: > 0 } ? value.RequestToken : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<ConsoleOperatorBearerExchangeResult> ReadExchangeResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return ConsoleOperatorBearerExchangeResult.Denied(
                "The honua-server admin session is not authenticated. Sign in again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return ConsoleOperatorBearerExchangeResult.Unavailable(
                "The honua-server operator-bearer exchange is unavailable.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync(
            stream,
            ConsoleOperatorBearerJsonContext.Default.ConsoleOperatorBearerResponseWire,
            cancellationToken).ConfigureAwait(false);

        return value is { AccessToken.Length: > 0 }
            ? ConsoleOperatorBearerExchangeResult.Issued(value.AccessToken, value.ExpiresAt)
            : ConsoleOperatorBearerExchangeResult.Unavailable(
                "The honua-server operator-bearer exchange returned an invalid response.");
    }
}

internal sealed record ConsoleOperatorBearerResponseWire
{
    public string AccessToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; init; }

    public long ExpiresIn { get; init; }
}

internal sealed record ConsoleServerCsrfResponseWire
{
    public string RequestToken { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleOperatorBearerResponseWire))]
[JsonSerializable(typeof(ConsoleServerCsrfResponseWire))]
internal sealed partial class ConsoleOperatorBearerJsonContext : JsonSerializerContext;
