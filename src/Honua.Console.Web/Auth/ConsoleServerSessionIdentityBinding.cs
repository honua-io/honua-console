using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.Options;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Binds a hosted deployment's server cookie session to its trusted edge subject before
/// operator bearer issuance. Identity comes from the authenticated server session endpoint.
/// </summary>
internal sealed class ConsoleServerSessionIdentityBinding(
    IOptions<ConsoleEdgeAuthOptions> options,
    IConsoleAccountSessionStore sessions,
    ConsoleServerSessionClientStore serverSessions,
    TimeProvider timeProvider)
{
    public async Task<bool> EnsureAsync(
        ConsoleServerSessionPartition partition,
        ConsoleEnvironmentProfile profile,
        string operatorKey,
        string? providerKey,
        CancellationToken cancellationToken)
    {
        var issuer = options.Value.ServerSessionIssuer;
        if (string.IsNullOrEmpty(issuer))
        {
            return true;
        }

        var matches = false;
        try
        {
            if (ConsoleEdgeAuthOptions.IsValidServerSessionIssuer(options.Value)
                && !string.IsNullOrWhiteSpace(providerKey))
            {
                using var response = await partition.Client.GetAsync(
                    ConsoleServerHttp.BuildUri(profile.ServerBaseUri, "api/v1/admin/auth/session"),
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var session = await response.Content.ReadFromJsonAsync(
                        ConsoleServerSessionIdentityJsonContext.Default.ConsoleServerAuthenticatedSessionWire,
                        cancellationToken).ConfigureAwait(false);
                    matches = session?.IsAuthenticated == true
                        && session.ExpiresAt > timeProvider.GetUtcNow()
                        && string.Equals(session.ProviderKey, providerKey, StringComparison.Ordinal)
                        && HasUniqueClaim(session.Claims, issuer, "iss")
                        && HasUniqueClaim(session.Claims, operatorKey, "sub", ClaimTypes.NameIdentifier);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            // An unavailable or malformed identity read cannot authorize an exchange.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout: discard this session instead of retaining an unchecked bearer.
        }

        if (!matches)
        {
            await sessions.ClearSessionAsync(profile.Id, CancellationToken.None).ConfigureAwait(false);
            serverSessions.Remove(partition.Key.OperatorKey, profile);
        }

        return matches;
    }

    private static bool HasUniqueClaim(
        IReadOnlyList<ConsoleServerSessionClaimWire>? claims,
        string expected,
        params string[] types)
    {
        if (claims is null || claims.Any(static claim => claim is null))
        {
            return false;
        }

        var values = claims.Where(claim => types.Contains(claim.Type, StringComparer.Ordinal))
            .Select(static claim => claim.Value)
            .ToArray();
        return values.Length > 0
            && values.All(value => !string.IsNullOrWhiteSpace(value)
                && string.Equals(value, expected, StringComparison.Ordinal));
    }
}

// Temporary private wire shapes: the published SDK has no admin auth/session projection.
internal sealed record ConsoleServerAuthenticatedSessionWire
{
    public bool IsAuthenticated { get; init; }
    public string? ProviderKey { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public List<ConsoleServerSessionClaimWire>? Claims { get; init; }
}

internal sealed record ConsoleServerSessionClaimWire
{
    public string? Type { get; init; }
    public string? Value { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleServerAuthenticatedSessionWire))]
internal sealed partial class ConsoleServerSessionIdentityJsonContext : JsonSerializerContext;
