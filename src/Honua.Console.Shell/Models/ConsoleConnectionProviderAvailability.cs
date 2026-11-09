namespace Honua.Console.Shell.Models;

public sealed record ConsoleConnectionProviderAvailability
{
    public IReadOnlyList<string>? InstalledProviders { get; init; }
    public string? FullImageTagSuffix { get; init; }
    public string? DocumentationHref { get; init; }
    public string? UnavailableReason { get; init; }

    public bool Allows(string provider) => UnavailableReason is null
        && (InstalledProviders is null || InstalledProviders.Contains(provider, StringComparer.OrdinalIgnoreCase));

    public static ConsoleConnectionProviderAvailability Unavailable { get; } = new()
    {
        UnavailableReason = "Console could not confirm this server's installed database providers. Check your server sign-in and connection, then reload this page."
    };
}
