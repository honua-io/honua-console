namespace Honua.Console.Shell.Services;

public interface IConsoleBrowserAuthentication
{
    bool SupportsPasswordSignIn { get; }
    Task SignInAsync(string password, CancellationToken cancellationToken = default);
}
