using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

internal static class SupportRegistrations
{
    public static void AddSupport(IServiceCollection services, HonuaConsoleShellOptions options)
    {
        AddTicketClient(services, options.SupportBaseUrl);

        // Validates every bundle against the pinned diagnostic-bundle.v1 schema before it can leave the Console.
        services.TryAddSingleton<Diagnostics.DiagnosticBundleExporter>();
        services.TryAddSingleton<ConsoleSupportContextBuilder>(serviceProvider =>
            new ConsoleSupportContextBuilder(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<IConsoleOperateObservabilityClient>()));

        AddAssistantClient(services, options);
        services.TryAddSingleton<IConsoleSupportKnowledgeBase>(_ => FileConsoleSupportKnowledgeBase.Load(options.SupportKbPath));
        services.TryAddSingleton<IConsoleSupportDeflectionLog, ConsoleSupportDeflectionLog>();
    }

    private static void AddTicketClient(IServiceCollection services, string? supportBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(supportBaseUrl)
            || !Uri.TryCreate(supportBaseUrl.Trim(), UriKind.Absolute, out var supportBaseUri))
        {
            services.TryAddSingleton<IConsoleSupportTicketClient, UnsupportedSupportTicketClient>();
            return;
        }

        services.TryAddSingleton<IConsoleSupportTicketClient>(serviceProvider =>
            new HttpSupportTicketClient(
                ShellHttpClients.Standalone(ShellHttpClients.ReadTimeout),
                supportBaseUri,
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));
    }

    // Without both an http(s) endpoint and a model id the /support page hides the assistant.
    private static void AddAssistantClient(IServiceCollection services, HonuaConsoleShellOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LlmBaseUrl)
            || string.IsNullOrWhiteSpace(options.LlmModel)
            || !Uri.TryCreate(options.LlmBaseUrl.Trim(), UriKind.Absolute, out var llmBaseUri)
            || (llmBaseUri.Scheme != Uri.UriSchemeHttp && llmBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IConsoleSupportAssistantClient, UnsupportedConsoleSupportAssistantClient>();
            return;
        }

        var model = options.LlmModel.Trim();
        services.TryAddSingleton<IConsoleSupportAssistantClient>(_ =>
            new HttpConsoleSupportAssistantClient(
                ShellHttpClients.Standalone(ShellHttpClients.LongRequestTimeout),
                llmBaseUri,
                model,
                options.LlmApiKey));
    }
}
