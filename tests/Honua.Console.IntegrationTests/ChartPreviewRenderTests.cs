using Bunit;
using Honua.Console.Shell.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Honua.Console.IntegrationTests;

public sealed class ChartPreviewRenderTests
{
    [Fact]
    public void ChartPreview_WithoutSpec_RendersSchematicPlaceholderUnbound()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<ChartPreview>(parameters => parameters
            .Add(p => p.Title, "Features by name"));

        // No-binding placeholder is present and no live chart is bound.
        Assert.Contains("chart-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-chart-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
        // The schematic draws bars so the empty state reads as a chart, not a blank box.
        Assert.Contains("<rect", cut.Markup, StringComparison.Ordinal);
        // Optional caption renders.
        Assert.Contains("Features by name", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void ClearingSpec_RemovesBoundChartAndAllowsRebinding(string? missingSpec)
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("init", _ => true).SetResult(true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<ChartPreview>(p => p.Add(x => x.Spec, Spec));
        cut.WaitForAssertion(() => AssertBound(cut, "true"));

        cut.Render(p => p.Add(x => x.Spec, missingSpec));

        cut.WaitForAssertion(() => AssertBound(cut, "false"));
        Assert.Single(module.Invocations["dispose"]);
        cut.Render(p => p.Add(x => x.Spec, Spec));
        cut.WaitForAssertion(() => AssertBound(cut, "true"));
        Assert.Equal(2, module.Invocations["init"].Count);
    }

    [Theory]
    [InlineData("/rows/b")]
    [InlineData(null)]
    public void ChangingFeaturesUrl_RebindsUnchangedSpec(string? nextUrl)
    {
        using var ctx = new BunitContext();
        var runtime = new ChartRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ChartPreview>(p => p.Add(x => x.Spec, Spec).Add(x => x.FeaturesUrl, "/rows/a"));
        cut.WaitForAssertion(() => AssertBound(cut, "true"));

        cut.Render(p => p.Add(x => x.FeaturesUrl, nextUrl));

        cut.WaitForAssertion(() => Assert.Equal(2, runtime.Module.Requests.Count));
        Assert.Equal(nextUrl, runtime.Module.Requests[1].GetProperty("featuresUrl").GetString());
        Assert.Equal(Spec, runtime.Module.Requests[1].GetProperty("spec").GetString());
        Assert.Equal(1, runtime.Module.Clears);
        cut.Render(p => p.Add(x => x.Title, "Caption only"));
        Assert.Equal(2, runtime.Module.Requests.Count);
    }

    [Fact]
    public async Task DisposingWhileImporting_ReleasesModuleWithoutInitializing()
    {
        using var ctx = new BunitContext();
        var runtime = new ChartRuntime { PendingImport = new() };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        ctx.Render<ChartPreview>(p => p.Add(x => x.Spec, Spec));
        Assert.Equal(1, runtime.Imports);

        await ctx.DisposeComponentsAsync();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.PendingImport.SetResult(runtime.Module));
        await runtime.Module.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(runtime.Module.Requests);
    }

    [Fact]
    public async Task ClearingWhileImporting_DoesNotInitialize()
    {
        using var ctx = new BunitContext();
        var runtime = new ChartRuntime { PendingImport = new() };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ChartPreview>(p => p.Add(x => x.Spec, Spec));
        cut.Render(p => p.Add(x => x.Spec, (string?)null));

        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.PendingImport.SetResult(runtime.Module));

        Assert.Empty(runtime.Module.Requests);
        AssertBound(cut, "false");
    }

    [Fact]
    public async Task ClearingWhileInitializing_DoesNotRestoreBoundState()
    {
        using var ctx = new BunitContext();
        var runtime = new ChartRuntime();
        var pending = new TaskCompletionSource<bool>();
        runtime.Module.NextInitialization = pending;
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ChartPreview>(p => p.Add(x => x.Spec, Spec));
        Assert.Single(runtime.Module.Requests);

        cut.Render(p => p.Add(x => x.Spec, (string?)null));
        await ctx.Renderer.Dispatcher.InvokeAsync(() => pending.SetResult(true));

        AssertBound(cut, "false");
        Assert.Equal(1, runtime.Module.Clears);
    }

    [Fact]
    public async Task ReplacingWhileInitializing_KeepsNewestBinding()
    {
        using var ctx = new BunitContext();
        var runtime = new ChartRuntime();
        var pending = new TaskCompletionSource<bool>();
        runtime.Module.NextInitialization = pending;
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ChartPreview>(p => p.Add(x => x.Spec, Spec).Add(x => x.FeaturesUrl, "/rows/a"));
        cut.Render(p => p.Add(x => x.FeaturesUrl, "/rows/b"));
        cut.WaitForAssertion(() => AssertBound(cut, "true"));

        await ctx.Renderer.Dispatcher.InvokeAsync(() => pending.SetResult(true));
        cut.Render(p => p.Add(x => x.Title, "Keep current rows"));

        AssertBound(cut, "true");
        Assert.Equal(2, runtime.Module.Requests.Count);
        Assert.Equal("/rows/b", runtime.Module.Requests[1].GetProperty("featuresUrl").GetString());
    }

    private const string ModulePath = "./_content/Honua.Console.Shell/chart-preview.js";
    private const string Spec = "{\"mark\":\"bar\",\"data\":{\"values\":[]}}";

    private static void AssertBound(IRenderedComponent<ChartPreview> cut, string expected) =>
        Assert.Equal(expected, cut.Find("figure").GetAttribute("data-chart-bound"));

    private sealed class ChartRuntime : IJSRuntime
    {
        public ChartModule Module { get; } = new();
        public TaskCompletionSource<IJSObjectReference>? PendingImport { get; init; }
        public int Imports { get; private set; }

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("import", identifier);
            Imports++;
            var module = PendingImport is null ? Module : await PendingImport.Task;
            return (TValue)(object)module;
        }

#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016
    }

    private sealed class ChartModule : IJSObjectReference
    {
        public List<JsonElement> Requests { get; } = [];
        public int Clears { get; private set; }
        public TaskCompletionSource<bool>? NextInitialization { get; set; }
        public TaskCompletionSource Disposed { get; } = new();

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "init")
            {
                Requests.Add(JsonSerializer.SerializeToElement(args![1]));
                var pending = NextInitialization;
                NextInitialization = null;
                var bound = pending is null || await pending.Task;
                return (TValue)(object)bound;
            }

            Assert.Equal("dispose", identifier);
            Clears++;
            return default!;
        }

#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

}
