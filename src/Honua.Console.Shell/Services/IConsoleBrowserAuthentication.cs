namespace Honua.Console.Shell.Services;

public interface IConsoleBrowserAuthentication
{
    bool SupportsPasswordSignIn { get; }
    Task<Uri?> SignOutAsync(CancellationToken cancellationToken = default);
    Task SignInAsync(string password, CancellationToken cancellationToken = default);
}
