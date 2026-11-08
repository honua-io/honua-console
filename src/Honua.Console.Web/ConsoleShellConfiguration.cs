using Honua.Console.Shell.DependencyInjection;

namespace Honua.Console.Web;

// Each setting reads its configuration key first, then the container environment variable.
internal static class ConsoleShellConfiguration
{
    public static HonuaConsoleShellOptions ReadOptions(IConfiguration configuration) => new()
    {
        ServerBaseUrl = Read(configuration, "Honua:Server:BaseUrl", "HONUA_SERVER_BASE_URL"),
        ServerAdminApiKey = Read(configuration, "Honua:Server:AdminApiKey", "HONUA_ADMIN_API_KEY"),
        ServerPublicationIds = Read(configuration, "Honua:Server:PublicationIds", "HONUA_SERVER_PUBLICATION_IDS"),
        ServerTemporalSources = Read(configuration, "Honua:Server:TemporalSources", "HONUA_SERVER_TEMPORAL_SOURCES"),
        ServerCredentialMode = Read(configuration, "Honua:Server:CredentialMode", "HONUA_SERVER_CREDENTIAL_MODE"),
        SupportBaseUrl = Read(configuration, "Honua:Support:BaseUrl", "HONUA_SUPPORT_BASE_URL"),
        SupportKbPath = Read(configuration, "Honua:Support:KbPath", "HONUA_SUPPORT_KB_PATH"),
        LlmBaseUrl = Read(configuration, "Honua:Llm:BaseUrl", "HONUA_LLM_BASE_URL"),
        LlmModel = Read(configuration, "Honua:Llm:Model", "HONUA_LLM_MODEL"),
        LlmApiKey = Read(configuration, "Honua:Llm:ApiKey", "HONUA_LLM_API_KEY"),
        AdvertisedCapabilities = Read(configuration, "Honua:Console:Capabilities", "HONUA_CONSOLE_CAPABILITIES"),
        CatalogAdminEnabled = bool.TryParse(
            Read(configuration, "Honua:Console:Catalogs:Enabled", "HONUA_CONSOLE_CATALOGS_ENABLED"),
            out var catalogsEnabled) && catalogsEnabled,
        RegistryIntentResolutionEnabled = bool.TryParse(
            Read(configuration, "Studio:RegistryIntentResolution", "HONUA_STUDIO_REGISTRY_INTENT_RESOLUTION"),
            out var enabled) && enabled,
        ConsoleMode = Read(configuration, "Honua:Console:Mode", "HONUA_CONSOLE_MODE"),
    };

    private static string? Read(IConfiguration configuration, string key, string environmentVariable) =>
        configuration[key] ?? configuration[environmentVariable];
}
