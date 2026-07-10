using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Calls honua-server's shipped operator-bearer issuance contract. The supplied
/// <see cref="HttpClient"/> must already carry the authenticated server admin session,
/// normally through a same-origin BFF. An instance that owns cookies must be isolated
/// per operator and profile and must never be registered process-wide. This client never
/// attaches an admin API key or an actor header.
/// </summary>
internal sealed class HttpConsoleOperatorBearerExchange : IConsoleOperatorBearerExchange
{
    private const string BearerPath = "api/v1/admin/auth/bearer";
    private readonly HttpClient _http;

    /// <summary>Initializes a new exchange client.</summary>
    public HttpConsoleOperatorBearerExchange(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
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

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, BearerPath));

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

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
}

internal sealed record ConsoleOperatorBearerResponseWire
{
    public string AccessToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; init; }

    public long ExpiresIn { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleOperatorBearerResponseWire))]
internal sealed partial class ConsoleOperatorBearerJsonContext : JsonSerializerContext;
