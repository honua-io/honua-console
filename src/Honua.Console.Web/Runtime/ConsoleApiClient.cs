using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Honua.Console.Web.Runtime;

// Auth bootstrap is a boundary projection, not a second copy of server/SDK domain DTOs.
public sealed class ConsoleApiClient(HttpClient http)
{
    public bool UsesCookieSession { get; init; } = true;
    public bool SupportsPasswordSignIn { get; init; } = true;

    // Same-origin password sign-in also requests a bearer when a separate manager is configured.
    public bool CapturesManagementBearer { get; init; }

    public Uri BaseUri => http.BaseAddress ?? throw new InvalidOperationException("The Honua API address is missing.");

    public async Task<ConsoleApiSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/admin/auth/session");
        request.SetBrowserRequestOption("cache", "no-store");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ConsoleApiSession(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var root = document?.RootElement ?? throw new JsonException("Missing session response.");
        if (!root.TryGetProperty("isAuthenticated", out var authenticated) || !authenticated.GetBoolean())
        {
            return new ConsoleApiSession(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        var claims = root.GetProperty("claims").EnumerateArray()
            .Select(claim => new Claim(claim.GetProperty("type").GetString()!, claim.GetProperty("value").GetString()!));
        var credentials = new ConsoleSessionCredentials(
            ExpiresAt: root.TryGetProperty("expiresAt", out var expiry) && expiry.ValueKind != JsonValueKind.Null
                ? expiry.GetDateTimeOffset() : null);
        return new ConsoleApiSession(new ClaimsPrincipal(new ClaimsIdentity(claims, "HonuaBrowserSession")), credentials);
    }

    public async Task<string> GetCsrfAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/admin/auth/csrf", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return document?.RootElement.GetProperty("requestToken").GetString()
            ?? throw new JsonException("Missing CSRF request token.");
    }

    public async Task SignOutCookieAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync("api/v1/admin/auth/logout", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SignInCookieAsync(string password, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/v1/admin/auth/session", new { password }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ConsoleSessionCredentials> GetPasswordTokenAsync(string password, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/v1/admin/auth/password-token", new { password }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var root = document?.RootElement ?? throw new JsonException("Missing token response.");
        var token = root.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(token) || !string.Equals(root.GetProperty("tokenType").GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("Missing operator bearer token.");
        }
        return new ConsoleSessionCredentials(AccessToken: token, ExpiresAt: root.GetProperty("expiresAt").GetDateTimeOffset());
    }

    public async Task<IReadOnlyList<ConsoleIdentityProvider>> GetIdentityProvidersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/admin/auth/config", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var root = document?.RootElement ?? throw new JsonException("Missing identity provider configuration.");
        if (!root.GetProperty("oidcEnabled").GetBoolean())
        {
            return [];
        }
        return root.GetProperty("providers").EnumerateArray().Select(provider => new ConsoleIdentityProvider(
            provider.GetProperty("key").GetString()!, provider.GetProperty("displayName").GetString()!)).ToArray();
    }

    public async Task<Uri> CreateAuthorizeUrlAsync(string providerKey, string returnTo, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"api/v1/admin/auth/providers/{Uri.EscapeDataString(providerKey)}/authorize-url", new { returnTo }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var value = document?.RootElement.GetProperty("authorizeUrl").GetString();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new JsonException("Invalid identity provider authorization address.");
        }
        return uri;
    }
}

public sealed record ConsoleApiSession(ClaimsPrincipal Principal, ConsoleSessionCredentials? Credentials = null);

public sealed class BrowserAuthenticationStateProvider : AuthenticationStateProvider, IConsoleBrowserAuthentication, IDisposable
{
    private readonly ConsoleApiClient api;
    private readonly ConsoleBrowserSession session;
    private readonly IConsoleAccountSessionStore sessions;

    public BrowserAuthenticationStateProvider(ConsoleApiClient api, ConsoleBrowserSession session, IConsoleAccountSessionStore sessions)
    {
        this.api = api;
        this.session = session;
        this.sessions = sessions;
        session.Changed += SessionChanged;
    }

    private void SessionChanged(ConsoleBrowserSessionSnapshot snapshot)
    {
        if (snapshot.Authenticated) return;
        _state = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        NotifyAuthenticationStateChanged(_state);
    }

    public void Dispose() => session.Changed -= SessionChanged;

    private Task<AuthenticationState>? _state;
    public bool SupportsPasswordSignIn => api.SupportsPasswordSignIn;

    public async Task SignInAsync(string password, CancellationToken cancellationToken = default)
    {
        if (!SupportsPasswordSignIn)
        {
            throw new InvalidOperationException("Password sign-in is unavailable for this identity provider.");
        }
        session.Clear();
        try
        {
            if (api.UsesCookieSession)
            {
                var csrf = await api.GetCsrfAsync(cancellationToken);
                session.Set(new ClaimsPrincipal(new ClaimsIdentity()), new ConsoleSessionCredentials(CsrfToken: csrf));
                await api.SignInCookieAsync(password, cancellationToken);
                if (api.CapturesManagementBearer)
                {
                    await PrepareCookieRequestAsync(cancellationToken);
                    var bearer = await api.GetPasswordTokenAsync(password, cancellationToken);
                    session.RememberManagementBearer(bearer.AccessToken!, bearer.ExpiresAt);
                }
            }
            else
            {
                var credentials = await api.GetPasswordTokenAsync(password, cancellationToken);
                session.Set(new ClaimsPrincipal(new ClaimsIdentity()), credentials);
            }
            await LoadAuthenticatedStateAsync();
        }
        catch
        {
            await ClearAuthenticationAsync();
            throw;
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (api.UsesCookieSession)
        {
            await PrepareCookieRequestAsync(cancellationToken);
            await api.SignOutCookieAsync(cancellationToken);
        }
        await ClearAuthenticationAsync();
    }

    public async Task PrepareCookieRequestAsync(CancellationToken cancellationToken = default)
    {
        var csrf = await api.GetCsrfAsync(cancellationToken);
        session.ReplaceServingCredentials(session.Principal, new ConsoleSessionCredentials(csrf, session.AccessToken, session.ExpiresAt));
    }

    private async Task LoadAuthenticatedStateAsync()
    {
        _state = LoadAsync();
        var state = await _state;
        if (state.User.Identity?.IsAuthenticated != true)
        {
            throw new HttpRequestException("The server did not establish a session.", null, HttpStatusCode.Unauthorized);
        }
        NotifyAuthenticationStateChanged(_state);
    }

    private async Task ClearAuthenticationAsync()
    {
        session.Clear();
        await sessions.ClearSessionAsync("configured-api", CancellationToken.None);
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        var revision = session.Revision;
        var result = await api.GetSessionAsync();
        var csrf = result.Principal.Identity?.IsAuthenticated == true && api.UsesCookieSession
            ? await api.GetCsrfAsync() : null;
        if (revision != session.Revision)
            return new AuthenticationState(session.IsAuthenticated ? session.Principal : new ClaimsPrincipal(new ClaimsIdentity()));
        var expiresAt = result.Credentials?.ExpiresAt;
        if (session.ExpiresAt is { } tokenExpiry && (expiresAt is null || tokenExpiry < expiresAt))
        {
            expiresAt = tokenExpiry;
        }
        session.ReplaceServingCredentials(result.Principal, new ConsoleSessionCredentials(csrf, session.AccessToken, expiresAt));
        var principal = session.IsAuthenticated ? result.Principal : new ClaimsPrincipal(new ClaimsIdentity());
        if (session.IsAuthenticated)
        {
            await sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "configured-api",
                AccountId = principal.AccountId() ?? string.Empty,
                DisplayName = principal.Identity?.Name ?? string.Empty,
                RoleIds = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
                ServerBaseUri = api.BaseUri
            });
        }
        return new AuthenticationState(principal);
    }
}
