using Bunit;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// Table reads in both publish pickers: failures are reported, and late answers for an earlier selection are ignored.
public sealed partial class PublishWizardWorkspaceRenderTests
{
    [Fact]
    public void ServiceStep_ReadFailureShowsItsOwnDetail()
    {
        var data = StubOperateData.WithFixtures();
        data.OnServicesRead = () => throw new HttpRequestException("Name or service not known");
        using var ctx = NewContext(data);
        var cut = ctx.Render<PublishWizardWorkspace>();

        // The detail is the failure itself, not the name of the field that holds it.
        var notice = cut.Find("[data-connection-notice]");
        Assert.Contains("Name or service not known", notice.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("_loadFailure", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LayerStep_TablesReadAfterTheConnectionWasClearedAreDiscarded()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ServiceLayerPublishTable>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = new RecordingPublishOperation { OnListTables = _ => pending.Task };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();
        await SelectServiceAndAdvanceAsync(cut);

        var read = cut.InvokeAsync(() => cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-a" }));
        cut.WaitForAssertion(() => Assert.Contains("Loading tables", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal));
        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "" });
        Assert.DoesNotContain("Loading tables", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);

        pending.SetResult([new() { Schema = "hydro", Table = "obsolete", Columns = ["id"] }]);
        await read;

        Assert.True(cut.Find("[data-table-picker]").HasAttribute("disabled"));
        Assert.DoesNotContain("hydro.obsolete", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find(".publish-wizard-next").HasAttribute("disabled"));
    }

    [Fact]
    public async Task LayerStep_EarlierConnectionsLateTablesCannotReplaceTheCurrentOnes()
    {
        var first = new TaskCompletionSource<IReadOnlyList<ServiceLayerPublishTable>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = new RecordingPublishOperation
        {
            OnListTables = connection => connection == "conn-a" ? first.Task : Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([
                new() { Schema = "warehouse", Table = "current", Columns = ["id"] }
            ])
        };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();
        await SelectServiceAndAdvanceAsync(cut);

        var oldRead = cut.InvokeAsync(() => cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-a" }));
        cut.WaitForAssertion(() => Assert.Equal("conn-a", publish.LastListedConnectionId));
        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-b" });
        cut.Find("[data-table-picker]").Change("warehouse.current");

        first.SetResult([new() { Schema = "hydro", Table = "obsolete", Columns = ["old"] }]);
        await oldRead;

        Assert.Contains("warehouse.current", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("hydro.obsolete", cut.Markup, StringComparison.Ordinal);
        Assert.False(cut.Find(".publish-wizard-next").HasAttribute("disabled"));
    }

    [Fact]
    public async Task LayerStep_FailedTableReadSaysSoAndRetryRecovers()
    {
        var fail = true;
        var publish = new RecordingPublishOperation
        {
            OnListTables = _ => fail
                ? Task.FromException<IReadOnlyList<ServiceLayerPublishTable>>(new ServiceLayerTableReadException(
                    new OperateCapabilityState("Connections", "Unavailable", "GET /api/v1/admin/connections/{id}/tables", "The Honua server could not be reached.")))
                : Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([new() { Schema = "hydro", Table = "streams", Columns = ["id"] }])
        };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();
        await SelectServiceAndAdvanceAsync(cut);

        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-a" });

        var notice = cut.Find("[data-table-load-failure] [data-connection-notice]");
        Assert.Equal("alert", notice.GetAttribute("role"));
        Assert.Contains("Could not read this connection's tables", notice.TextContent, StringComparison.Ordinal);
        Assert.Contains("not been confirmed empty", notice.TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("[data-table-picker]").HasAttribute("disabled"));
        Assert.Contains("Tables could not be read", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);

        // The retry keeps the notice and its Try again button in place, enabled, so focus stays on it.
        var retry = new TaskCompletionSource<IReadOnlyList<ServiceLayerPublishTable>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fail = false;
        publish.OnListTables = _ => retry.Task;
        var click = cut.InvokeAsync(() => cut.Find("[data-table-load-failure] button").ClickAsync(new MouseEventArgs()));
        cut.WaitForAssertion(() => Assert.Contains("Loading tables", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal));
        Assert.False(cut.Find("[data-table-load-failure] button").HasAttribute("disabled"));

        retry.SetResult([new() { Schema = "hydro", Table = "streams", Columns = ["id"] }]);
        await click;

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-table-load-failure]")));
        Assert.Contains("hydro.streams", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);
        Assert.Equal("conn-a", publish.LastListedConnectionId);
    }

    [Fact]
    public async Task LayerStep_EarlierConnectionsLateFailureCannotReplaceTheCurrentTables()
    {
        var first = new TaskCompletionSource<IReadOnlyList<ServiceLayerPublishTable>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = new RecordingPublishOperation
        {
            OnListTables = connection => connection == "conn-a" ? first.Task : Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([
                new() { Schema = "warehouse", Table = "current", Columns = ["id"] }
            ])
        };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();
        await SelectServiceAndAdvanceAsync(cut);

        var oldRead = cut.InvokeAsync(() => cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-a" }));
        cut.WaitForAssertion(() => Assert.Equal("conn-a", publish.LastListedConnectionId));
        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-b" });

        first.SetException(new ServiceLayerTableReadException(
            new OperateCapabilityState("Connections", "Unavailable", "GET /api/v1/admin/connections/{id}/tables", "Gateway timeout.")));
        await oldRead;

        Assert.Empty(cut.FindAll("[data-table-load-failure]"));
        Assert.Contains("warehouse.current", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);
        Assert.False(cut.Find("[data-table-picker]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task LayerStep_SuccessfulEmptyReadSaysNoPublishableTables()
    {
        var publish = new RecordingPublishOperation { OnListTables = _ => Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([]) };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();
        await SelectServiceAndAdvanceAsync(cut);

        Assert.Contains("Select a table", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);
        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-a" });

        Assert.Contains("No publishable tables", cut.Find("[data-table-picker]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-table-load-failure]"));
    }

    [Fact]
    public async Task LayerStep_ClearingTheConnectionDismissesATableReadFailure()
    {
        var publish = new RecordingPublishOperation
        {
            OnListTables = _ => Task.FromException<IReadOnlyList<ServiceLayerPublishTable>>(new HttpRequestException("Connection refused"))
        };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();
        await SelectServiceAndAdvanceAsync(cut);

        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "conn-a" });
        Assert.Contains("Connection refused", cut.Find("[data-table-load-failure]").TextContent, StringComparison.Ordinal);

        await cut.Find("[data-connection-picker]").ChangeAsync(new ChangeEventArgs { Value = "" });
        Assert.Empty(cut.FindAll("[data-table-load-failure]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickPublish_LateFailureAfterClearingTheConnectionShowsNothing(bool enabled)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ServiceLayerPublishTable>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = new RecordingPublishOperation { OnListTables = _ => pending.Task };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var cut = ctx.Render<OperatePublishLayerPage>();

        var read = cut.InvokeAsync(() => cut.Find("[data-workspace-field='connectionId']").ChangeAsync(new ChangeEventArgs { Value = "conn-a" }));
        cut.WaitForAssertion(() => Assert.Equal("conn-a", publish.LastListedConnectionId));
        await cut.Find("[data-workspace-field='connectionId']").ChangeAsync(new ChangeEventArgs { Value = "" });

        pending.SetException(new HttpRequestException("Connection reset"));
        await read;

        Assert.Empty(cut.FindAll("[data-table-load-failure]"));
        Assert.DoesNotContain("Loading tables", cut.Find("[data-workspace-field='table']").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickPublish_UnauthorizedTableReadAsksToSignIn(bool enabled)
    {
        var publish = new RecordingPublishOperation
        {
            OnListTables = _ => Task.FromException<IReadOnlyList<ServiceLayerPublishTable>>(new ServiceLayerTableReadException(
                new OperateCapabilityState("Connections", "Unauthorized", "GET /api/v1/admin/connections/{id}/tables", "Sign-in expired.") { StatusCode = 401 }))
        };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var cut = ctx.Render<OperatePublishLayerPage>();

        await cut.Find("[data-workspace-field='connectionId']").ChangeAsync(new ChangeEventArgs { Value = "conn-a" });

        var notice = cut.Find("[data-table-load-failure]");
        Assert.Contains("Sign in to read this environment", notice.TextContent, StringComparison.Ordinal);
        Assert.Contains("Sign in", notice.QuerySelector("a")!.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickPublish_FailedTableReadIsNotReportedAsNoPublishableTables(bool enabled)
    {
        var fail = true;
        var publish = new RecordingPublishOperation
        {
            OnListTables = _ => fail
                ? Task.FromException<IReadOnlyList<ServiceLayerPublishTable>>(new ServiceLayerTableReadException(
                    new OperateCapabilityState("Connections", "Forbidden", "GET /api/v1/admin/connections/{id}/tables", "Access denied.") { StatusCode = 403 }))
                : Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([new() { Schema = "warehouse", Table = "current", Columns = ["id"] }])
        };
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var cut = ctx.Render<OperatePublishLayerPage>();

        await cut.Find("[data-workspace-field='connectionId']").ChangeAsync(new ChangeEventArgs { Value = "conn-a" });

        var table = cut.Find("[data-workspace-field='table']");
        Assert.DoesNotContain("No publishable tables", table.TextContent, StringComparison.Ordinal);
        Assert.Contains("Tables could not be read", table.TextContent, StringComparison.Ordinal);
        // The 403 keeps its own heading from the shared notice rather than a generic failure.
        Assert.Contains("Access to these objects was denied", cut.Find("[data-table-load-failure]").TextContent, StringComparison.Ordinal);

        fail = false;
        await cut.Find("[data-table-load-failure] button").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[data-table-load-failure]"));
        Assert.Contains("warehouse.current", cut.Find("[data-workspace-field='table']").TextContent, StringComparison.Ordinal);
    }
}
