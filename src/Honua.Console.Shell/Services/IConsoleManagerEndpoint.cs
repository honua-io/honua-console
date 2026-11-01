namespace Honua.Console.Shell.Services;

/// <summary>Resolves an explicitly trusted manager for the active serving endpoint.</summary>
public interface IConsoleManagerEndpoint
{
    /// <summary>Returns the manager URI, or null when this profile has no trusted mapping.</summary>
    Uri? Resolve(Uri servingBaseUri);
}
