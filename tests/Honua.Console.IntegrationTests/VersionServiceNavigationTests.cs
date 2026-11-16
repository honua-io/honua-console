using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class VersionServiceNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingService_HidesOldActionsAndClearsAlterForm(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var operation = new VersionOperation();
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page, "first");
        page.Find("[data-alter]").Click();
        page.Find("[data-version-description]").Input("Unsaved old edit");

        Load(page, "second");

        Assert.Empty(page.FindAll("[data-version-row]"));
        Assert.Empty(page.FindAll("[data-submit-version]"));
        Assert.True(page.Find("[data-load-versions]").HasAttribute("disabled"));
        await page.InvokeAsync(() => operation.SecondList.SetResult(Versions("second")));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-version-row]")));
        Assert.Contains("second version", page.Find("[data-version-row]").TextContent);
        Assert.Equal(string.Empty, page.Find("[data-version-name]").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-cancel-alter]"));
        if (enabled)
        {
            var context = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current;
            Assert.Equal("second", context.Filters["serviceId"]);
            Assert.Equal(0, context.Unsaved.ChangedFieldCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingService_ClosesDeleteConfirmationFromPreviousService(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var operation = new VersionOperation();
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page, "first");
        page.Find("[data-delete]").Click();
        Assert.Single(page.FindAll("[data-console-confirm-accept]"));

        Load(page, "second");
        await page.InvokeAsync(() => operation.SecondList.SetResult(Versions("second")));

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-console-confirm-accept]")));
        Assert.Empty(operation.Deletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingCreate_PreventsServiceSwitchUntilOriginalServiceRefreshes(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var operation = new VersionOperation();
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page, "first");
        page.Find("[data-version-name]").Input("New branch");
        page.Find("[data-submit-version]").Click();
        Assert.Equal("first", operation.Created?.ServiceId);
        page.Find("[data-service-id-input]").Change("second");
        Assert.True(page.Find("[data-load-versions]").HasAttribute("disabled"));
        // A queued event must also obey the operation guard.
        page.Find("[data-load-versions]").Click();
        Assert.Equal(new[] { "first" }, operation.Lists);

        await page.InvokeAsync(() => operation.Create.SetResult(VersionOperationResult.Success("Created", "Version created.")));

        page.WaitForAssertion(() => Assert.False(page.Find("[data-load-versions]").HasAttribute("disabled")));
        Assert.Equal(new[] { "first", "first" }, operation.Lists);
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton<IConsoleNotificationService, ConsoleNotificationService>();
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate/versions"));
        return ctx;
    }

    private static void Load(IRenderedComponent<OperateVersionsPage> page, string serviceId)
    {
        page.Find("[data-service-id-input]").Change(serviceId);
        page.Find("[data-load-versions]").Click();
    }

    private static OperateVersionListView Versions(string serviceId) => new()
    {
        Bound = true,
        Versions = [new OperateVersionView
        {
            VersionGuid = serviceId + "-branch", VersionName = serviceId + " version", Owner = "operator",
            Access = "protected", Status = "active", Description = "Existing branch"
        }]
    };

    private sealed class VersionOperation : IVersionManagementOperation
    {
        public TaskCompletionSource<OperateVersionListView> SecondList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<VersionOperationResult> Create { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Lists { get; } = [];
        public List<string> Deletes { get; } = [];
        public CreateVersionCommand? Created { get; private set; }
        public Task<OperateVersionListView> ListVersionsAsync(string serviceId, CancellationToken cancellationToken = default)
        {
            Lists.Add(serviceId);
            return serviceId == "second" ? SecondList.Task : Task.FromResult(Versions(serviceId));
        }
        public Task<VersionOperationResult> CreateVersionAsync(CreateVersionCommand command, CancellationToken cancellationToken = default)
        {
            Created = command;
            return Create.Task;
        }
        public Task<VersionOperationResult> DeleteVersionAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default)
        {
            Deletes.Add(serviceId + "/" + versionGuid);
            return Task.FromResult(VersionOperationResult.Success("Deleted", "Version deleted."));
        }
        public Task<ReconcileResultView> ReconcileAsync(ReconcileVersionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> AlterVersionAsync(AlterVersionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionConflictsView> InspectConflictsAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResolveConflictsResultView> ResolveConflictsAsync(string serviceId, string versionGuid, IReadOnlyList<ConflictResolutionChoice> resolutions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> PostAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
