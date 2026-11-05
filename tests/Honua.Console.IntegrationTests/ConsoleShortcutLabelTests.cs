using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

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
