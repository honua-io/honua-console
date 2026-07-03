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

    [Fact]
    public void Textarea_is_not_disabled_while_busy_so_user_can_queue_a_followup()
    {
        // The refine textarea must remain enabled when Busy=true so the user can compose their next
        // message while waiting for the current request to complete. The Send button stays disabled.
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, true));

        var textarea = cut.Find(".studio-ai-refine-input");
        Assert.False(textarea.HasAttribute("disabled"),
            "Textarea must NOT be disabled while busy — user should be able to type a follow-up.");
    }

    [Fact]
    public void Working_indicator_is_visible_while_busy()
    {
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, true));

        var indicator = cut.Find("[data-studio-ai-working]");
        Assert.Contains("Honua is working", indicator.TextContent);
    }

    [Fact]
    public void Working_indicator_is_not_rendered_when_not_busy()
    {
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, false));

        Assert.Empty(cut.FindAll("[data-studio-ai-working]"));
    }

    [Fact]
    public void Cancel_button_is_shown_when_busy_and_OnCancel_is_wired()
    {
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, true)
            .Add(c => c.OnCancel, () => { }));

        Assert.Single(cut.FindAll("[data-studio-ai-cancel]"));
    }

    [Fact]
    public void Cancel_button_is_not_shown_when_OnCancel_is_not_wired()
    {
        // When the caller hasn't wired a cancel handler the button must not appear — no dead affordance.
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, true));

        Assert.Empty(cut.FindAll("[data-studio-ai-cancel]"));
    }

    [Fact]
    public void Clicking_cancel_button_invokes_OnCancel()
    {
        var cancelled = 0;
        var cut = Render<StudioAiConversation>(p => p
            .Add(c => c.Busy, true)
            .Add(c => c.OnCancel, () => cancelled++));

        cut.Find("[data-studio-ai-cancel]").Click();

        Assert.Equal(1, cancelled);
    }
}
