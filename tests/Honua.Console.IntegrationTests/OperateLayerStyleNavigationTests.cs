using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerStyleNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviousLayerReadCannotReplaceCurrentLayerEdits(bool enabled)
    {
        using var context = Context(enabled);
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
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (enabled) Assert.Equal("second", Assert.Single(registry.Current.Entities).ResourceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviousLayerSaveCannotChangeCurrentSaveOrBaseline(bool enabled)
    {
        using var context = Context(enabled);
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
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
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

    private static BunitContext Context(bool enabled)
    {
        var context = new BunitContext();
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
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
        public List<OperateLayerStyleOverrideEdit> Edits { get; } = [];
        public TaskCompletionSource<OperateLayerStyleOverrideView> FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperateLayerStyleOverrideSaveResult> FirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperateLayerStyleOverrideSaveResult> SecondSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperateLayerStyleOverrideView> GetOverridesAsync(string resourceId, CancellationToken cancellationToken = default) =>
            resourceId == "first" && DelayFirstRead ? FirstRead.Task : Task.FromResult(View(resourceId));
        public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(OperateLayerStyleOverrideEdit edit, CancellationToken cancellationToken = default)
        {
            Edits.Add(edit);
            return edit.ResourceId == "first" ? FirstSave.Task : SecondSave.Task;
        }
    }
}
