using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Honua.Console.Web.Runtime;

// Auth bootstrap is a boundary projection, not a second copy of server/SDK domain DTOs.
public sealed class ConsoleApiClient(HttpClient http)
{
    public Uri BaseUri => http.BaseAddress ?? throw new InvalidOperationException("The Honua API address is missing.");

    public async Task<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/admin/auth/session", cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var root = document?.RootElement ?? throw new JsonException("Missing session response.");
        if (!root.TryGetProperty("isAuthenticated", out var authenticated) || !authenticated.GetBoolean())
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
        var claims = root.GetProperty("claims").EnumerateArray()
            .Select(claim => new Claim(claim.GetProperty("type").GetString()!, claim.GetProperty("value").GetString()!));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "HonuaBrowserSession"));
    }
}

public sealed class BrowserAuthenticationStateProvider(
    ConsoleApiClient api,
    ConsoleBrowserSession session,
    IConsoleAccountSessionStore sessions) : AuthenticationStateProvider
{
    private Task<AuthenticationState>? _state;
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        var principal = await api.GetPrincipalAsync();
        session.Set(principal);
        if (session.IsAuthenticated)
        {
            await sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "configured-api",
                AccountId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value ?? string.Empty,
                DisplayName = principal.Identity?.Name ?? string.Empty,
                RoleIds = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
                ServerBaseUri = api.BaseUri
            });
        }
        return new AuthenticationState(principal);
    }
}
