using System.Net.Http.Headers;
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
    internal readonly record struct AuthenticationResult(bool IsAuthenticated, string Message);

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
