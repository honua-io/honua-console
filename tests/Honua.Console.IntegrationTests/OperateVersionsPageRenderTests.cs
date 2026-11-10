using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateVersionsPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reconcile_PreservesPolicyAndMountedConflictLinks(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var operation = new SeededVersionOperation();
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);

        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal("/console/operate/versions/parcels/review-branch/conflicts", page.Find("[data-conflicts]").GetAttribute("href"));
        if (enabled) Capture("versions-list", page);
        page.Find("[data-reconcile-policy]").Change("version-wins");
        page.Find("[data-abort-if-conflicts]").Change(true);
        page.Find("[data-reconcile]").Click();

        page.WaitForAssertion(() => Assert.NotNull(operation.Reconcile));
        Assert.Equal("parcels", operation.Reconcile!.ServiceId);
        Assert.Equal("review-branch", operation.Reconcile.VersionGuid);
        Assert.Equal("version-wins", operation.Reconcile.Policy);
        Assert.True(operation.Reconcile.AbortIfConflicts);
        Assert.Equal("2", page.Find("[data-auto-resolved]").TextContent);
        Assert.Equal("1", page.Find("[data-remaining-conflicts]").TextContent);
        Assert.Equal("/console/operate/versions/parcels/review-branch/conflicts", page.Find("[data-open-conflicts]").GetAttribute("href"));
        if (enabled) Capture("versions-conflicts", page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_SubmitsExistingCommandAndClearsSuccessfulForm(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var operation = new SeededVersionOperation();
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);
        page.Find("[data-version-name]").Change(" Review edits ");
        page.Find("[data-version-owner]").Change(" operator ");
        page.Find("[data-version-description]").Change(" Parcel updates ");
        page.Find("[data-version-access]").Change("protected");
        page.Find("[data-submit-version]").Click();

        page.WaitForAssertion(() => Assert.NotNull(operation.Created));
        Assert.Equal("parcels", operation.Created!.ServiceId);
        Assert.Equal("Review edits", operation.Created.VersionName);
        Assert.Equal("operator", operation.Created.Owner);
        Assert.Equal("Parcel updates", operation.Created.Description);
        Assert.Equal("protected", operation.Created.Access);
        Assert.Equal(string.Empty, page.Find("[data-version-name]").GetAttribute("value"));
        Assert.Equal(0, ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
    }

    [Fact]
    public async Task Context_TracksLoadedServiceAndActualEditsThenClearsOnDispose()
    {
        using var ctx = CreateContext(true);
        ctx.Services.AddSingleton<IVersionManagementOperation>(new SeededVersionOperation());
        var registry = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);
        Assert.Equal("/operate/versions", registry.Current.Route);
        Assert.Equal("parcels", registry.Current.Filters["serviceId"]);
        var selected = Assert.Single(registry.Current.Selection);
        Assert.Equal(ConsoleAgentEntityKind.Service, selected.Kind);
        Assert.Equal("parcels", selected.ServiceName);
        page.Find("[data-alter]").Click();
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        page.Find("[data-version-description]").Change("Changed description");
        Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);
        Capture("versions-alter", page);
        page.Find("[data-cancel-alter]").Click();
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        await ctx.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Filters);
        Assert.Empty(registry.Current.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBinding_ShowsExplanationWithoutInventedVersions(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        ctx.Services.AddSingleton<IVersionManagementOperation, UnsupportedVersionManagementOperation>();
        var page = ctx.Render<OperateVersionsPage>();
        page.Find("[data-service-id-input]").Change("parcels");
        page.Find("[data-load-versions]").Click();
        page.WaitForAssertion(() => Assert.Contains("Branch versions are not bound", page.Markup));
        Assert.Empty(page.FindAll("[data-version-row]"));
        Assert.Empty(page.FindAll("[data-submit-version]"));
        if (enabled) Capture("versions-missing", page);
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton(new ConsoleAgentContextRegistry());
        ctx.Services.AddSingleton<IConsoleNotificationService, ConsoleNotificationService>();
        ctx.Services.AddSingleton<NavigationManager>(new HostedNavigationManager());
        return ctx;
    }

    private static void Load(IRenderedComponent<OperateVersionsPage> page)
    {
        page.Find("[data-service-id-input]").Change(" parcels ");
        page.Find("[data-load-versions]").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-version-row]")));
    }

    private static void Capture(string name, IRenderedComponent<OperateVersionsPage> page) =>
        CatalogAdminPreview.Capture(name, page.Markup);

    private sealed class HostedNavigationManager : NavigationManager
    {
        public HostedNavigationManager() => Initialize("http://localhost/console/", "http://localhost/console/operate/versions");
    }

    private sealed class SeededVersionOperation : IVersionManagementOperation
    {
        public CreateVersionCommand? Created { get; private set; }
        public ReconcileVersionCommand? Reconcile { get; private set; }

        public Task<OperateVersionListView> ListVersionsAsync(string serviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateVersionListView
            {
                Bound = true,
                Versions = [new OperateVersionView
                {
                    VersionGuid = "review-branch", VersionName = "Review edits", Owner = "operator",
                    Access = "protected", Status = "active", Description = "Parcel updates"
                }]
            });

        public Task<VersionOperationResult> CreateVersionAsync(CreateVersionCommand command, CancellationToken cancellationToken = default)
        {
            Created = command;
            return Task.FromResult(VersionOperationResult.Success("Created", "Version created."));
        }

        public Task<ReconcileResultView> ReconcileAsync(ReconcileVersionCommand command, CancellationToken cancellationToken = default)
        {
            Reconcile = command;
            return Task.FromResult(new ReconcileResultView
            {
                Operation = VersionOperationResult.Success("Reconciled", "Review the remaining conflict before posting."),
                HasConflicts = true, CanPost = false, AutoResolvedCount = 2, RemainingConflictCount = 1
            });
        }

        public Task<VersionOperationResult> AlterVersionAsync(AlterVersionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> DeleteVersionAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionConflictsView> InspectConflictsAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResolveConflictsResultView> ResolveConflictsAsync(string serviceId, string versionGuid, IReadOnlyList<ConflictResolutionChoice> resolutions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VersionOperationResult> PostAsync(string serviceId, string versionGuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
