using Bunit;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Shared bUnit DI helpers. Render tests build their own <see cref="BunitContext"/> and register only
/// the services the page under test needs; shell-wide services injected into many pages/components
/// (e.g. the notification surface) are registered here once so each test does not duplicate the
/// registration. Mirrors the production registration in
/// <c>HonuaConsoleShellServiceCollectionExtensions</c>.
/// </summary>
internal static class ConsoleTestServices
{
    /// <summary>
    /// Registers <see cref="IConsoleNotificationService"/> for tests that render pages/components which
    /// inject the shell notification surface. Returns the context for fluent chaining.
    /// </summary>
    public static BunitContext AddConsoleNotifications(this BunitContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ctx.Services.AddSingleton<IConsoleNotificationService, ConsoleNotificationService>();
        return ctx;
    }
}
