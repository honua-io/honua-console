namespace Honua.Console.Shell.Services;

/// <summary>
/// Platform-adaptive formatting for the keyboard-shortcut hints Console renders next to primary actions
/// (e.g. the Studio "Send" affordance). macOS labels the primary modifier with the Command glyph
/// (<c>⌘</c>); Windows and Linux label it <c>Ctrl</c>. Centralizing the format here means every shortcut
/// label picks up the correct glyph from one place rather than hard-coding <c>⌘</c> (which read as
/// platform-wrong on Windows/Linux — UCD review 2026-07-12, honua-console#313).
/// </summary>
public static class ConsoleKeyboardShortcut
{
    /// <summary>The Return/Enter key glyph shared by every submit chord.</summary>
    public const string ReturnKeyGlyph = "↵";

    /// <summary>
    /// The primary command/control modifier label: <c>⌘</c> on macOS, <c>Ctrl</c> on Windows/Linux.
    /// </summary>
    public static string PrimaryModifierLabel(bool usesCommandKey) => usesCommandKey ? "⌘" : "Ctrl";

    /// <summary>
    /// The "confirm with the primary modifier + Return" chord: <c>⌘↵</c> on macOS, <c>Ctrl+↵</c> on
    /// Windows/Linux. The macOS convention concatenates the modifier and key glyphs; the Windows/Linux
    /// convention joins them with a <c>+</c>.
    /// </summary>
    public static string SubmitChord(bool usesCommandKey) =>
        usesCommandKey ? $"⌘{ReturnKeyGlyph}" : $"Ctrl+{ReturnKeyGlyph}";

    /// <summary>
    /// A full action label with its submit chord, e.g. <c>Send · ⌘↵</c> (macOS) or
    /// <c>Send · Ctrl+↵</c> (Windows/Linux).
    /// </summary>
    public static string SubmitActionLabel(string action, bool usesCommandKey) =>
        $"{action} · {SubmitChord(usesCommandKey)}";
}
