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
    public async Task ConfirmDialog_HungScript_RetriesAndShows()
    {
        using var ctx = new BunitContext();
        // The hung-import timer is covered on the map and by TimedOutImport. Here the
        // first import fails immediately so the dialog loop is what is under test.
        var runtime = new ScriptRuntime { SecondAttemptSucceeds = true, TimeoutInsteadOfHang = true };
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<ConsoleConfirmDialog>();
        Assert.Empty(cut.FindAll("[data-console-confirm]"));
        Assert.Empty(runtime.Specifiers);

        await cut.InvokeAsync(() => cut.Instance.Open("Delete this item?", () => Task.CompletedTask));

        Assert.Equal(1, runtime.CallCount("show"));
        Assert.Equal("./_content/Honua.Console.Shell/console-confirm-dialog.js", runtime.Specifiers[0]);
        Assert.StartsWith(
            "./_content/Honua.Console.Shell/console-confirm-dialog.js?honuaRetry=",
            runtime.Specifiers[1],
            StringComparison.Ordinal);
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
