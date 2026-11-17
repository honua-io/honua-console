namespace Honua.Console.Shell.Services;

// What a profile-resolving server client needs to authenticate: reads may use the admin key, writes only the operator.
public sealed class ConsoleServerAccess
{
    public required IConsoleEnvironmentProfileStore Profiles { get; init; }

    public required IConsoleAccountSessionStore Sessions { get; init; }

    public required IConsoleOperatorBearerProvider OperatorBearer { get; init; }

    public string? AdminApiKey { get; init; }

    public ConsoleServerCredentialMode CredentialMode { get; init; } = ConsoleServerCredentialMode.Interactive;
}
