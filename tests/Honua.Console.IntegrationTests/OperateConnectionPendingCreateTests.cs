using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class OperateConnectionV4RenderTests
{
    public enum DraftEvent { Input, Change }

    public sealed record DraftEdit(string Selector, string Value, DraftEvent Event = DraftEvent.Input)
    {
        public void Apply(IRenderedComponent<OperateConnectionNewPage> page)
        {
            if (Event == DraftEvent.Change) page.Find(Selector).Change(Value);
            else page.Find(Selector).Input(Value);
        }
    }

    public static IEnumerable<object[]> PendingCreateEdits()
    {
        DraftEdit[] edits =
        [
            new("input[placeholder='prod-postgis']", "newer-name"),
            new("select:not([aria-label])", "mysql", DraftEvent.Change),
            new("[data-credential-source]", "secret", DraftEvent.Change),
            new("input[placeholder='db-prod.honua.internal']", "newer.example"),
            new("input[type=number]", "5433", DraftEvent.Change),
            new("input[placeholder='honua_geo']", "newer-database"),
            new("input[placeholder='honua_reader']", "newer-user"),
            new("input[type=password]", "newer-password"),
            new(".studio-field-grid > label:nth-last-child(2) select", "Require", DraftEvent.Change),
            new("input[placeholder='Production PostGIS for parcels and hazards']", "newer-description")
        ];
        foreach (var enabled in new[] { false, true })
        {
            foreach (var edit in edits) yield return [enabled, edit];
        }
    }

    [Theory]
    [MemberData(nameof(PendingCreateEdits))]
    public async Task SuccessfulCreatePreservesEveryNewerDraftField(bool enabled, DraftEdit edit)
    {
        using var context = Context(enabled, "/console/");
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());
        Assert.Single(operations.CreatedCommands);
        Assert.True(CreateButton(page).HasAttribute("disabled"));
        edit.Apply(page);

        await page.InvokeAsync(() => operations.Create.SetResult(CreatedConnection()));
        await pending;

        Assert.Equal("http://localhost/console/operate/connections/new", context.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.False(CreateButton(page).HasAttribute("disabled"));
        Assert.Equal(edit.Value, page.Find(edit.Selector) is AngleSharp.Html.Dom.IHtmlSelectElement select ? select.Value : page.Find(edit.Selector).GetAttribute("value"));
        var receipt = page.Find("[data-created-connection]");
        Assert.Equal("/console/operate/connections/created%20id", receipt.GetAttribute("href"));
        Assert.Contains("submitted-name", receipt.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-password", receipt.TextContent, StringComparison.Ordinal);
        Assert.Equal(0, context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.PendingOperationCount);
    }

    [Theory]
    [MemberData(nameof(PendingCreateEdits))]
    public async Task RejectedCreateDoesNotBindErrorsToANewerDraft(bool enabled, DraftEdit edit)
    {
        using var context = Context(enabled);
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());
        edit.Apply(page);

        await page.InvokeAsync(() => operations.Create.SetResult(RejectedConnection()));
        await pending;

        Assert.DoesNotContain("Submitted host was rejected", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Original create failed", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-created-connection]"));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.False(CreateButton(page).HasAttribute("disabled"));
        Assert.Equal(edit.Value, page.Find(edit.Selector) is AngleSharp.Html.Dom.IHtmlSelectElement select ? select.Value : page.Find(edit.Selector).GetAttribute("value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedSuccessfulCreateNavigatesWithoutDiscardPrompt(bool enabled)
    {
        using var context = Context(enabled, "/console/");
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());

        await page.InvokeAsync(() => operations.Create.SetResult(CreatedConnection()));
        await pending;

        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Equal("http://localhost/console/operate/connections/created%20id", context.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Single(operations.CreatedCommands);
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier == "confirm");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedRejectedCreateKeepsTheCurrentFieldErrors(bool enabled)
    {
        using var context = Context(enabled);
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());

        await page.InvokeAsync(() => operations.Create.SetResult(RejectedConnection()));
        await pending;

        Assert.Contains("Submitted host was rejected", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Original create failed", page.Markup, StringComparison.Ordinal);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Equal("submitted-name", page.Find("input[placeholder='prod-postgis']").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-created-connection]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatedNameCannotBeSubmittedTwiceAfterAnUnrelatedEdit(bool enabled)
    {
        using var context = Context(enabled);
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());
        page.Find("input[placeholder='Production PostGIS for parcels and hazards']").Input("newer-description");
        await page.InvokeAsync(() => operations.Create.SetResult(CreatedConnection()));
        await pending;

        Assert.Single(page.FindAll("[data-created-connection]"));
        await CreateButton(page).ClickAsync(new());

        Assert.Single(operations.CreatedCommands);
        Assert.Contains("already exists", page.Markup, StringComparison.Ordinal);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Single(page.FindAll("[data-created-connection]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewNameCanBeSubmittedAfterTheEarlierCreationCompletes(bool enabled)
    {
        using var context = Context(enabled);
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());
        page.Find("input[placeholder='prod-postgis']").Input("newer-name");
        await page.InvokeAsync(() => operations.Create.SetResult(CreatedConnection()));
        await pending;

        var second = CreateButton(page).ClickAsync(new());
        Assert.Equal(new[] { "submitted-name", "newer-name" }, operations.CreatedCommands.Select(command => command.Name));
        await page.InvokeAsync(() => operations.SecondCreate.SetResult(CreatedConnection() with { ConnectionId = "new-id" }));
        await second;

        Assert.Equal("http://localhost/operate/connections/new-id", context.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingAwayAndBackStillPreservesTheDraft(bool enabled)
    {
        using var context = Context(enabled);
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        var pending = CreateButton(page).ClickAsync(new());
        page.Find("input[placeholder='prod-postgis']").Input("temporary-name");
        page.Find("input[placeholder='prod-postgis']").Input("submitted-name");
        await page.InvokeAsync(() => operations.Create.SetResult(CreatedConnection()));
        await pending;

        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Single(page.FindAll("[data-created-connection]"));
        Assert.Equal("http://localhost/operate/connections/new", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASecretReferenceEditCannotBeDiscardedByCreation(bool enabled)
    {
        using var context = Context(enabled);
        var operations = RegisterPendingCreate(context);
        var page = RenderValidConnectionDraft(context);
        page.Find("[data-credential-source]").Change("secret");
        page.Find("[data-secret-reference]").Input("env:SUBMITTED_DSN");
        var pending = CreateButton(page).ClickAsync(new());
        page.Find("[data-secret-reference]").Input("env:NEWER_DSN");
        await page.InvokeAsync(() => operations.Create.SetResult(CreatedConnection()));
        await pending;

        Assert.Equal("env:SUBMITTED_DSN", Assert.Single(operations.CreatedCommands).SecretReference);
        Assert.Equal("env:NEWER_DSN", page.Find("[data-secret-reference]").GetAttribute("value"));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.DoesNotContain("SUBMITTED_DSN", page.Find("[data-created-connection]").TextContent, StringComparison.Ordinal);
    }

    private static Operations RegisterPendingCreate(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var operations = new Operations();
        context.Services.AddSingleton<IConsoleConnectionCreateOperation>(operations);
        return operations;
    }

    private static IRenderedComponent<OperateConnectionNewPage> RenderValidConnectionDraft(BunitContext context)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("operate/connections/new");
        var page = context.Render<OperateConnectionNewPage>();
        page.Find("input[placeholder='prod-postgis']").Input("submitted-name");
        page.Find("input[placeholder='db-prod.honua.internal']").Input("submitted.example");
        page.Find("input[placeholder='honua_geo']").Input("spatial");
        page.Find("input[placeholder='honua_reader']").Input("reader");
        page.Find("input[type=password]").Input("fixture-password");
        return page;
    }

    private static AngleSharp.Dom.IElement CreateButton(IRenderedComponent<OperateConnectionNewPage> page) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() is "Create connection" or "Creating…");

    private static ConsoleConnectionCreateResult CreatedConnection() => new()
    {
        Succeeded = true,
        State = "Created",
        ConnectionId = "created id",
        Name = "submitted-name"
    };

    private static ConsoleConnectionCreateResult RejectedConnection() => new()
    {
        Succeeded = false,
        State = "Rejected",
        Detail = "Original create failed",
        FieldErrors = [new("invalid", "Submitted host was rejected", Path: "host")]
    };
}
