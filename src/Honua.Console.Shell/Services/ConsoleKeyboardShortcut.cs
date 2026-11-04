namespace Honua.Console.Shell.Services;

public static class ConsoleKeyboardShortcut
{
    public const string ReturnKeyGlyph = "↵";

    public static string PrimaryModifierLabel(bool usesCommandKey) => usesCommandKey ? "⌘" : "Ctrl";

    public static string SubmitChord(bool usesCommandKey) =>
        usesCommandKey ? $"⌘{ReturnKeyGlyph}" : $"Ctrl+{ReturnKeyGlyph}";

    public static string SubmitActionLabel(string action, bool usesCommandKey) =>
        $"{action} · {SubmitChord(usesCommandKey)}";
}
