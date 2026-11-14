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
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);

        page.Render(p => p.Add(c => c.ServiceName, "second"));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        var secondSave = page.Find($"[data-save-{editor}]").ClickAsync(new());
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        await page.InvokeAsync(() => operation.FirstSave.SetResult(new()
        {
            Succeeded = true,
            State = "Updated",
            Detail = "Previous service saved"
        }));
        await firstSave;

        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
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
        AvailableProtocols = ["FeatureServer"],
        EnabledProtocols = ["FeatureServer"],
        AllowedRoles = [$"{serviceName}-role"]
    };

    private sealed class DelayedSettingsOperation : IServiceConfigurationOperation
    {
        private readonly UnsupportedServiceConfigurationOperation _unsupported = new();
        public bool DelayFirstSettings { get; init; } = true;
        public TaskCompletionSource<ServiceSettingsView> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceConfigurationResult> FirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceConfigurationResult> SecondSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ServiceSettingsView> GetSettingsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            serviceName == "first" && DelayFirstSettings ? First.Task : Task.FromResult(Settings(serviceName));
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
