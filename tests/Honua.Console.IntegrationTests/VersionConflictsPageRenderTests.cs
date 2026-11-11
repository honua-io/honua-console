using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class VersionConflictsPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_PreservesChoiceOnRerenderAndPublishesCountsOnly(bool enabled)
    {
        var (context, registry, operation) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        var page = ctx.Render<OperateVersionConflictsPage>(p => p.Add(x => x.ServiceId, "city/parcels").Add(x => x.VersionGuid, "review"));
        Assert.Equal(enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal("/console/operate/versions", page.Find("a").GetAttribute("href"));
        if (enabled) CatalogAdminPreview.Capture("data-conflicts-pending", page.Markup);
        Assert.True(page.Find("[data-post]").HasAttribute("disabled"));
        page.Find("[data-choice-version]").Change(true);
        page.Render(p => p.Add(x => x.ServiceId, "city/parcels").Add(x => x.VersionGuid, "review"));
        Assert.True(page.Find("[data-choice-version]").HasAttribute("checked"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.DoesNotContain("private_value", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        page.Find("[data-submit-resolutions]").Click();
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(new ConflictResolutionChoice(3, 42, "version"), Assert.Single(operation.Choices));
        operation.View = new() { Bound = true };
        await page.InvokeAsync(() => operation.Resolution.SetResult(new() { Operation = VersionOperationResult.Success("Resolved", "Conflict resolved."), CanPost = true }));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-no-conflicts]")));
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.False(page.Find("[data-post]").HasAttribute("disabled"));
        if (enabled) CatalogAdminPreview.Capture("data-conflicts-resolved", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_FromPreviousVersionCannotReplaceCurrentState(bool enabled)
    {
        var (context, registry, operation) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        var page = ctx.Render<OperateVersionConflictsPage>(p => p.Add(x => x.ServiceId, "parcels").Add(x => x.VersionGuid, "old"));
        page.Find("[data-choice-version]").Change(true);
        page.Find("[data-submit-resolutions]").Click();
        page.Render(p => p.Add(x => x.ServiceId, "roads").Add(x => x.VersionGuid, "new"));
        page.Find("[data-choice-default]").Change(true);
        await page.InvokeAsync(() => operation.Resolution.SetResult(new() { Operation = VersionOperationResult.Success("Resolved", "Old version done.") }));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        Assert.Empty(page.FindAll("[data-resolve-result]"));
        Assert.True(page.Find("[data-choice-default]").HasAttribute("checked"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        if (enabled) Assert.Equal("roads", Assert.Single(registry.Current.Selection).ServiceName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_RequiresConfirmationAndTargetsCurrentVersion(bool enabled)
    {
        var (context, registry, operation) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        operation.View = new() { Bound = true };
        var page = ctx.Render<OperateVersionConflictsPage>(p => p.Add(x => x.ServiceId, "city/parcels").Add(x => x.VersionGuid, "review"));
        if (enabled) CatalogAdminPreview.Capture("data-conflicts-clean", page.Markup);
        page.Find("[data-post]").Click();
        Assert.Null(operation.PostedVersion);
        page.Find("[data-console-confirm-accept]").Click();
        page.WaitForAssertion(() => Assert.Equal(("city/parcels", "review"), operation.PostedVersion));
        Assert.Contains("Version posted.", page.Find("[data-post-result]").TextContent);
        if (enabled) CatalogAdminPreview.Capture("data-conflicts-posted", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBinding_ExposesRecoveryWithoutMutationControls(bool enabled)
    {
        var (context, registry, operation) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        operation.View = VersionConflictsView.Unbound("Missing binding", "Connect an environment to inspect conflicts.");
        var page = ctx.Render<OperateVersionConflictsPage>(p => p.Add(x => x.ServiceId, "parcels").Add(x => x.VersionGuid, "review"));
        Assert.Contains("Conflict resolution is not bound", page.Markup);
        Assert.Empty(page.FindAll("[data-post], [data-submit-resolutions]"));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (enabled) CatalogAdminPreview.Capture("data-conflicts-missing", page.Markup);
    }

    private static (BunitContext Context, ConsoleAgentContextRegistry Registry, ConflictOperation Operation) CreateContext(ConsoleV4Options options)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        var operation = new ConflictOperation();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate/data"));
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        return (ctx, registry, operation);
    }

    private sealed class ConflictOperation : IVersionManagementOperation
    {
        public VersionConflictsView View { get; set; } = new()
        {
            Bound = true,
            HasConflicts = true,
            Conflicts = [new() { LayerId = 3, ObjectId = 42, ConflictType = "Attribute", FieldDiffs = [new() { Name = "owner", Base = "private_value", Default = "Default owner", Version = "Version owner", Changed = true }] }]
        };
        public TaskCompletionSource<ResolveConflictsResultView> Resolution { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<ConflictResolutionChoice> Choices { get; private set; } = [];
        public (string Service, string Version)? PostedVersion { get; private set; }
        public Task<VersionConflictsView> InspectConflictsAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => Task.FromResult(View);
        public Task<ResolveConflictsResultView> ResolveConflictsAsync(string serviceId, string versionGuid, IReadOnlyList<ConflictResolutionChoice> resolutions, CancellationToken cancellationToken = default)
        {
            Choices = resolutions;
            return Resolution.Task;
        }
        public Task<VersionOperationResult> PostAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default)
        {
            PostedVersion = (serviceId, versionGuid);
            return Task.FromResult(VersionOperationResult.Success("Posted", "Version posted."));
        }
        public Task<OperateVersionListView> ListVersionsAsync(string serviceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> CreateVersionAsync(CreateVersionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> AlterVersionAsync(AlterVersionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> DeleteVersionAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReconcileResultView> ReconcileAsync(ReconcileVersionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
