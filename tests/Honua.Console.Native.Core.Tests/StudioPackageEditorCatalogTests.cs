using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioPackageEditorCatalogTests
{
    [Fact]
    public void CatalogExposesOneStudioRoutePerPackageFamily()
    {
        var editors = StudioPackageEditorCatalog.Editors;
        var expectedFamilies = Enum.GetValues<StudioPackageFamily>();

        Assert.Equal(expectedFamilies.Length, editors.Count);
        Assert.All(expectedFamilies, family => Assert.Contains(editors, editor => editor.Family == family));
        Assert.All(editors, editor =>
        {
            Assert.StartsWith("/studio/", editor.Path, StringComparison.Ordinal);
            Assert.EndsWith(".package", editor.PackageType, StringComparison.Ordinal);
            Assert.NotEmpty(editor.RequiredEditors);
            Assert.NotEmpty(editor.ValidationChecks);
            Assert.NotEmpty(editor.PreviewPanels);
            Assert.NotEmpty(editor.PublishReviewItems);
        });
    }

    [Fact]
    public void StableMockLifecycleCoversSaveReopenEditPublishForEveryPackageFamily()
    {
        foreach (var editor in StudioPackageEditorCatalog.Editors)
        {
            var snapshot = StudioPackageLifecycleSimulator.Create(editor);

            snapshot = StudioPackageLifecycleSimulator.Apply(editor, snapshot, StudioLifecycleOperation.Save);
            snapshot = StudioPackageLifecycleSimulator.Apply(editor, snapshot, StudioLifecycleOperation.Reopen);
            snapshot = StudioPackageLifecycleSimulator.Apply(editor, snapshot, StudioLifecycleOperation.Edit);
            snapshot = StudioPackageLifecycleSimulator.Apply(
                editor,
                snapshot,
                StudioLifecycleOperation.Publish,
                CreateReadyPublication(editor));

            Assert.True(snapshot.Published);
            Assert.True(snapshot.PublishedVersion > 0);
            Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("content-version.create", StringComparison.Ordinal));
            Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("content-version.read", StringComparison.Ordinal));
            Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("publication.create", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GeneratedMapLifecycleSupportsPublishShareEmbedAndRollback()
    {
        var map = StudioPackageEditorCatalog.Find("map")!;
        var snapshot = StudioPackageLifecycleSimulator.Create(map);

        foreach (var operation in StudioPackageEditorCatalog.FullLifecycleOperations)
        {
            snapshot = StudioPackageLifecycleSimulator.Apply(
                map,
                snapshot,
                operation,
                new StudioPublicationReadiness(OfflinePolicyReviewed: true));
        }

        Assert.True(snapshot.Published);
        Assert.True(snapshot.Shared);
        Assert.True(snapshot.Embedded);
        Assert.NotNull(snapshot.RollbackFromVersion);
        Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("publication.create", StringComparison.Ordinal));
        Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("share-access.update", StringComparison.Ordinal));
        Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("embed-token.mint", StringComparison.Ordinal));
        Assert.Contains(snapshot.Evidence, entry => entry.StartsWith("rollback.create", StringComparison.Ordinal));
    }

    [Fact]
    public void ShareEmbedAndRollbackRequirePublishedVersion()
    {
        var map = StudioPackageEditorCatalog.Find("map")!;
        var snapshot = StudioPackageLifecycleSimulator.Create(map);
        var publishedOperations = new[]
        {
            StudioLifecycleOperation.Share,
            StudioLifecycleOperation.Embed,
            StudioLifecycleOperation.Rollback
        };

        foreach (var operation in publishedOperations)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                StudioPackageLifecycleSimulator.Apply(map, snapshot, operation));

            Assert.Contains("requires a published content version", exception.Message, StringComparison.Ordinal);
        }

        Assert.False(snapshot.Published);
        Assert.False(snapshot.Shared);
        Assert.False(snapshot.Embedded);
        Assert.Null(snapshot.RollbackFromVersion);
    }

    [Fact]
    public void DashboardAndReportChartsUseVegaLite()
    {
        var dashboard = StudioPackageEditorCatalog.Find("dashboard")!;
        var report = StudioPackageEditorCatalog.Find("report")!;

        Assert.True(dashboard.UsesVegaLite);
        Assert.True(report.UsesVegaLite);
        Assert.Contains(dashboard.RequiredEditors, capability => capability.Title.Contains("Vega-Lite", StringComparison.Ordinal));
        Assert.Contains(report.RequiredEditors, capability => capability.Title.Contains("Vega-Lite", StringComparison.Ordinal));
    }

    [Fact]
    public void FormPublishRequiresOfflinePolicyReview()
    {
        var form = StudioPackageEditorCatalog.Find("form")!;
        var snapshot = StudioPackageLifecycleSimulator.Create(form);

        Assert.True(form.RequiresOfflinePolicyBeforePublish);
        Assert.False(StudioPackageLifecycleSimulator.CanPublish(
            form,
            new StudioPublicationReadiness(OfflinePolicyReviewed: false, OfflineSyncPolicy: "online-only")));
        Assert.False(StudioPackageLifecycleSimulator.CanPublish(
            form,
            new StudioPublicationReadiness(OfflinePolicyReviewed: true)));
        Assert.False(StudioPackageLifecycleSimulator.CanPublish(
            form,
            new StudioPublicationReadiness(OfflinePolicyReviewed: true, OfflineSyncPolicy: string.Empty)));
        Assert.Throws<InvalidOperationException>(() =>
            StudioPackageLifecycleSimulator.Apply(
                form,
                snapshot,
                StudioLifecycleOperation.Publish,
                new StudioPublicationReadiness(OfflinePolicyReviewed: true)));

        var published = StudioPackageLifecycleSimulator.Apply(
            form,
            snapshot,
            StudioLifecycleOperation.Publish,
            new StudioPublicationReadiness(OfflinePolicyReviewed: true, OfflineSyncPolicy: "online-only"));

        Assert.True(published.Published);
    }

    [Fact]
    public void ReopenedAppEditsCreateNewContentVersionsWithoutMutatingPublishedVersion()
    {
        var app = StudioPackageEditorCatalog.Find("app")!;
        var snapshot = StudioPackageLifecycleSimulator.Create(app);

        snapshot = StudioPackageLifecycleSimulator.Apply(app, snapshot, StudioLifecycleOperation.Save);
        snapshot = StudioPackageLifecycleSimulator.Apply(app, snapshot, StudioLifecycleOperation.Publish);
        var publishedVersion = snapshot.PublishedVersion;

        snapshot = StudioPackageLifecycleSimulator.Apply(app, snapshot, StudioLifecycleOperation.Reopen);
        snapshot = StudioPackageLifecycleSimulator.Apply(app, snapshot, StudioLifecycleOperation.Edit);

        Assert.True(app.ReopenedEditsCreateNewContentVersions);
        Assert.Equal(publishedVersion, snapshot.PublishedVersion);
        Assert.True(snapshot.CurrentVersion > publishedVersion);
        Assert.Contains(snapshot.Evidence, entry => entry.Contains("reopened app edit created a new version", StringComparison.Ordinal));
    }

    private static StudioPublicationReadiness CreateReadyPublication(StudioPackageEditorDefinition editor)
    {
        return editor.RequiresOfflinePolicyBeforePublish
            ? new StudioPublicationReadiness(OfflinePolicyReviewed: true, OfflineSyncPolicy: "online-only")
            : new StudioPublicationReadiness(OfflinePolicyReviewed: true);
    }
}
