using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class QuickPublishRequestRenderTests
{
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "conn-b")]
    [InlineData(true, "conn-b")]
    public async Task AuthorityCaptureKeepsTheSubmittedConnectionAndForm(bool enabled, string nextConnection)
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var authority = new DeferredAuthority();
        var publish = new RecordingPublish();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new Connections());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(publish);
        ctx.Services.AddSingleton<IConsoleAuthorityBindingValidator>(authority);
        var cut = ctx.Render<OperatePublishLayerPage>();
        cut.Find("[data-workspace-field='connectionId']").Change("conn-a");
        cut.Find("[data-workspace-field='table']").Change("hydro.streams");
        cut.Find("[data-workspace-field='serviceName']").Input("reviewed-service");
        cut.Find("[data-workspace-field='layerName']").Input("reviewed-layer");

        var pending = cut.InvokeAsync(() => cut.Find("[data-workspace-publish]").ClickAsync(new MouseEventArgs()));
        cut.WaitForAssertion(() => Assert.Equal(1, authority.Captures));
        cut.Find("[data-workspace-field='connectionId']").Change(nextConnection);
        cut.Find("[data-workspace-field='serviceName']").Input("edited-service");
        cut.Find("[data-workspace-field='layerName']").Input("edited-layer");
        authority.Completion.SetResult("reviewed-environment");
        await pending;

        var command = Assert.Single(publish.Commands);
        Assert.Equal("conn-a", command.ConnectionId);
        Assert.Equal("hydro", command.Schema);
        Assert.Equal("streams", command.Table);
        Assert.Equal("reviewed-service", command.ServiceName);
        Assert.Equal("reviewed-layer", command.LayerName);
        Assert.Equal("reviewed-environment", command.AuthorityBinding);
    }

    private sealed class DeferredAuthority : IConsoleAuthorityBindingValidator
    {
        public TaskCompletionSource<string?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Captures { get; private set; }
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Captures++;
            return Completion.Task;
        }
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingPublish : IServiceLayerPublishOperation
    {
        public List<ServiceLayerPublishCommand> Commands { get; } = [];
        public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([new() { Schema = "hydro", Table = "streams", GeometryType = "LineString" }]);
        public Task<ServiceLayerPublishResult> PublishAsync(ServiceLayerPublishCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(new ServiceLayerPublishResult { Succeeded = true, State = "Published" });
        }
    }

    private sealed class Connections : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [new("conn-a", "A", "postgis", "a.db", "svc", "Connected", "now", null),
                 new("conn-b", "B", "postgis", "b.db", "svc", "Connected", "now", null)], [], [], [], []));
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => Task.FromResult<OperateConnectionSummary?>(null);
        public Task<OperateDatasetEditPreview?> FindDatasetEditAsync(string resourceId, CancellationToken cancellationToken = default) => Task.FromResult<OperateDatasetEditPreview?>(null);
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) => Task.FromResult<OperateServiceDetail?>(null);
    }
}
