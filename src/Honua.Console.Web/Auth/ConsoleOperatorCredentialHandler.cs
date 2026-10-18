using Honua.Console.Contracts;
using System.Net;
using System.Net.Http.Headers;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Last credential boundary before transport for interactive server requests. Typed clients may
/// stamp a service key, but the browser host can only forward the active operator's bearer.
/// Server authorization remains responsible for scopes, tenant/owner checks and approval policy.
/// </summary>
public sealed class ConsoleOperatorCredentialHandler(
    IConsoleOperatorContext operators,
    IConsoleEnvironmentProfileStore profiles,
    IConsoleAccountSessionStore sessions,
    IConsoleOperatorBearerProvider bearers,
    bool allowAnonymous = false) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-API-Key");
        request.Headers.Authorization = null;

        var pinned = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var expected);
        var capture = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Capture, out var requested) && requested;
        if (!operators.HasOperator)
        {
            return allowAnonymous && !pinned && !capture
                ? await base.SendAsync(request, cancellationToken)
                : Reauthenticate(request);
        }

        var profile = await profiles.GetActiveProfileAsync(cancellationToken);
        if (profile is null || request.RequestUri is not { IsAbsoluteUri: true } target
            || !new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/").IsBaseOf(target))
        {
            return Reauthenticate(request);
        }

        var binding = AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri);
        if (pinned && !string.Equals(expected, binding, StringComparison.Ordinal)) return AuthorityChanged(request);
        if (capture || pinned) request.Options.Set(ConsoleAuthorityRequestOptions.Resolved, binding);
        var credential = await bearers.ResolveAsync(profile, cancellationToken);
        var session = await sessions.GetSessionAsync(profile.Id, cancellationToken);
        if (!credential.IsAvailable || session?.ServerBaseUri != profile.ServerBaseUri)
        {
            return Reauthenticate(request);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (capture || pinned)
        {
            var current = await profiles.GetActiveProfileAsync(cancellationToken);
            if (!operators.HasOperator || current is null || !string.Equals(binding,
                AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), current.Id, current.ServerBaseUri), StringComparison.Ordinal))
            {
                response.Dispose();
                return AuthorityChanged(request);
            }
        }
        return response;
    }

    private static HttpResponseMessage AuthorityChanged(HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            RequestMessage = request,
            Content = new StringContent("{\"message\":\"The editor connection changed. Your edits are retained; review the original target before retrying.\"}", System.Text.Encoding.UTF8, "application/json")
        };
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        return response;
    }

    private static HttpResponseMessage Reauthenticate(HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            Content = new StringContent(
                "{\"message\":\"Sign in to honua-server again before retrying this request.\"}",
                System.Text.Encoding.UTF8, "application/json")
        };
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\""));
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        return response;
    }
}

internal sealed class ConsoleAuthorityBindingValidator(IConsoleOperatorContext operators, IConsoleEnvironmentProfileStore profiles) : IConsoleAuthorityBindingValidator
{
    public async Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default)
    {
        if (!operators.HasOperator) return false;
        var profile = await profiles.GetActiveProfileAsync(cancellationToken);
        return profile is not null && operators.HasOperator && string.Equals(binding,
            AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri), StringComparison.Ordinal);
    }
}
