using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateServiceNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedSettingsFromPreviousServiceCannotReplaceCurrentSettings(bool enabled)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation();
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));

        page.Render(p => p.Add(c => c.ServiceName, "second"));
        page.WaitForAssertion(() => Assert.Equal("second-role", page.Find("[data-allowed-roles]").GetAttribute("value")));
        var renders = page.RenderCount;
        await page.InvokeAsync(() => operation.First.SetResult(Settings("first")));

        page.WaitForState(() => page.RenderCount > renders);
        Assert.Equal("second", page.Find("[data-service-detail]").GetAttribute("data-service-detail"));
        Assert.Equal("second-role", page.Find("[data-allowed-roles]").GetAttribute("value"));
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData(false, "protocols")]
    [InlineData(true, "protocols")]
    [InlineData(false, "access")]
    [InlineData(true, "access")]
    public async Task PreviousServiceSaveCannotChangeCurrentResultOrBusyState(bool enabled, string editor)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation { DelayFirstSettings = false };
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));
        if (enabled) page.Find($"#service-{editor}-tab").Click();
        var firstSave = page.Find($"[data-save-{editor}]").ClickAsync(new());
        Assert.True(page.Find($"[data-save-{editor}]").HasAttribute("disabled"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);

        page.Render(p => p.Add(c => c.ServiceName, "second"));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        var secondSave = page.Find($"[data-save-{editor}]").ClickAsync(new());
        Assert.True(page.Find($"[data-save-{editor}]").HasAttribute("disabled"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new()
        {
            Succeeded = true,
            State = "Updated",
            Detail = "Previous service saved"
        }));
        await firstSave;

        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        Assert.True(page.Find($"[data-save-{editor}]").HasAttribute("disabled"));
        Assert.DoesNotContain("Previous service saved", page.Markup);
        Assert.Equal("second-role", page.Find("[data-allowed-roles]").GetAttribute("value"));
        await page.InvokeAsync(() => operation.SecondSave.SetResult(new()
        {
            Succeeded = true,
            State = "Updated",
            Detail = "Current service saved"
        }));
        await secondSave;
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Contains("Current service saved", page.Markup);
    }

    [Theory]
    [InlineData(false, "protocols")]
    [InlineData(true, "protocols")]
    [InlineData(false, "access")]
    [InlineData(true, "access")]
    public async Task SuccessfulSavePreservesEditsInBothPanels(bool enabled, string editor)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation { DelayFirstSettings = false };
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));
        page.Find("[data-allowed-roles]").Input("unsaved-role");
        page.Find("[data-protocol='Wms']").Change(true);
        if (enabled) page.Find($"#service-{editor}-tab").Click();
        var save = page.Find($"[data-save-{editor}]").ClickAsync(new());

        page.Find("[data-allowed-roles]").Input("newer-role");
        page.Find("[data-protocol='FeatureServer']").Change(false);
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new()
        {
            Succeeded = true,
            State = "Updated"
        }));
        await save;

        Assert.Equal("newer-role", page.Find("[data-allowed-roles]").GetAttribute("value"));
        Assert.False(page.Find("[data-protocol='FeatureServer']").HasAttribute("checked"));
        Assert.True(page.Find("[data-protocol='Wms']").HasAttribute("checked"));
        Assert.Equal(enabled ? 2 : 0,
            context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData(false, "protocols")]
    [InlineData(true, "protocols")]
    [InlineData(false, "access")]
    [InlineData(true, "access")]
    public async Task SettingsReadBackPreservesEditsMadeWhileReading(bool enabled, string editor)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation { DelayFirstSettings = false, DelayRefresh = true };
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));
        if (enabled) page.Find($"#service-{editor}-tab").Click();
        var save = page.Find($"[data-save-{editor}]").ClickAsync(new());
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new() { Succeeded = true, State = "Updated" }));
        await operation.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        page.Find("[data-allow-anonymous]").Change(true);
        page.Find("[data-allow-anonymous-write]").Change(true);
        page.Find("[data-allowed-roles]").Input("draft-reader");
        page.Find("[data-allowed-write-roles]").Input("draft-writer");
        page.Find("[data-protocol='Wms']").Change(true);
        await page.InvokeAsync(() => operation.Refresh.SetResult(Settings("first")));
        await save;

        Assert.True(page.Find("[data-allow-anonymous]").HasAttribute("checked"));
        Assert.True(page.Find("[data-allow-anonymous-write]").HasAttribute("checked"));
        Assert.Equal("draft-reader", page.Find("[data-allowed-roles]").GetAttribute("value"));
        Assert.Equal("draft-writer", page.Find("[data-allowed-write-roles]").GetAttribute("value"));
        Assert.True(page.Find("[data-protocol='Wms']").HasAttribute("checked"));
        Assert.Equal(enabled ? 5 : 0,
            context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessReadBackPreservesRoleTypingBeforeBlur(bool enabled)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation { DelayFirstSettings = false, DelayRefresh = true };
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));
        if (enabled) page.Find("#service-access-tab").Click();
        page.Find("[data-allowed-roles]").Input("reader,writer");
        page.Find("[data-allowed-write-roles]").Input("editor,admin");
        var save = page.Find("[data-save-access]").ClickAsync(new());
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new() { Succeeded = true, State = "Updated" }));
        await operation.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        page.Find("[data-allowed-roles]").Input("reader,writer,newer");
        page.Find("[data-allowed-write-roles]").Input("editor,admin,newer");
        await page.InvokeAsync(() => operation.Refresh.SetResult(Settings("first") with
        {
            AllowedRoles = ["reader", "writer"],
            AllowedWriteRoles = ["editor", "admin"]
        }));
        await save;

        Assert.Equal("reader,writer,newer", page.Find("[data-allowed-roles]").GetAttribute("value"));
        Assert.Equal("editor,admin,newer", page.Find("[data-allowed-write-roles]").GetAttribute("value"));
        Assert.Equal(enabled ? 2 : 0,
            context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData(false, "protocols")]
    [InlineData(true, "protocols")]
    [InlineData(false, "access")]
    [InlineData(true, "access")]
    public async Task ReadBackUpdatesSavedPanelAndPreservesOtherPanelDraft(bool enabled, string editor)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation { DelayFirstSettings = false, DelayRefresh = true };
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));
        page.Find("[data-allowed-roles]").Input("submitted-role");
        page.Find("[data-protocol='Wms']").Change(true);
        if (enabled) page.Find($"#service-{editor}-tab").Click();
        var save = page.Find($"[data-save-{editor}]").ClickAsync(new());
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new() { Succeeded = true, State = "Updated" }));
        await operation.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var persisted = editor == "protocols"
            ? Settings("first") with { EnabledProtocols = ["Wms"] }
            : Settings("first") with { AllowedRoles = ["canonical-role"], AllowAnonymous = true };
        await page.InvokeAsync(() => operation.Refresh.SetResult(persisted));
        await save;

        Assert.Equal(editor == "protocols" ? "submitted-role" : "canonical-role",
            page.Find("[data-allowed-roles]").GetAttribute("value"));
        Assert.Equal(editor == "access", page.Find("[data-allow-anonymous]").HasAttribute("checked"));
        Assert.Equal(editor == "access", page.Find("[data-protocol='FeatureServer']").HasAttribute("checked"));
        Assert.True(page.Find("[data-protocol='Wms']").HasAttribute("checked"));
        Assert.Equal(enabled ? 1 : 0,
            context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData(false, "protocols")]
    [InlineData(true, "protocols")]
    [InlineData(false, "access")]
    [InlineData(true, "access")]
    public async Task BothPanelSavesStayDisabledUntilReadBackCompletes(bool enabled, string editor)
    {
        using var context = Context(enabled);
        var operation = new DelayedSettingsOperation { DelayFirstSettings = false, DelayRefresh = true };
        context.Services.AddSingleton<IServiceConfigurationOperation>(operation);
        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "first"));
        if (enabled) page.Find($"#service-{editor}-tab").Click();
        var save = page.Find($"[data-save-{editor}]").ClickAsync(new());

        Assert.All(page.FindAll("[data-save-protocols], [data-save-access]"),
            button => Assert.True(button.HasAttribute("disabled")));
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new() { Succeeded = true, State = "Updated" }));
        await operation.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(page.FindAll("[data-save-protocols], [data-save-access]"),
            button => Assert.True(button.HasAttribute("disabled")));

        page.Find("[data-protocol='Wms']").Change(true);
        page.Find("[data-allowed-roles]").Input("newer-role");
        await page.InvokeAsync(() => operation.Refresh.SetResult(Settings("first")));
        await save;

        Assert.All(page.FindAll("[data-save-protocols], [data-save-access]"),
            button => Assert.False(button.HasAttribute("disabled")));
        Assert.True(page.Find("[data-protocol='Wms']").HasAttribute("checked"));
        Assert.Equal("newer-role", page.Find("[data-allowed-roles]").GetAttribute("value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceDetailShowsLoadingStateUntilServiceReadCompletes(bool enabled)
    {
        using var context = Context(enabled);
        var pending = new TaskCompletionSource<OperateServicesView>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new PendingServiceDataSource(pending.Task));
        context.Services.AddSingleton<IServiceConfigurationOperation>(new DelayedSettingsOperation { DelayFirstSettings = false });

        var page = context.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, "missing"));

        Assert.Contains("Loading service", page.Markup);
        Assert.DoesNotContain("Service Not Found", page.Markup);

        await page.InvokeAsync(() => pending.SetResult(new OperateServicesView([], [])));

        page.WaitForAssertion(() => Assert.Contains("Service Not Found", page.Markup));
        Assert.DoesNotContain("Loading service", page.Markup);
    }

    private sealed class PendingServiceDataSource(Task<OperateServicesView> pending) : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([], [], [], [], []));
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
        public Task<OperateServicesView> GetServiceViewAsync(string serviceName, CancellationToken cancellationToken = default) =>
            pending;
    }

    private static BunitContext Context(bool enabled)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(
            new("first", "First", "FeatureServer", "Running", "server", [], [], []),
            new("second", "Second", "FeatureServer", "Running", "server", [], [], [])));
        return context;
    }

    private static ServiceSettingsView Settings(string serviceName) => new()
    {
        Bound = true,
        ServiceName = serviceName,
        AvailableProtocols = ["FeatureServer", "Wms"],
        EnabledProtocols = ["FeatureServer"],
        AllowedRoles = [$"{serviceName}-role"]
    };

    private sealed class DelayedSettingsOperation : IServiceConfigurationOperation
    {
        private readonly UnsupportedServiceConfigurationOperation _unsupported = new();
        public bool DelayFirstSettings { get; init; } = true;
        public bool DelayRefresh { get; init; }
        private int _settingsReads;
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceSettingsView> Refresh { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceSettingsView> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceConfigurationResult> FirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceConfigurationResult> SecondSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ServiceSettingsView> GetSettingsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (++_settingsReads > 1 && DelayRefresh)
            {
                RefreshStarted.SetResult();
                return Refresh.Task;
            }
            return serviceName == "first" && DelayFirstSettings ? First.Task : Task.FromResult(Settings(serviceName));
        }
        public Task<ServiceConfigurationResult> SetLayerEnabledAsync(ServiceLayerEnableCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.SetLayerEnabledAsync(command, cancellationToken);
        public Task<ServiceConfigurationResult> UpdateProtocolsAsync(ServiceProtocolsCommand command, CancellationToken cancellationToken = default) =>
            command.ServiceName == "first" ? FirstSave.Task : SecondSave.Task;
        public Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(ServiceAccessPolicyCommand command, CancellationToken cancellationToken = default) =>
            command.ServiceName == "first" ? FirstSave.Task : SecondSave.Task;
        public Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(ServiceMapServerSettingsCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.UpdateMapServerSettingsAsync(command, cancellationToken);
    }
}
