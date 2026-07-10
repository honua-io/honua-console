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
    /// <summary>
    /// Attaches the active operator's forwardable bearer to a honua-server request. A
    /// configured admin key is used only when no real operator bearer exists, preserving
    /// the explicit headless/dev fallback without replacing the human audit principal.
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
    /// profile, or <see langword="null"/> when the caller should fall back to the shared
    /// admin <c>X-API-Key</c>. Returns <see langword="null"/> for anonymous profiles and
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
        // a real honua-server bearer; do not forward it — let the admin-key fallback apply.
        return ConsoleAuthConstants.IsSessionSentinel(token) ? null : token;
    }
}
