namespace Honua.Console.Shell.Services;

/// <summary>
/// Controls whether a genuinely sessionless caller may use the process-wide admin
/// API key for server mutations.
/// </summary>
public enum ConsoleServerCredentialMode
{
    /// <summary>
    /// Human-facing mode. Mutations require a forwardable operator bearer and fail
    /// closed when the operator must reauthenticate.
    /// </summary>
    Interactive,

    /// <summary>
    /// Explicit non-interactive service mode. An API key may be used only when no
    /// interactive account session exists and the active profile is explicitly marked
    /// <c>ServiceApiKey</c>.
    /// </summary>
    HeadlessService
}

internal static class ConsoleServerCredentialModeParser
{
    public static ConsoleServerCredentialMode Parse(string? value) =>
        string.Equals(value?.Trim(), nameof(ConsoleServerCredentialMode.HeadlessService), StringComparison.OrdinalIgnoreCase)
            ? ConsoleServerCredentialMode.HeadlessService
            : ConsoleServerCredentialMode.Interactive;
}
