using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Honua.Console.IntegrationTests;

public sealed class RetryingModuleImportTests
{
    [Fact]
    public void Specifier_KeepsTheFirstPathAndBustsTheBrowserEntryAfterThat()
    {
        const string path = "./_content/Honua.Console.Shell/console-tabs.js";
        Assert.Equal(path, RetryingModuleImport.Specifier(path, 1));
        Assert.Equal(path + "?honuaRetry=2", RetryingModuleImport.Specifier(path, 2));
        Assert.Equal(path + "?honuaRetry=3", RetryingModuleImport.Specifier(path, 3));
    }

    [Fact]
    public async Task TimedOutImport_CanBeRetriedWithAFreshSpecifier()
    {
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true };
        using var lifetime = new CancellationTokenSource();

        await Assert.ThrowsAsync<TimeoutException>(() => RetryingModuleImport.ImportAsync(
            runtime, "./tabs.js", 1, lifetime.Token, TimeSpan.FromMilliseconds(40)));
        var module = await RetryingModuleImport.ImportAsync(
            runtime, "./tabs.js", 2, lifetime.Token, TimeSpan.FromMilliseconds(40));

        Assert.NotNull(module);
        Assert.Equal(["./tabs.js", "./tabs.js?honuaRetry=2"], runtime.Specifiers);
    }

    [Fact]
    public async Task CancelledImport_DoesNotReturnAModule()
    {
        var runtime = new ScriptRuntime();
        using var lifetime = new CancellationTokenSource();
        var importing = RetryingModuleImport.ImportAsync(
            runtime, "./tabs.js", 1, lifetime.Token, TimeSpan.FromSeconds(2));
        lifetime.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await importing);
        Assert.Equal(["./tabs.js"], runtime.Specifiers);
    }

    [Fact]
    public async Task ConfirmDialog_HungScript_RetriesAndShows()
    {
        using var ctx = new BunitContext();
        // The hung-import timer is covered on the map and by TimedOutImport. Here the
        // first import fails immediately so the dialog loop is what is under test.
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true, TimeoutInsteadOfHang = true };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ConsoleConfirmDialog>();
        Assert.Empty(cut.FindAll("[data-console-confirm]"));

        await cut.InvokeAsync(() => cut.Instance.Open("Delete this item?", () => Task.CompletedTask));

        Assert.Equal(1, runtime.CallCount("show"));
        Assert.Equal(
            "./_content/Honua.Console.Shell/console-confirm-dialog.js?honuaRetry=2",
            runtime.Specifiers[1]);
        Assert.Equal("Delete this item?", cut.Find(".console-confirm-title").TextContent);
    }

    [Fact]
    public void Tabs_HungScript_RetriesAttachOnTheSamePage()
    {
        using var ctx = new BunitContext();
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true, TimeoutInsteadOfHang = true };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ConsoleTabs>(parameters => parameters
            .Add(component => component.Tabs, [new Honua.Console.Shell.Models.ConsoleTab("layers-tab", "Layers", "layers-panel")])
            .Add(component => component.SelectedId, "layers-tab")
            .Add(component => component.Label, "Service"));

        Assert.Equal("Service", cut.Find("[role='tablist']").GetAttribute("aria-label"));
        Assert.Equal(1, runtime.CallCount("attach"));
        Assert.Equal(
            "./_content/Honua.Console.Shell/console-tabs.js?honuaRetry=2",
            runtime.Specifiers[1]);
    }

    private sealed class ScriptRuntime : IJSRuntime
    {
        private readonly ScriptModule _module = new();
        public List<string> Specifiers { get; } = [];
        public bool SecondAttemptSucceeds { get; init; }
        public bool TimeoutInsteadOfHang { get; init; }

        public int CallCount(string name) => _module.Calls.Count(call => call == name);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier != "import")
            {
                return _module.Invoke<TValue>(identifier);
            }

            var specifier = args is { Length: > 0 } && args[0] is string text ? text : "";
            Specifiers.Add(specifier);
            if (SecondAttemptSucceeds && specifier.Contains("honuaRetry=", StringComparison.Ordinal))
            {
                return (TValue)(object)_module;
            }

            if (TimeoutInsteadOfHang)
            {
                throw new TimeoutException("simulated import timeout");
            }

            // Same shape as the map-preview double: an async method waiting on a task
            // that never completes. The helper's own timeout is what resumes the retry.
            var hung = new TaskCompletionSource<TValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            return await hung.Task;
        }

#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016
    }

    private sealed class ScriptModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public TValue Invoke<TValue>(string identifier)
        {
            Calls.Add(identifier);
            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
