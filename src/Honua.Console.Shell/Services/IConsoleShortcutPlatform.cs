namespace Honua.Console.Shell.Services;

/// <summary>
/// Reports whether keyboard-shortcut hints on the current Console host should use the macOS Command glyph
/// (<c>⌘</c>) or the Windows/Linux <c>Ctrl</c> label. Shared Razor components resolve the glyph through
/// this seam so a single implementation drives every shortcut label (honua-console#313).
/// </summary>
public interface IConsoleShortcutPlatform
{
    /// <summary><see langword="true"/> on macOS (render <c>⌘</c>); <see langword="false"/> on Windows/Linux (render <c>Ctrl</c>).</summary>
    bool UsesCommandKey { get; }
}

/// <summary>
/// Default host-runtime shortcut platform: the modifier glyph follows the OS the Console host process runs
/// on. On the native desktop (MAUI) Console this is exact — the process runs on the operator's own machine.
/// On the browser host it standardizes on the host OS, so Windows/Linux deployments (the overwhelming
/// Console target) render the correct <c>Ctrl</c> label instead of the previously hard-coded <c>⌘</c>.
/// </summary>
public sealed class RuntimeConsoleShortcutPlatform : IConsoleShortcutPlatform
{
    public bool UsesCommandKey { get; } = OperatingSystem.IsMacOS();
}
