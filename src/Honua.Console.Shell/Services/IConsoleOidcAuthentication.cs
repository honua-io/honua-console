namespace Honua.Console.Shell.Services;

public interface IConsoleOidcAuthentication
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<ConsoleIdentityProvider>> GetProvidersAsync(CancellationToken cancellationToken = default);
    Task<Uri> BeginAsync(string providerKey, string returnTo, CancellationToken cancellationToken = default);
    Task<string> CompleteAsync(string code, string state, CancellationToken cancellationToken = default);
}

public sealed record ConsoleIdentityProvider(string Key, string DisplayName);
