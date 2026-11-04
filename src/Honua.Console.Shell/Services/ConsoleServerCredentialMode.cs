namespace Honua.Console.Shell.Services;

public enum ConsoleServerCredentialMode
{
    // Mutations require a forwardable operator bearer and fail closed when the operator must reauthenticate.
    Interactive,

    // An API key is allowed only when no interactive session exists and the profile is marked ServiceApiKey.
    HeadlessService
}

internal static class ConsoleServerCredentialModeParser
{
    public static ConsoleServerCredentialMode Parse(string? value) =>
        string.Equals(value?.Trim(), nameof(ConsoleServerCredentialMode.HeadlessService), StringComparison.OrdinalIgnoreCase)
            ? ConsoleServerCredentialMode.HeadlessService
            : ConsoleServerCredentialMode.Interactive;
}
