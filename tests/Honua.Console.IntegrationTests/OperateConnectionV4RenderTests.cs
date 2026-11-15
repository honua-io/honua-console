using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

public sealed class OperateConnectionV4RenderTests
{
    private static readonly OperateConnectionSummary Connection = new("connection a", "Production database", "PostGIS", "db.example:5432 / spatial", "reader", "Healthy", "Today", null);

    private static readonly OperateConnectionSummary SecondConnection = new("second", "Second database", "PostGIS", "second.example", "reader", "Healthy", "Today", null);

    [Theory]
    [InlineData(false, "/")]
    [InlineData(true, "/")]
    [InlineData(false, "/console/")]
    [InlineData(true, "/console/")]
    public void InventoryUsesStableEncodedConnectionLinks(bool enabled, string mount)
    {
        using var context = Context(enabled, mount);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateConnectionsPage>();
        var link = page.Find("tbody a");
        Assert.Equal(mount + "operate/connections/connection%20a", link.GetAttribute("href"));
        Assert.Equal("Production database", link.TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        Assert.Equal(enabled ? "/operate/connections" : "/", registry.Current.Route);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Entities);
            Assert.Equal(ConsoleAgentEntityKind.Connection, entity.Kind);
            Assert.Equal(Connection.Id, entity.Id);
            Assert.Equal("/operate/connections/connection%20a", ConsoleAgentRoutes.ForEntity(entity));
            if (mount == "/console/") CatalogAdminPreview.Capture("data-connections", page.Markup);
        }
    }

    [Theory]
    [InlineData(false, "/")]
    [InlineData(true, "/")]
    [InlineData(false, "/console/")]
    [InlineData(true, "/console/")]
    public void DetailDeepLinkSelectsStableEntityAndRetainsTesting(bool enabled, string mount)
    {
        using var context = Context(enabled, mount);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("operate/connections/connection%20a");
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateConnectionDetailPage>(parameters => parameters.Add(p => p.ConnectionId, Connection.Id));
        Assert.Equal(Connection.Name, page.Find("h1").TextContent);
        Assert.Equal(enabled ? 2 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Entities);
            Assert.Equal(Connection.Id, entity.Id);
            Assert.Equal("/operate/connections/connection%20a", registry.Current.Route);
            Assert.Equal(registry.Current.Route, ConsoleAgentRoutes.ForEntity(entity));
            if (mount == "/console/") CatalogAdminPreview.Capture("data-connection-detail", page.Markup);
        }
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("Connection test", page.Markup));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (enabled && mount == "/console/") CatalogAdminPreview.Capture("data-connection-detail-tested", page.Markup);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/console/")]
    public void DiagnosticsContextPreservesDeepLinkRoute(string mount)
    {
        using var context = Context(true, mount);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("operate/connections/connection%20a/diagnostics");
        context.Render<OperateConnectionDetailPage>(parameters => parameters.Add(p => p.ConnectionId, Connection.Id));
        Assert.Equal("/operate/connections/connection%20a/diagnostics", context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryAndDetailExposePendingLoad(bool detail)
    {
        using var context = Context(true);
        var source = new Source { Load = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        context.Services.AddSingleton<IOperateTransitionDataSource>(source);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        if (detail)
        {
            var page = context.Render<OperateConnectionDetailPage>(p => p.Add(c => c.ConnectionId, Connection.Id));
            Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
            source.Load.SetResult(new([Connection], [], [], [], []));
            page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        }
        else
        {
            var page = context.Render<OperateConnectionsPage>();
            Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
            source.Load.SetResult(new([Connection], [], [], [], []));
            page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        }
    }

    [Fact]
    public void DetailPublishesPendingTestUntilCompletion()
    {
        using var context = Context(true);
        var ops = new Operations();
        context.Services.AddSingleton<IConsoleConnectionCreateOperation>(ops);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateConnectionDetailPage>(p => p.Add(c => c.ConnectionId, Connection.Id));
        page.Find("button").Click();
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        ops.Test.SetResult(ConsoleConnectionTestOutcome.MissingBinding("Fixture"));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
    }

    [Fact]
    public async Task NewConnectionRetainsEarlyEditAndCountsBothLoadsTestAndCreate()
    {
        using var context = Context(true);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var ops = new Operations { Providers = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var source = new Source { Load = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        context.Services.AddSingleton<IConsoleConnectionCreateOperation>(ops);
        context.Services.AddSingleton<IOperateTransitionDataSource>(source);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateConnectionNewPage>();
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        page.Find("input[placeholder='prod-postgis']").Input("early-name");
        Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);
        ops.Providers.SetResult(new() { InstalledProviders = ["postgis"] });
        await source.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        source.Load.SetResult(new([], [], [], [], []));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);
        await page.InvokeAsync(() => page.Find("input[type=password]").Input("fixture-password"));
        await page.InvokeAsync(() => page.Find("input[placeholder='db-prod.honua.internal']").Input("db.example"));
        await page.InvokeAsync(() => page.Find("input[placeholder='honua_geo']").Input("spatial"));
        await page.InvokeAsync(() => page.Find("input[placeholder='honua_reader']").Input("reader"));
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Test connection").Click();
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        ops.Test.SetResult(ConsoleConnectionTestOutcome.MissingBinding("Fixture"));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        CatalogAdminPreview.Capture("data-connection-new-tested", page.Markup);
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Create connection").Click();
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        ops.Create.SetResult(ConsoleConnectionCreateResult.MissingBinding("Fixture"));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        CatalogAdminPreview.Capture("data-connection-new-error", page.Markup);
    }

    [Theory]
    [InlineData(false, "input[placeholder='db-prod.honua.internal']")]
    [InlineData(true, "input[placeholder='db-prod.honua.internal']")]
    [InlineData(false, "input[type=password]")]
    [InlineData(true, "input[type=password]")]
    public async Task EditingConnectionSettingsDiscardsAnInFlightTestResult(bool enabled, string editedInput)
    {
        using var context = Context(enabled);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var operations = new Operations();
        context.Services.AddSingleton<IConsoleConnectionCreateOperation>(operations);
        var page = context.Render<OperateConnectionNewPage>();
        page.Find("input[placeholder='db-prod.honua.internal']").Input("original.example");
        page.Find("input[placeholder='honua_geo']").Input("spatial");
        page.Find("input[placeholder='honua_reader']").Input("reader");
        page.Find("input[type=password]").Input("fixture-password");
        var pending = page.FindAll("button").Single(b => b.TextContent.Trim() == "Test connection").ClickAsync(new());
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        Assert.True(page.FindAll("button").Single(b => b.TextContent.Trim() == "Testing…").HasAttribute("disabled"));

        page.Find(editedInput).Input("changed-settings");
        await page.InvokeAsync(() => operations.Test.SetResult(ConsoleConnectionTestOutcome.MissingBinding("Obsolete test result")));
        await pending;

        Assert.DoesNotContain("Obsolete test result", page.Markup, StringComparison.Ordinal);
        Assert.Equal("changed-settings", page.Find(editedInput).GetAttribute("value"));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.False(page.FindAll("button").Single(b => b.TextContent.Trim() == "Test connection").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavigationClearsThePreviousConnectionsTestOutcome(bool enabled)
    {
        using var context = Context(enabled);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new Source { Connections = [Connection, SecondConnection] });
        var page = context.Render<OperateConnectionDetailPage>(p => p.Add(c => c.ConnectionId, Connection.Id));
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[role=status]")));

        page.Render(p => p.Add(c => c.ConnectionId, "second"));

        Assert.Equal("Second database", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("[role=status]"));
    }

    [Fact]
    public async Task PreviousConnectionsDelayedTestCannotReplaceCurrentTestOrClearItsBusyState()
    {
        using var context = Context(true);
        var operations = new Operations();
        context.Services.AddSingleton<IConsoleConnectionCreateOperation>(operations);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new Source { Connections = [Connection, SecondConnection] });
        var page = context.Render<OperateConnectionDetailPage>(p => p.Add(c => c.ConnectionId, Connection.Id));
        var firstTest = page.Find("button").ClickAsync(new());
        page.Render(p => p.Add(c => c.ConnectionId, "second"));
        var secondTest = page.Find("button").ClickAsync(new());
        Assert.Equal(new[] { Connection.Id, "second" }, operations.TestedConnections);

        await page.InvokeAsync(() => operations.Test.SetResult(ConsoleConnectionTestOutcome.MissingBinding("Old connection result")));
        await firstTest;

        Assert.Equal("Second database", page.Find("h1").TextContent);
        Assert.DoesNotContain("Old connection result", page.Markup, StringComparison.Ordinal);
        Assert.True(page.Find("button").HasAttribute("disabled"));
        Assert.Equal(1, context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.PendingOperationCount);
        await page.InvokeAsync(() => operations.SecondTest.SetResult(ConsoleConnectionTestOutcome.MissingBinding("Current connection result")));
        await secondTest;
        Assert.Contains("Current connection result", page.Markup, StringComparison.Ordinal);
        Assert.False(page.Find("button").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("connection a")]
    [InlineData("second")]
    public async Task OlderConnectionLoadCannotReplaceTheCurrentConnection(string currentId)
    {
        using var context = Context(true);
        var source = new Source { Load = new(TaskCreationOptions.RunContinuationsAsynchronously), Connections = [Connection, SecondConnection] };
        context.Services.AddSingleton<IOperateTransitionDataSource>(source);
        var page = context.Render<OperateConnectionDetailPage>(p => p.Add(c => c.ConnectionId, Connection.Id));
        page.Render(p => p.Add(c => c.ConnectionId, currentId));
        var renderCount = page.RenderCount;
        await page.InvokeAsync(() => source.Load.SetResult(new([Connection with { Name = "Outdated database" }], [], [], [], [])));
        page.WaitForState(() => page.RenderCount > renderCount);
        Assert.Equal(currentId == Connection.Id ? Connection.Name : "Second database", page.Find("h1").TextContent);
        Assert.Equal(0, context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.PendingOperationCount);
    }

    private sealed class Operations : IConsoleConnectionCreateOperation
    {
        public TaskCompletionSource<ConsoleConnectionProviderAvailability>? Providers { get; init; }
        public TaskCompletionSource<ConsoleConnectionTestOutcome> Test { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ConsoleConnectionTestOutcome> SecondTest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> TestedConnections { get; } = [];
        public TaskCompletionSource<ConsoleConnectionCreateResult> Create { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConsoleConnectionProviderAvailability> GetProviderAvailabilityAsync(CancellationToken cancellationToken = default) => Providers?.Task ?? Task.FromResult(new ConsoleConnectionProviderAvailability());
        public Task<ConsoleConnectionTestOutcome> TestAsync(ConsoleConnectionCreateCommand command, CancellationToken cancellationToken = default) => Test.Task;
        public Task<ConsoleConnectionTestOutcome> TestExistingAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            TestedConnections.Add(connectionId);
            return connectionId == "second" ? SecondTest.Task : Test.Task;
        }
        public Task<ConsoleConnectionCreateResult> CreateAsync(ConsoleConnectionCreateCommand command, CancellationToken cancellationToken = default) => Create.Task;
    }

    private static BunitContext Context(bool enabled, string mount = "/")
    {
        var context = new BunitContext();
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager(mount, "operate/connections"));
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new Source());
        context.Services.AddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        return context;
    }

    private sealed class Source : IOperateTransitionDataSource
    {
        public TaskCompletionSource<OperateTransitionWorkspace>? Load { get; init; }
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _loads;
        public IReadOnlyList<OperateConnectionSummary> Connections { get; init; } = [Connection];
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            Requested.TrySetResult();
            return ++_loads == 1 && Load is { } load
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
