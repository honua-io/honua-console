namespace Honua.Console.Shell.DependencyInjection;

public sealed record HonuaConsoleShellOptions
{
    public string? ServerBaseUrl { get; init; }

    public string? ServerAdminApiKey { get; init; }

    public string? ServerPublicationIds { get; init; }

    public string? ServerTemporalSources { get; init; }

    // Only HeadlessService lets a sessionless caller use the admin API key; the default requires an operator bearer.
    public string? ServerCredentialMode { get; init; }

    public string? SupportBaseUrl { get; init; }

    public string? SupportKbPath { get; init; }

    public string? LlmBaseUrl { get; init; }

    public string? LlmModel { get; init; }

    public string? LlmApiKey { get; init; }

    // Narrows the live server capability manifest; it can never widen it.
    public string? AdvertisedCapabilities { get; init; }

    public bool RegistryIntentResolutionEnabled { get; init; }

    public bool V4Enabled { get; init; }

    public bool CatalogAdminEnabled { get; init; }

    public string? ConsoleMode { get; init; }
}
