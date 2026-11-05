using Bunit;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

internal static class ConsoleTestServices
{
    public static BunitContext AddConsoleNotifications(this BunitContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ctx.Services.AddSingleton<IConsoleNotificationService, ConsoleNotificationService>();
        return ctx;
    }
}
