using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Platform-adaptive keyboard-shortcut glyphs (UCD review 2026-07-12, honua-console#313). The primary
/// modifier renders as the Command glyph (⌘) on macOS and as Ctrl on Windows/Linux; the shared
/// <see cref="ConsoleShortcutLabel"/> component resolves the glyph from <see cref="IConsoleShortcutPlatform"/>
/// so every shortcut label picks up the correct platform glyph from one place, fixing the previously
/// hard-coded ⌘ that read as platform-wrong on Windows.
/// </summary>
public sealed class ConsoleShortcutLabelTests
{
    [Fact]
    public void PrimaryModifier_IsCommandOnMac_AndCtrlElsewhere()
    {
        Assert.Equal("⌘", ConsoleKeyboardShortcut.PrimaryModifierLabel(usesCommandKey: true));
        Assert.Equal("Ctrl", ConsoleKeyboardShortcut.PrimaryModifierLabel(usesCommandKey: false));
    }

    [Fact]
    public void SubmitChord_ConcatenatesOnMac_AndJoinsWithPlusElsewhere()
    {
        Assert.Equal("⌘↵", ConsoleKeyboardShortcut.SubmitChord(usesCommandKey: true));
        Assert.Equal("Ctrl+↵", ConsoleKeyboardShortcut.SubmitChord(usesCommandKey: false));
    }

    [Theory]
    [InlineData(true, "Send · ⌘↵")]
    [InlineData(false, "Send · Ctrl+↵")]
    public void SubmitActionLabel_IsPlatformAdaptive(bool usesCommandKey, string expected)
    {
        Assert.Equal(expected, ConsoleKeyboardShortcut.SubmitActionLabel("Send", usesCommandKey));
    }

    [Theory]
    [InlineData(true, "Send · ⌘↵")]
    [InlineData(false, "Send · Ctrl+↵")]
    public void ConsoleShortcutLabel_RendersPlatformGlyphFromInjectedPlatform(bool usesCommandKey, string expected)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleShortcutPlatform>(new StubShortcutPlatform(usesCommandKey));

        var component = ctx.Render<ConsoleShortcutLabel>(parameters => parameters.Add(p => p.Action, "Send"));

        Assert.Equal(expected, component.Find("[data-console-shortcut='Send']").TextContent);
    }

    private sealed class StubShortcutPlatform(bool usesCommandKey) : IConsoleShortcutPlatform
    {
        public bool UsesCommandKey { get; } = usesCommandKey;
    }
}
