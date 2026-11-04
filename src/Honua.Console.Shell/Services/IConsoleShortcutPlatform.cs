namespace Honua.Console.Shell.Services;

public interface IConsoleShortcutPlatform
{
    bool UsesCommandKey { get; }
}

public sealed class RuntimeConsoleShortcutPlatform : IConsoleShortcutPlatform
{
    // UsesCommandKey follows the host process, so a browser session shows the server's modifier, not the visitor's.
    public bool UsesCommandKey { get; } = OperatingSystem.IsMacOS();
}
