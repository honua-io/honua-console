using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Honua.Console.IntegrationTests;

public sealed class StudioEntryNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportCompletingAfterDisposalIsReleasedWithoutCheckingAnotherEditor(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        using var context = Context(runtime, navigation, v4);
        var page = context.Render<StudioPage>();
        var leaving = context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask());
        Assert.Equal(1, runtime.Imports);
        await context.DisposeComponentsAsync();
        runtime.Import.SetResult(runtime.Module);
        await leaving.WaitAsync(TimeSpan.FromSeconds(5));
        page.WaitForAssertion(() => Assert.Equal(1, runtime.Module.Disposals));
        Assert.Equal(0, runtime.Module.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededNavigationSharesImportAndOnlyCurrentAttemptChecksEditor(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        using var context = Context(runtime, navigation, v4);
        var page = context.Render<StudioPage>();
        var first = context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask());
        var second = context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/activity").AsTask());
        runtime.Import.SetResult(runtime.Module);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await first);
        Assert.True(await second);
        Assert.Equal(1, runtime.Imports);
        Assert.Equal(1, runtime.Module.Checks);
        await context.DisposeComponentsAsync();
        Assert.Equal(1, runtime.Module.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveEditorRefusalPreventsNavigation(bool v4)
    {
        var runtime = new Runtime();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.Allowed = false;
        using var context = Context(runtime, new Navigation(), v4);
        context.Render<StudioPage>();
        var navigation = context.Services.GetRequiredService<NavigationManager>() as Navigation;
        Assert.False(await context.Renderer.Dispatcher.InvokeAsync(() => navigation!.RequestAsync("/catalog").AsTask()));
        Assert.Equal(1, runtime.Module.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedImportPreventsNavigationAndNextAttemptRetries(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        runtime.Import.SetException(new JSException("Unavailable"));
        using var context = Context(runtime, navigation, v4);
        context.Render<StudioPage>();
        Assert.False(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        runtime.Import = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Import.SetResult(runtime.Module);
        Assert.True(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        Assert.Equal(2, runtime.Imports);
        Assert.Equal(1, runtime.Module.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededEditorCheckReceivesCancellationAndCannotBlockCurrentAttempt(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.PendingCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = Context(runtime, navigation, v4);
        context.Render<StudioPage>();
        var first = context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask());
        var oldToken = runtime.Module.LastToken;
        runtime.Module.PendingCheck = null;
        var second = context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/activity").AsTask());
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(runtime.Module.LastToken.IsCancellationRequested);
        Assert.Equal(2, runtime.Module.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsPendingEditorCheckAndReleasesModuleOnce(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.PendingCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = Context(runtime, navigation, v4);
        context.Render<StudioPage>();
        var leaving = context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask());
        var token = runtime.Module.LastToken;
        await context.DisposeComponentsAsync();
        await leaving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(1, runtime.Module.Disposals);
        Assert.True(await navigation.RequestAsync("/activity"));
        Assert.Equal(1, runtime.Module.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveInteropFailurePreventsNavigationWithoutReimportingModule(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.Failure = new JSException("Unavailable");
        using var context = Context(runtime, navigation, v4);
        context.Render<StudioPage>();
        Assert.False(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        runtime.Module.Failure = null;
        Assert.True(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        Assert.Equal(1, runtime.Imports);
        Assert.Equal(2, runtime.Module.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringImportCanBeRetried(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        runtime.Import.SetCanceled();
        using var context = Context(runtime, navigation, v4);
        context.Render<StudioPage>();
        Assert.False(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        runtime.Import = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Import.SetResult(runtime.Module);
        Assert.True(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/activity").AsTask()));
        Assert.Equal(2, runtime.Imports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedNavigationReusesModuleAndDisposalToleratesDisconnectedRuntime(bool v4)
    {
        var runtime = new Runtime();
        var navigation = new Navigation();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.DisposalFailure = new JSDisconnectedException("Disconnected");
        using var context = Context(runtime, navigation, v4);
        context.Render<StudioPage>();
        Assert.True(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        Assert.True(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/activity").AsTask()));
        await context.DisposeComponentsAsync();
        Assert.Equal(1, runtime.Imports);
        Assert.Equal(2, runtime.Module.Checks);
        Assert.Equal(1, runtime.Module.Disposals);
    }

    [Theory]
    [InlineData(false, "/studio/proof", true)]
    [InlineData(true, "/studio/proof", true)]
    [InlineData(false, "/studio", false)]
    [InlineData(true, "/studio", false)]
    public async Task RetiredOrUnavailableEditorDoesNotRegisterAnEditorCheck(bool v4, string path, bool embed)
    {
        var runtime = new Runtime();
        var navigation = new Navigation(path);
        using var context = Context(runtime, navigation, v4);
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = embed.ToString() }).Build());
        context.Render<StudioPage>();
        Assert.True(await context.Renderer.Dispatcher.InvokeAsync(() => navigation.RequestAsync("/catalog").AsTask()));
        Assert.Equal(0, runtime.Imports);
        Assert.Equal(0, runtime.Module.Checks);
    }

    private static BunitContext Context(Runtime runtime, Navigation navigation, bool v4)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IJSRuntime>(runtime);
        context.Services.AddSingleton<NavigationManager>(navigation);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        return context;
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string path = "/studio") => Initialize("http://localhost/", $"http://localhost{path}");
        public ValueTask<bool> RequestAsync(string path) => NotifyLocationChangingAsync(ToAbsoluteUri(path).ToString(), null, false);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
        protected override void SetNavigationLockState(bool value) { }
    }

    // These interop doubles deliberately propagate failures to the page under test.
#pragma warning disable BL0016
    private sealed class Runtime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Module Module { get; } = new();
        public int Imports { get; private set; }
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("import", identifier);
            Assert.Equal("./js/style-navigation.mjs", Assert.Single(args!));
            Imports++;
            return (TValue)(object)await Import.Task;
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class Module : IJSObjectReference
    {
        public int Checks { get; private set; }
        public int Disposals { get; private set; }
        public bool Allowed { get; set; } = true;
        public Exception? Failure { get; set; }
        public Exception? DisposalFailure { get; set; }
        public TaskCompletionSource<bool>? PendingCheck { get; set; }
        public CancellationToken LastToken { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("mayLeaveCurrentStyleEditor", identifier);
            Checks++;
            LastToken = cancellationToken;
            if (Failure is not null) throw Failure;
            var allowed = PendingCheck is null ? Allowed : await PendingCheck.Task.WaitAsync(cancellationToken);
            return (TValue)(object)allowed;
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (DisposalFailure is not null) throw DisposalFailure;
            return ValueTask.CompletedTask;
        }
    }
#pragma warning restore BL0016
}
