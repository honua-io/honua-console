using System.Text.Json;

namespace Honua.Console.Shell.Services;

// honua-server requires a fresh antiforgery token on session-cookie requests to its browser-session
// endpoints. A server without the CSRF endpoint, or an unreadable answer, gets the request without it.
internal static class ConsoleServerCsrf
{
    private const string CsrfPath = "api/v1/admin/auth/csrf";
    private const string HeaderName = "X-Honua-CSRF";

    public static async Task<string?> TryGetTokenAsync(
        HttpClient http,
        Uri serverBaseUri,
        CancellationToken cancellationToken)
    {
        using var response = await http
            .GetAsync(ConsoleServerHttp.BuildUri(serverBaseUri, CsrfPath), cancellationToken)
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

    public static void AddToken(HttpRequestMessage request, string? token)
    {
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderName, token);
        }
    }
}
