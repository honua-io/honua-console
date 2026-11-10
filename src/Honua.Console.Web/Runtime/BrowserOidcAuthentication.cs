using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Microsoft.JSInterop;

namespace Honua.Console.Web.Runtime;

// Only correlation metadata crosses the redirect. PKCE and provider credentials stay on the server.
public sealed class BrowserOidcAuthentication(
    ConsoleApiClient api,
    BrowserAuthenticationStateProvider authentication,
    IJSRuntime javascript) : IConsoleOidcAuthentication
{
    private const string PendingKey = "honua.console.pending-oidc";
    public bool IsAvailable => api.UsesCookieSession && !api.SupportsPasswordSignIn;

    public async Task<IReadOnlyList<ConsoleIdentityProvider>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return await api.GetIdentityProvidersAsync(cancellationToken);
    }

    public async Task<Uri> BeginAsync(string providerKey, string returnTo, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        var providers = await GetProvidersAsync(cancellationToken);
        if (!providers.Any(provider => provider.Key == providerKey))
        {
            throw new InvalidOperationException("The identity provider is unavailable.");
        }
        await authentication.PrepareCookieRequestAsync(cancellationToken);
        var uri = await api.CreateAuthorizeUrlAsync(providerKey, cancellationToken);
        var state = ConsoleUrlQuery.GetValue(uri.AbsoluteUri, "state");
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new JsonException("The identity provider did not return correlation state.");
        }
        var pending = new PendingSignIn(providerKey, state, ConsoleReturnUrl.Sanitize(returnTo), DateTimeOffset.UtcNow);
        await javascript.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, PendingKey, JsonSerializer.Serialize(pending, PendingSignInJsonContext.Default.PendingSignIn));
        return uri;
    }

    public async Task<string> CompleteAsync(string code, string state, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        var stored = await javascript.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, PendingKey);
        await javascript.InvokeVoidAsync("sessionStorage.removeItem", cancellationToken, PendingKey);
        var pending = stored is null ? null : JsonSerializer.Deserialize(stored, PendingSignInJsonContext.Default.PendingSignIn);
        if (pending is null || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)
            || !string.Equals(pending.State, state, StringComparison.Ordinal)
            || pending.CreatedAt > DateTimeOffset.UtcNow || pending.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
        {
            throw new InvalidOperationException("Sign-in correlation is missing or expired.");
        }
        await authentication.PrepareCookieRequestAsync(cancellationToken);
        await api.CompleteOidcAsync(pending.ProviderKey, code, state, cancellationToken);
        await authentication.RefreshAsync();
        return ConsoleReturnUrl.Sanitize(pending.ReturnTo);
    }

    private void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Server-brokered identity provider sign-in requires the same origin.");
        }
    }
}

internal sealed record PendingSignIn(string ProviderKey, string State, string ReturnTo, DateTimeOffset CreatedAt);

[JsonSerializable(typeof(PendingSignIn))]
internal partial class PendingSignInJsonContext : JsonSerializerContext;
