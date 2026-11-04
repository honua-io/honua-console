namespace Honua.Console.Shell.Services;

public interface IConsoleManagerEndpoint
{
    // Null means a configured manager does not match this serving URI; with no manager configured, Resolve returns the serving URI.
    Uri? Resolve(Uri servingBaseUri);
}
