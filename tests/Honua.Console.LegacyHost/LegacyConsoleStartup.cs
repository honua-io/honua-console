using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.HttpOverrides;

namespace Honua.Console.Web;

internal static class LegacyConsoleStartup
{
    internal static void AddLegacyConsoleServices(this WebApplicationBuilder builder)
    {
        // Add services to the container.
        builder.Services.AddRazorComponents()
            // Detailed circuit errors leak exception messages and stack traces to the browser, so only enable
            // them in Development; production circuits surface a generic error and log the detail server-side.
            .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

        // NL->workflow generation against a local CPU model can hold a circuit for minutes and
        // return a graph payload larger than SignalR's 32 KB default receive cap. Raise the cap and
        // the client timeout so a slow-but-valid generation renders instead of terminating the circuit.
        builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
        {
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
            options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        });
        builder.AddConsoleObservability();
        builder.Services.AddSingleton<AgentMcpSessions>();
        builder.Services.AddHttpClient("honua-agent-workspace").ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddHttpClient("honua-studio-audience").ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");

        builder.Services.AddHonuaConsoleShell(ConsoleShellConfiguration.ReadOptions(builder.Configuration));

        builder.AddConsoleAuthentication();
        builder.AddConsoleServerSessionBff();
        builder.Services.Configure<ForwardedHeadersOptions>(ConsoleForwardedHeaders.Apply);
    }
}
