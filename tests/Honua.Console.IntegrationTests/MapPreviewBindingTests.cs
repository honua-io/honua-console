using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed class MapPreviewBindingTests
{
    [Fact]
    public async Task MapPreview_DisposedWhileImporting_DoesNotInitializeMap()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        cut.Render(parameters => parameters.Add(p => p.AriaLabel, "Updated caption"));
        Assert.Equal(1, runtime.Imports);
        Assert.True(runtime.ImportStarted);
        Assert.False(runtime.Import.Task.IsCompleted);

        await ctx.DisposeComponentsAsync();
        var module = new MapModule();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.Import.SetResult(module));
        await module.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, module.Initializations);
        Assert.Equal(1, module.Disposals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void MapPreview_ClearingStyle_RemovesBoundMapAndDisablesZoom(string? missingStyle)
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Setup<bool>("init", _ => true).SetResult(true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));

        cut.Render(parameters => parameters.Add(p => p.StyleUrl, missingStyle));

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Single(module.Invocations["dispose"]);
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.True(button.HasAttribute("disabled")));

        cut.Render(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(2, module.Invocations["init"].Count);
    }

    [Fact]
    public void MapPreview_BoundMap_DropsTheInitialZoomChipButKeepsTheCrs()
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Setup<bool>("init", _ => true).SetResult(true);
        var cut = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.StyleUrl, "/style.json")
            .Add(p => p.Crs, "EPSG:3857")
            .Add(p => p.Zoom, 1));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));

        var scale = cut.Find("[data-map-scale]");
        Assert.Contains("EPSG:3857", scale.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("z 1", scale.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapPreview_ClearingStyleWhileImporting_DoesNotInitializeMap()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        Assert.True(runtime.ImportStarted);
        cut.Render(parameters => parameters.Add(p => p.StyleUrl, (string?)null));

        var module = new MapModule();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.Import.SetResult(module));

        Assert.Equal(0, module.Initializations);
        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));
    }

    [Fact]
    public async Task MapPreview_RerenderingWhileImporting_ImportsAndInitializesOnce()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));
        cut.Render(p => p.Add(x => x.AriaLabel, "Updated caption"));
        cut.Render(p => p.Add(x => x.StyleUrl, "/replacement.json"));
        Assert.Equal(1, runtime.Imports);

        var module = new MapModule();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.Import.SetResult(module));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(1, module.Initializations);
        Assert.Equal("/replacement.json", module.LastStyleUrl);
    }

    [Fact]
    public async Task MapPreview_RerenderingWhileInitializing_KeepsPendingMap()
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        var initialization = module.Setup<bool>("init", _ => true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));

        cut.Render(p => p.Add(x => x.AriaLabel, "Updated caption"));
        Assert.Single(module.Invocations["init"]);
        await ctx.Renderer.Dispatcher.InvokeAsync(() => initialization.SetResult(true));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
    }

    [Fact]
    public async Task MapPreview_ClearingStyleWhileInitializing_DiscardsLateResult()
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        var initialization = module.Setup<bool>("init", _ => true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));

        cut.Render(p => p.Add(x => x.StyleUrl, (string?)null));
        Assert.Single(module.Invocations["dispose"]);
        await ctx.Renderer.Dispatcher.InvokeAsync(() => initialization.SetResult(true));

        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void MapPreview_CanceledImport_KeepsSchematicAndCanRetry()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        runtime.Import.SetCanceled();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));
        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));

        runtime.Import = new();
        var module = new MapModule();
        runtime.Import.SetResult(module);
        cut.Render(p => p.Add(x => x.AriaLabel, "Retry unchanged style"));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(2, runtime.Imports);
        Assert.Equal(1, module.Initializations);
    }

    [Fact]
    public async Task MapPreview_LatestStyleCompletesFirst_OldFailureCannotClearBinding()
    {
        using var ctx = new BunitContext();
        var (cut, completeFirst, completeLatest) = PendingStyleReplacement(ctx);
        await ctx.Renderer.Dispatcher.InvokeAsync(() => completeLatest(true));
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        await ctx.Renderer.Dispatcher.InvokeAsync(() => completeFirst(false));

        Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound"));
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.False(button.HasAttribute("disabled")));
    }

    [Fact]
    public async Task MapPreview_OldStyleCompletesFirst_RemainsUnboundUntilLatestCompletes()
    {
        using var ctx = new BunitContext();
        var (cut, completeFirst, completeLatest) = PendingStyleReplacement(ctx);
        await ctx.Renderer.Dispatcher.InvokeAsync(() => completeFirst(true));
        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));
        await ctx.Renderer.Dispatcher.InvokeAsync(() => completeLatest(true));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.False(button.HasAttribute("disabled")));
    }

    private static (IRenderedComponent<MapPreview> Cut, Action<bool> First, Action<bool> Latest) PendingStyleReplacement(BunitContext ctx)
    {
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        var first = module.Setup<bool>("init", invocation =>
            System.Text.Json.JsonSerializer.SerializeToElement(invocation.Arguments[1])
                .GetProperty("styleUrl").GetString() == "/first.json");
        var latest = module.Setup<bool>("init", invocation =>
            System.Text.Json.JsonSerializer.SerializeToElement(invocation.Arguments[1])
                .GetProperty("styleUrl").GetString() == "/latest.json");
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/first.json"));

        cut.Render(p => p.Add(x => x.StyleUrl, "/latest.json"));

        Assert.Equal(2, module.Invocations["init"].Count);
        Assert.Single(module.Invocations["dispose"]);
        return (cut, value => { first.SetResult(value); }, value => { latest.SetResult(value); });
    }

    [Fact]
    public void MapPreview_FailedImport_CanRetryTheSameStyle()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        runtime.Import.SetException(new JSException("Module unavailable"));
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));

        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.True(button.HasAttribute("disabled")));
        runtime.Import = new();
        var module = new MapModule();
        runtime.Import.SetResult(module);
        cut.Render(p => p.Add(x => x.AriaLabel, "Retry after module recovery"));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(2, runtime.Imports);
        Assert.Equal(1, module.Initializations);
        Assert.Equal("/style.json", module.LastStyleUrl);
    }

    private sealed class DeferredMapRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; set; } = new();
        public int Imports { get; private set; }
        public bool ImportStarted { get; private set; }

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            ImportStarted = true;
            Imports++;
            return (TValue)await Import.Task;
        }

        // This test runtime forwards to its own fake implementation, which must expose failures to the test.
#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016
    }

    private sealed class MapModule : IJSObjectReference
    {
        public int Initializations { get; private set; }
        public string? LastStyleUrl { get; private set; }
        public TaskCompletionSource Disposed { get; } = new();
        public int Disposals { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "init")
            {
                Initializations++;
                LastStyleUrl = System.Text.Json.JsonSerializer.SerializeToElement(args![1]).GetProperty("styleUrl").GetString();
                return ValueTask.FromResult((TValue)(object)true);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        // This test runtime forwards to its own fake implementation, which must expose failures to the test.
#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016

        public ValueTask DisposeAsync()
        {
            Disposals++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
