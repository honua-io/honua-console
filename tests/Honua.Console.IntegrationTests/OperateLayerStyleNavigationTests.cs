using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerStyleNavigationTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task PreviousLayerReadCannotReplaceCurrentLayerEdits(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedOverrides();
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        var page = context.Render<OperateLayerStylePage>(p => p.Add(c => c.ResourceId, "first"));

        page.Render(p => p.Add(c => c.ResourceId, "second"));
        page.WaitForAssertion(() => Assert.Equal("second-popup", page.Find("[data-layer-style-popup]").GetAttribute("value")));
        page.Find("[data-layer-style-popup]").Change("second draft");
        var renders = page.RenderCount;
        await page.InvokeAsync(() => source.FirstRead.SetResult(View("first")));
        page.WaitForState(() => page.RenderCount > renders);

        Assert.Equal("second draft", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Equal("/operate/layers/second", page.Find("a").GetAttribute("href"));
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (presentation == ConsolePresentationMode.V4) Assert.Equal("second", Assert.Single(registry.Current.Entities).ResourceId);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task PreviousLayerSaveCannotChangeCurrentSaveOrBaseline(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedOverrides { DelayFirstRead = false };
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateLayerStylePage>(p => p.Add(c => c.ResourceId, "first"));
        page.Find("[data-layer-style-popup]").Change("first draft");
        var firstSave = page.Find("[data-layer-style-save]").ClickAsync(new());
        Assert.True(page.Find("[data-layer-style-save]").HasAttribute("disabled"));

        page.Render(p => p.Add(c => c.ResourceId, "second"));
        Assert.False(page.Find("[data-layer-style-save]").HasAttribute("disabled"));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        page.Find("[data-layer-style-popup]").Change("second draft");
        var secondSave = page.Find("[data-layer-style-save]").ClickAsync(new());
        await page.InvokeAsync(() => source.FirstSave.SetResult(new(true)));
        await firstSave;

        Assert.True(page.Find("[data-layer-style-save]").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-layer-style-saved]"));
        Assert.Equal("second draft", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        await page.InvokeAsync(() => source.SecondSave.SetResult(new(true)));
        await secondSave;

        Assert.Single(page.FindAll("[data-layer-style-saved]"));
        Assert.False(page.Find("[data-layer-style-save]").HasAttribute("disabled"));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Collection(source.Edits,
            edit => Assert.Equal(new OperateLayerStyleOverrideEdit("first", "shared", PopupInfoJson: "first draft", DrawingInfoJson: ""), edit),
            edit => Assert.Equal(new OperateLayerStyleOverrideEdit("second", "shared", PopupInfoJson: "second draft", DrawingInfoJson: ""), edit));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void SameLayerParametersPreserveBothDraftsAndDirtyContext(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedOverrides { DelayFirstRead = false };
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        var page = context.Render<OperateLayerStylePage>(p => p.Add(c => c.ResourceId, "first"));
        page.Find("[data-layer-style-popup]").Change("popup draft");
        page.Find("[data-layer-style-drawing]").Change("drawing draft");

        page.Render(p => p.Add(c => c.ResourceId, "first"));

        Assert.Equal("popup draft", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Equal("drawing draft", page.Find("[data-layer-style-drawing]").GetAttribute("value"));
        Assert.Equal(["first"], source.Reads);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 2 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task SameLayerParametersDoNotRestartPendingRead(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedOverrides();
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        var page = context.Render<OperateLayerStylePage>(p => p.Add(c => c.ResourceId, "first"));

        page.Render(p => p.Add(c => c.ResourceId, "first"));
        Assert.Equal(["first"], source.Reads);
        await page.InvokeAsync(() => source.FirstRead.SetResult(View("first")));
        page.WaitForAssertion(() => Assert.Equal("first-popup", page.Find("[data-layer-style-popup]").GetAttribute("value")));
        Assert.Empty(page.FindAll("[data-layer-style-saved]"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task SameLayerParametersPreserveNewerEditsDuringSave(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedOverrides { DelayFirstRead = false };
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        var page = context.Render<OperateLayerStylePage>(p => p.Add(c => c.ResourceId, "first"));
        page.Find("[data-layer-style-popup]").Change("submitted popup");
        var save = page.Find("[data-layer-style-save]").ClickAsync(new());
        page.Find("[data-layer-style-popup]").Change("newer popup");
        page.Find("[data-layer-style-drawing]").Change("newer drawing");

        page.Render(p => p.Add(c => c.ResourceId, "first"));
        Assert.True(page.Find("[data-layer-style-save]").HasAttribute("disabled"));
        Assert.Equal("newer popup", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Equal("newer drawing", page.Find("[data-layer-style-drawing]").GetAttribute("value"));
        await page.InvokeAsync(() => source.FirstSave.SetResult(new(true)));
        await save;

        Assert.Equal("newer popup", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Equal("newer drawing", page.Find("[data-layer-style-drawing]").GetAttribute("value"));
        Assert.Single(page.FindAll("[data-layer-style-saved]"));
        Assert.Equal(["first"], source.Reads);
        Assert.Equal("submitted popup", Assert.Single(source.Edits).PopupInfoJson);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 2 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    private static BunitContext Context(ConsolePresentationMode presentation)
    {
        var context = new BunitContext();
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        return context;
    }

    private static OperateLayerStyleOverrideView View(string resourceId) => new(resourceId,
        [new("shared", resourceId, resourceId, PopupInfoJson: $"{resourceId}-popup")])
    {
        LayerId = resourceId == "first" ? 1 : 2,
        CanonicalResourceId = resourceId
    };

    private sealed class DelayedOverrides : IOperateLayerStyleOverrideDataSource
    {
        public bool DelayFirstRead { get; init; } = true;
        public List<string> Reads { get; } = [];
        public List<OperateLayerStyleOverrideEdit> Edits { get; } = [];
        public TaskCompletionSource<OperateLayerStyleOverrideView> FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperateLayerStyleOverrideSaveResult> FirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperateLayerStyleOverrideSaveResult> SecondSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperateLayerStyleOverrideView> GetOverridesAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            Reads.Add(resourceId);
            return resourceId == "first" && DelayFirstRead ? FirstRead.Task : Task.FromResult(View(resourceId));
        }
        public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(OperateLayerStyleOverrideEdit edit, CancellationToken cancellationToken = default)
        {
            Edits.Add(edit);
            return edit.ResourceId == "first" ? FirstSave.Task : SecondSave.Task;
        }
    }
}
