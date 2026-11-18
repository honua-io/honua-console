using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Honua.Console.IntegrationTests;

public sealed class RetryingModuleImportTests
{
    [Fact]
    public async Task TimedOutImport_CanBeRetriedWithAFreshSpecifier()
    {
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true };
        var session = new RetryingModuleImport(ModuleImportScriptError.Stop, TimeSpan.FromMilliseconds(40));

        var module = await session.LoadAsync(runtime, "./tabs.js", CancellationToken.None);

        Assert.NotNull(module);
        Assert.Equal("./tabs.js", runtime.Specifiers[0]);
        Assert.StartsWith("./tabs.js?honuaRetry=", runtime.Specifiers[1], StringComparison.Ordinal);
        Assert.Equal(2, runtime.Specifiers.Count);
    }

    [Fact]
    public async Task TimedOutImport_StopsAfterThreeAttempts()
    {
        var runtime = new ScriptRuntime();
        var session = new RetryingModuleImport(ModuleImportScriptError.Stop, TimeSpan.FromMilliseconds(40));

        var module = await session.LoadAsync(runtime, "./tabs.js", CancellationToken.None);

        Assert.Null(module);
        Assert.Equal(3, runtime.Specifiers.Count);
        Assert.Equal("./tabs.js", runtime.Specifiers[0]);
        Assert.StartsWith("./tabs.js?honuaRetry=", runtime.Specifiers[1], StringComparison.Ordinal);
        Assert.StartsWith("./tabs.js?honuaRetry=", runtime.Specifiers[2], StringComparison.Ordinal);
        Assert.NotEqual(runtime.Specifiers[1], runtime.Specifiers[2]);
    }

    [Fact]
    public async Task ScriptError_RetriesOnlyWhenTheCallerAsks()
    {
        var stopRuntime = new ScriptRuntime { FailFirstWithScriptError = true };
        var stop = new RetryingModuleImport(ModuleImportScriptError.Stop, TimeSpan.FromMilliseconds(40));
        Assert.Null(await stop.LoadAsync(stopRuntime, "./tabs.js", CancellationToken.None));
        Assert.Equal(["./tabs.js"], stopRuntime.Specifiers);

        var retryRuntime = new ScriptRuntime { FailFirstWithScriptError = true, SecondAttemptSucceeds = true };
        var retry = new RetryingModuleImport(ModuleImportScriptError.Retry, TimeSpan.FromMilliseconds(40));
        Assert.NotNull(await retry.LoadAsync(retryRuntime, "./tabs.js", CancellationToken.None));
        Assert.Equal("./tabs.js", retryRuntime.Specifiers[0]);
        Assert.StartsWith("./tabs.js?honuaRetry=", retryRuntime.Specifiers[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledImport_DoesNotReturnAModule()
    {
        var runtime = new ScriptRuntime();
        var session = new RetryingModuleImport(ModuleImportScriptError.Stop, TimeSpan.FromSeconds(2));
        using var lifetime = new CancellationTokenSource();
        var importing = session.LoadAsync(runtime, "./tabs.js", lifetime.Token);
        lifetime.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await importing);
        Assert.Equal(["./tabs.js"], runtime.Specifiers);
    }

    [Fact]
    public async Task ConfirmDialog_ScriptError_RetriesAndShows()
    {
        using var ctx = new BunitContext();
        // The hung-import timer is covered on the map and by TimedOutImport. Here the
        // first import fails immediately so the dialog loop is what is under test.
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true, FailFirstWithScriptError = true };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ConsoleConfirmDialog>();
        Assert.Empty(cut.FindAll("[data-console-confirm]"));
        Assert.Empty(runtime.Specifiers);

        await cut.InvokeAsync(() => cut.Instance.Open("Delete this item?", () => Task.CompletedTask));

        Assert.Equal(1, runtime.ShowCalls);
        Assert.Equal("./_content/Honua.Console.Shell/console-confirm-dialog.js", runtime.Specifiers[0]);
        Assert.StartsWith(
            "./_content/Honua.Console.Shell/console-confirm-dialog.js?honuaRetry=",
            runtime.Specifiers[1],
            StringComparison.Ordinal);
        Assert.Equal("Delete this item?", cut.Find(".console-confirm-title").TextContent);
    }

    [Fact]
    public void Tabs_ScriptError_RetriesAttachOnTheSamePage()
    {
        using var ctx = new BunitContext();
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true, FailFirstWithScriptError = true };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ConsoleTabs>(parameters => parameters
            .Add(component => component.Tabs, [new Honua.Console.Shell.Models.ConsoleTab("layers-tab", "Layers", "layers-panel")])
            .Add(component => component.SelectedId, "layers-tab")
            .Add(component => component.Label, "Service"));

        Assert.Equal("Service", cut.Find("[role='tablist']").GetAttribute("aria-label"));
        Assert.Equal(1, runtime.AttachCalls);
        Assert.Equal("./_content/Honua.Console.Shell/console-tabs.js", runtime.Specifiers[0]);
        Assert.StartsWith(
            "./_content/Honua.Console.Shell/console-tabs.js?honuaRetry=",
            runtime.Specifiers[1],
            StringComparison.Ordinal);
    }

    private sealed class ScriptRuntime : IJSRuntime
    {
        private readonly ScriptModule _module = new();
        public List<string> Specifiers { get; } = [];
        public bool SecondAttemptSucceeds { get; init; }
        public bool TimeoutInsteadOfHang { get; init; }
        public bool FailFirstWithScriptError { get; init; }

        public int ShowCalls => _module.Calls.Count(static call => call == "show");

        public int AttachCalls => _module.Calls.Count(static call => call == "attach");

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier != "import")
            {
                _module.Calls.Add(identifier);
                return default!;
            }

            var specifier = args is { Length: > 0 } && args[0] is string text ? text : "";
            Specifiers.Add(specifier);
            if (SecondAttemptSucceeds && specifier.Contains("honuaRetry=", StringComparison.Ordinal))
            {
                return (TValue)(object)_module;
            }

            if (FailFirstWithScriptError)
            {
                throw new JSException("simulated script error");
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
