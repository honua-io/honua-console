using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// bUnit component tests for <see cref="ConsoleConfirmDialog"/>: the shared confirm gate
/// used for destructive actions across the Operate admin surface.
///
/// Tests verify:
///   - Dialog is initially hidden
///   - <c>Open()</c> renders the dialog with the supplied title, body, and labels
///   - Cancel closes the dialog without calling the onConfirm callback
///   - Confirming calls the onConfirm callback and then closes the dialog
///   - A body-less open renders no body paragraph
///
/// Note: <c>ConsoleConfirmDialog.Open()</c> calls <c>StateHasChanged()</c> which must run on
/// the Blazor Dispatcher.  All direct calls to <c>cut.Instance.Open()</c> are therefore
/// wrapped in <c>await cut.InvokeAsync(...)</c>.
/// </summary>
public sealed class ConsoleConfirmDialogTests : ConsoleComponentTestBase
{
    [Fact]
    public void Dialog_is_hidden_before_Open_is_called()
    {
        var cut = Render<ConsoleConfirmDialog>();
        Assert.Empty(cut.FindAll("[data-console-confirm]"));
    }

    [Fact]
    public async Task Open_renders_dialog_with_title_body_and_confirm_label()
    {
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open(
            title: "Delete this item?",
            onConfirm: () => Task.CompletedTask,
            body: "This cannot be undone.",
            confirmLabel: "Delete"));

        Assert.NotEmpty(cut.FindAll("[data-console-confirm]"));
        cut.Find(".console-confirm-title").TextContent.MarkupMatches("Delete this item?");
        cut.Find(".console-confirm-body").TextContent.MarkupMatches("This cannot be undone.");
        Assert.Equal("Delete", cut.Find("[data-console-confirm-accept]").TextContent.Trim());
    }

    [Fact]
    public async Task Open_without_body_does_not_render_a_body_paragraph()
    {
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open(
            title: "Are you sure?",
            onConfirm: () => Task.CompletedTask));

        // No body param → .console-confirm-body must be absent.
        Assert.Empty(cut.FindAll(".console-confirm-body"));
        // Dialog and title still present.
        Assert.NotEmpty(cut.FindAll("[data-console-confirm]"));
        cut.Find(".console-confirm-title").TextContent.MarkupMatches("Are you sure?");
    }

    [Fact]
    public async Task Cancel_closes_dialog_without_calling_onConfirm()
    {
        var called = false;
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open(
            title: "Delete?",
            onConfirm: () => { called = true; return Task.CompletedTask; }));

        Assert.NotEmpty(cut.FindAll("[data-console-confirm]"));

        cut.Find(".console-button-secondary").Click();

        Assert.Empty(cut.FindAll("[data-console-confirm]"));
        Assert.False(called, "onConfirm must not be called when the user cancels.");
    }

    [Fact]
    public async Task Backdrop_click_cancels_dialog_without_calling_onConfirm()
    {
        var called = false;
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open(
            title: "Delete?",
            onConfirm: () => { called = true; return Task.CompletedTask; }));

        // Click the backdrop element directly (not the inner dialog box).
        cut.Find("[data-console-confirm]").Click();

        Assert.Empty(cut.FindAll("[data-console-confirm]"));
        Assert.False(called, "onConfirm must not be called when the backdrop is clicked.");
    }

    [Fact]
    public async Task Confirm_calls_onConfirm_and_then_closes_dialog()
    {
        var called = false;
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open(
            title: "Delete?",
            onConfirm: () => { called = true; return Task.CompletedTask; }));

        cut.Find("[data-console-confirm-accept]").Click();

        Assert.True(called, "onConfirm must be called when the user confirms.");
        Assert.Empty(cut.FindAll("[data-console-confirm]"));
    }

    [Fact]
    public async Task Custom_cancel_label_is_rendered_on_the_cancel_button()
    {
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open(
            title: "Roll back?",
            onConfirm: () => Task.CompletedTask,
            confirmLabel: "Roll back",
            cancelLabel: "Keep current"));

        Assert.Equal("Keep current", cut.Find(".console-button-secondary").TextContent.Trim());
        Assert.Equal("Roll back", cut.Find("[data-console-confirm-accept]").TextContent.Trim());
    }

    [Fact]
    public async Task Dialog_has_alertdialog_role_on_inner_container()
    {
        var cut = Render<ConsoleConfirmDialog>();

        await cut.InvokeAsync(() => cut.Instance.Open("Publish?", () => Task.CompletedTask));

        Assert.Equal("alertdialog", cut.Find("[aria-modal='true']").GetAttribute("role"));
    }
}
