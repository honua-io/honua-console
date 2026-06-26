using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class StudioAiConversationTests : ConsoleComponentTestBase
{
    [Fact]
    public void Ctrl_enter_sends_the_refine_text()
    {
        string? sent = null;
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.OnSend, text => sent = text));

        var textarea = cut.Find(".studio-ai-refine-input");
        textarea.Input("colour parcels by zoning");
        textarea.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        Assert.Equal("colour parcels by zoning", sent);
    }

    [Fact]
    public void Cmd_enter_sends_the_refine_text()
    {
        string? sent = null;
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.OnSend, text => sent = text));

        var textarea = cut.Find(".studio-ai-refine-input");
        textarea.Input("add a legend");
        textarea.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", MetaKey = true });

        Assert.Equal("add a legend", sent);
    }

    [Fact]
    public void Bare_enter_does_not_send()
    {
        var sends = 0;
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.OnSend, _ => sends++));

        var textarea = cut.Find(".studio-ai-refine-input");
        textarea.Input("still typing");
        textarea.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(0, sends);
    }

    [Fact]
    public void Ctrl_enter_with_empty_text_does_not_send()
    {
        var sends = 0;
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.OnSend, _ => sends++));

        cut.Find(".studio-ai-refine-input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        Assert.Equal(0, sends);
    }

    [Fact]
    public void Ctrl_enter_while_busy_does_not_send()
    {
        var sends = 0;
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, true)
            .Add(c => c.OnSend, _ => sends++));

        var textarea = cut.Find(".studio-ai-refine-input");
        textarea.Input("queued while busy");
        textarea.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        Assert.Equal(0, sends);
    }
}
