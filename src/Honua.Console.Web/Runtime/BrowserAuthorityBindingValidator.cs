using Honua.Console.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Web.Runtime;

internal sealed class BrowserAuthorityBindingValidator(ConsoleBrowserSession session, Uri apiBaseUri)
    : IConsoleAuthorityBindingValidator
{
    public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(session.CaptureAuthority(apiBaseUri));

    public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Equals(binding, session.CaptureAuthority(apiBaseUri), StringComparison.Ordinal));
}

internal static class BrowserAuthorityServices
{
    internal static IServiceCollection AddBrowserAuthorityBinding(this IServiceCollection services, Uri apiBaseUri) =>
        services.AddSingleton<IConsoleAuthorityBindingValidator>(provider =>
            new BrowserAuthorityBindingValidator(provider.GetRequiredService<ConsoleBrowserSession>(), apiBaseUri));
}
