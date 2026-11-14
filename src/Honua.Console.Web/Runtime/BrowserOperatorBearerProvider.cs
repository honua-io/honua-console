using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Runtime;

// The browser transport owns credentials (same-origin cookie + CSRF, or the in-memory cross-origin bearer),
// so Shell mutation clients must not demand a bearer the cookie session never exposes.
public sealed class BrowserOperatorBearerProvider(ConsoleBrowserSession session, Uri apiBaseUri) : IConsoleOperatorBearerProvider
{
    public Task<ConsoleOperatorBearerResolution> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ServerBaseUri != apiBaseUri)
        {
            return Task.FromResult(new ConsoleOperatorBearerResolution
            {
                Message = "This environment is not the Honua API this Console is configured for."
            });
        }

        return Task.FromResult(session.IsAuthenticated
            ? new ConsoleOperatorBearerResolution { HasInteractiveSession = true, UsesBrowserTransport = true }
            : new ConsoleOperatorBearerResolution { Message = "Sign in to Honua before making changes." });
    }
}
