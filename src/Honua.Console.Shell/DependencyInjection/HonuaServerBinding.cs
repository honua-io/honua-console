using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

internal sealed record HonuaServerBinding(Uri? BaseUri, string? AdminApiKey)
{
    [MemberNotNullWhen(true, nameof(BaseUri))]
    public bool IsLive => BaseUri is not null;

    public static HonuaServerBinding From(HonuaConsoleShellOptions options) =>
        new(ParseHttpBaseUri(options.ServerBaseUrl), options.ServerAdminApiKey);

    private static Uri? ParseHttpBaseUri(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed
            : null;
}

internal static class HonuaServerBindingRegistration
{
    // Without a bound server the Unsupported implementation renders the missing-binding state, never mock data.
    public static void AddServerBound<TService, TLive, TUnsupported>(this IServiceCollection services, HonuaServerBinding server)
        where TService : class
        where TLive : class, TService
        where TUnsupported : class, TService
    {
        if (server.IsLive)
        {
            services.TryAddSingleton<TService, TLive>();
        }
        else
        {
            services.TryAddSingleton<TService, TUnsupported>();
        }
    }
}
