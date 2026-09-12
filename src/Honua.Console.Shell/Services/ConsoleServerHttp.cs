using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Shared request helpers for the "Family-B" thin typed honua-server clients
/// (observability, scenes, SensorThings, alert rules, monitoring metrics, server
/// version, deploy approvals, GitOps releases, support tickets).
///
/// These clients build a plain <see cref="System.Net.Http.HttpClient"/> without the
/// <see cref="HonuaServerBindingHandler"/>, so the two rules that handler centralises
/// for the Family-A clients — base-URI normalisation and the operator-bearer /
/// admin-key auth decision — must live in exactly one place here too. Previously each
/// client carried verbatim copies, which let the session-sentinel fix drift (a signed-in
/// operator with no real honua-server bearer would forward the non-forwardable
/// <see cref="ConsoleAuthConstants.SessionSentinelPrefix"/> sentinel as a Bearer token
/// instead of falling back to the admin key, 401/403-ing every Family-B surface).
/// </summary>
internal static class ConsoleServerHttp
{
    private const int MaxProblemBodyCharacters = 16 * 1024;

    internal readonly record struct AuthenticationResult(bool IsAuthenticated, string Message);

    internal readonly record struct ProblemResponse(string Message, string Detail);

    /// <summary>
    /// Attaches the active operator's forwardable bearer to a honua-server read request. A
    /// configured admin key is used only when no real operator bearer exists. Human-attributable
    /// mutations use <see cref="AttachMutationAuthenticationAsync"/> instead.
    /// </summary>
    public static async Task AttachAuthenticationAsync(
        HttpRequestMessage request,
        IConsoleAccountSessionStore sessions,
        ConsoleEnvironmentProfile profile,
        string? adminApiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(profile);

        var bearer = await ResolveForwardableBearerAsync(sessions, profile, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(bearer))
        {
            request.Headers.Remove("X-API-Key");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return;
        }

        if (!string.IsNullOrWhiteSpace(adminApiKey))
        {
            request.Headers.Remove("X-API-Key");
            request.Headers.TryAddWithoutValidation("X-API-Key", adminApiKey);
        }
    }

    /// <summary>
    /// Attaches a human-attributable credential for an approval or operational
    /// mutation. Interactive mode never falls back to the shared admin API key.
    /// Explicit headless mode may use that key only when no interactive session
    /// exists and the profile is explicitly <c>ServiceApiKey</c>, so a missing/expired
    /// human bearer can never change the audit actor.
    /// </summary>
    public static async Task<AuthenticationResult> AttachMutationAuthenticationAsync(
        HttpRequestMessage request,
        IConsoleOperatorBearerProvider bearerProvider,
        ConsoleEnvironmentProfile profile,
        string? adminApiKey,
        ConsoleServerCredentialMode credentialMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bearerProvider);
        ArgumentNullException.ThrowIfNull(profile);

        request.Headers.Authorization = null;
        request.Headers.Remove("X-API-Key");

        var resolution = await bearerProvider.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        if (resolution.IsAvailable)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resolution.AccessToken);
            return new AuthenticationResult(true, string.Empty);
        }

        if (credentialMode == ConsoleServerCredentialMode.HeadlessService
            && !resolution.HasInteractiveSession
            && profile.Account.AuthMode == ConsoleAccountAuthMode.ServiceApiKey
            && !string.IsNullOrWhiteSpace(adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", adminApiKey);
            return new AuthenticationResult(true, string.Empty);
        }

        var message = credentialMode == ConsoleServerCredentialMode.HeadlessService
            && !resolution.HasInteractiveSession
            && profile.Account.AuthMode == ConsoleAccountAuthMode.ServiceApiKey
            ? "Headless/service credential mode is enabled, but no admin API key is configured."
            : resolution.Message;
        return new AuthenticationResult(false, message);
    }

    /// <summary>
    /// Resolves a relative path against an absolute honua-server base URI, ensuring the
    /// base authority + base path are preserved (a trailing slash is added when missing so
    /// the last base path segment is not dropped by <see cref="Uri"/> resolution).
    /// </summary>
    public static Uri BuildUri(Uri baseUri, string relativePath)
    {
        var normalizedBase = baseUri.AbsoluteUri.EndsWith('/')
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBase, relativePath);
    }

    /// <summary>
    /// Reads a bounded RFC 9457/problem+json failure and preserves only the
    /// operator-actionable message plus safe correlation identifiers. This is
    /// shared by focused Console clients so a failed API/MCP operation can be
    /// reconciled with <c>honua admin</c> instead of collapsing to a generic
    /// 403/500 toast. Arbitrary response fields are deliberately not echoed.
    /// </summary>
    public static async Task<ProblemResponse> ReadProblemAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var message = fallbackMessage;
        var details = new List<string>
        {
            $"HTTP {((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)} {response.ReasonPhrase}".TrimEnd()
        };

        var body = await ReadBoundedBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = document.RootElement;
                    message = FirstString(root, "detail", "title") ?? message;
                    AddProblemField(details, root, "code");
                    AddProblemField(details, root, "correlationId");
                    AddProblemField(details, root, "requestId");
                    AddProblemField(details, root, "operationId");
                    AddProblemField(details, root, "proposalId");
                    AddProblemField(details, root, "traceId");
                    AddProblemField(details, root, "auditId");
                    AddProblemField(details, root, "handleId");
                    AddProblemField(details, root, "instance");

                    if (root.TryGetProperty("extensions", out var extensions)
                        && extensions.ValueKind == JsonValueKind.Object)
                    {
                        AddProblemField(details, extensions, "correlationId");
                        AddProblemField(details, extensions, "requestId");
                        AddProblemField(details, extensions, "operationId");
                        AddProblemField(details, extensions, "proposalId");
                        AddProblemField(details, extensions, "traceId");
                        AddProblemField(details, extensions, "auditId");
                        AddProblemField(details, extensions, "handleId");
                    }
                }
            }
            catch (JsonException)
            {
                // The status and safe response headers remain useful. Never echo
                // an arbitrary non-JSON error page into the privileged Console.
            }
        }

        AddHeader(details, response, "X-Correlation-ID", "correlationId");
        AddHeader(details, response, "X-Request-ID", "requestId");
        AddHeader(details, response, "traceparent", "traceparent");

        return new ProblemResponse(
            message.ReplaceLineEndings(" "),
            string.Join("; ", details.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static async Task<string> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: false);
            var buffer = new char[2048];
            var body = new StringBuilder();
            while (body.Length <= MaxProblemBodyCharacters)
            {
                var remaining = Math.Min(buffer.Length, MaxProblemBodyCharacters + 1 - body.Length);
                var read = await reader.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                body.Append(buffer, 0, read);
            }

            return body.Length > MaxProblemBodyCharacters ? string.Empty : body.ToString();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static void AddProblemField(List<string> details, JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return;
        }

        var rendered = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(rendered) && rendered.Length <= 512)
        {
            details.Add($"{name}={rendered.ReplaceLineEndings(" ")}");
        }
    }

    private static void AddHeader(
        List<string> details,
        HttpResponseMessage response,
        string headerName,
        string label)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 512)
            {
                details.Add($"{label}={value.ReplaceLineEndings(" ")}");
            }
        }
    }

    /// <summary>
    /// Resolves the operator's forwardable honua-server bearer token for the active
    /// profile, or <see langword="null"/> when no forwardable bearer exists. Read callers may
    /// apply their documented fallback. Returns <see langword="null"/> for anonymous profiles and
    /// for the non-forwardable Console session sentinel
    /// (<see cref="ConsoleAuthConstants.IsSessionSentinel"/>), mirroring
    /// <see cref="HonuaServerBindingHandler"/> so the single auth rule cannot diverge again.
    /// </summary>
    public static async Task<string?> ResolveForwardableBearerAsync(
        IConsoleAccountSessionStore sessions,
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return null;
        }

        var session = await sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var token = session?.AccessToken;

        // A Console session sentinel marks "operator signed in" for read context but is not
        // a real honua-server bearer; do not forward it.
        return ConsoleAuthConstants.IsSessionSentinel(token)
            || session?.AccessTokenExpiresAt <= DateTimeOffset.UtcNow
                ? null
                : token;
    }
}
