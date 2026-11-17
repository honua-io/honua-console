using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

internal static class ConnectionRenderTestSupport
{
    internal static readonly OperateConnectionSummary Connection = new("connection a", "Production database", "PostGIS", "db.example:5432 / spatial", "reader", "Healthy", "Today", null);

    internal static readonly OperateConnectionSummary SecondConnection = new("second", "Second database", "PostGIS", "second.example", "reader", "Healthy", "Today", null);

    internal sealed class Operations : IConsoleConnectionCreateOperation
    {
        public TaskCompletionSource<ConsoleConnectionProviderAvailability>? Providers { get; init; }
        public TaskCompletionSource<ConsoleConnectionTestOutcome> Test { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ConsoleConnectionTestOutcome> SecondTest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> TestedConnections { get; } = [];
        public TaskCompletionSource<ConsoleConnectionCreateResult> Create { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConsoleConnectionProviderAvailability> GetProviderAvailabilityAsync(CancellationToken cancellationToken = default) => Providers?.Task ?? Task.FromResult(new ConsoleConnectionProviderAvailability());
        public int TestCalls { get; private set; }
        public Task<ConsoleConnectionTestOutcome> TestAsync(ConsoleConnectionCreateCommand command, CancellationToken cancellationToken = default)
        {
            TestCalls++;
            return Test.Task;
        }
        public Task<ConsoleConnectionTestOutcome> TestExistingAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            TestedConnections.Add(connectionId);
            return connectionId == "second" ? SecondTest.Task : Test.Task;
        }
        public List<ConsoleConnectionCreateCommand> CreatedCommands { get; } = [];
        public TaskCompletionSource<ConsoleConnectionCreateResult> SecondCreate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConsoleConnectionCreateResult> CreateAsync(ConsoleConnectionCreateCommand command, CancellationToken cancellationToken = default)
        {
            CreatedCommands.Add(command);
            return CreatedCommands.Count == 1 ? Create.Task : SecondCreate.Task;
        }
    }

    internal static BunitContext Context(bool enabled, string mount = "/")
    {
        var context = new BunitContext();
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager(mount, "operate/connections"));
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new Source());
        context.Services.AddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        return context;
    }

    internal sealed class Source : IOperateTransitionDataSource
    {
        public TaskCompletionSource<OperateTransitionWorkspace>? Load { get; init; }
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Loads { get; private set; }
        public IReadOnlyList<OperateConnectionSummary> Connections { get; init; } = [Connection];
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            Requested.TrySetResult();
            return ++Loads == 1 && Load is { } load
                ? load.Task
                : Task.FromResult(new OperateTransitionWorkspace(Connections, [], [], [], []));
        }
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(connectionId == Connection.Id ? Connection : null);
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }
}
