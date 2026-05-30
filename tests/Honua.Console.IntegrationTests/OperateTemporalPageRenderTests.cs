using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render + behavior coverage for the temporal viewer and disconnected sync conflict review
/// surface (<c>/operate/temporal</c>, honua-console#43). Drives <see cref="OperateTemporalPage"/> through a
/// configurable fake <see cref="ITemporalCapabilityClient"/> (never a mock server) to assert each
/// acceptance criterion and failure path: the missing-binding capability explanation (AC #1), RBAC /
/// unsupported gates, the now-vs-as-of diff and feature timeline, the governed rollback that yields a job +
/// result checkpoint rather than erasing history (AC #2), and the conflict resolution that writes a
/// committed change set + audit evidence (AC #3). The merged-build <see cref="UnsupportedTemporalCapabilityClient"/>
/// is also exercised through real DI to prove it never fabricates temporal data.
/// </summary>
public sealed class OperateTemporalPageRenderTests
{
    // ---- AC #1: unsupported / unbound temporal sources render a capability explanation, not an empty viewer.

    [Fact]
    public void TemporalViewer_WhenBindingMissing_RendersCapabilityExplanationNotEmptyViewer()
    {
        var page = Render(new FakeTemporalClient
        {
            Workspace = new TemporalViewerWorkspace([], [MissingBindingState])
        });

        page.WaitForAssertion(
            () => Assert.Contains("Temporal capability is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("honua-server#1166", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-server#1167", page.Markup, StringComparison.Ordinal);
        // Not an empty viewer: no source table is rendered behind the explanation.
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemporalViewer_MergedBuildClient_ReportsMissingBindingWithoutMockData()
    {
        var client = new UnsupportedTemporalCapabilityClient();

        var workspace = await client.GetWorkspaceAsync();

        Assert.Empty(workspace.Sources);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Missing binding", state.State);
    }

    [Fact]
    public void TemporalViewer_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.TestContext();
        // Register exactly what the merged build registers — the unsupported client — and prove the page
        // renders the capability explanation rather than an empty viewer, with no fabricated source rows.
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();

        var page = ctx.RenderComponent<OperateTemporalPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporal capability is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    // ---- Bound source: full surface renders the server-declared capability and is inspectable.

    [Fact]
    public void TemporalViewer_WhenSourcesBound_RendersServerDeclaredCapabilityModes()
    {
        var page = Render(new FakeTemporalClient { Workspace = WorkspaceWith(RollbackSource) });

        page.WaitForAssertion(
            () => Assert.Contains("parcels", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Rollback", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Bidirectional", page.Markup, StringComparison.Ordinal);
        Assert.Contains("retain-7y", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Temporal capability is not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporalViewer_InspectSource_ShowsCheckpointsAndRetentionWarning_WhenNoPolicy()
    {
        var noRetention = RollbackSource with { RetentionPolicyId = null };
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(noRetention),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before release")])
        };
        var page = Render(client);

        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.Contains("Before release", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Retention policy must be visible before a user relies on history (information-model governance).
        Assert.Contains("No retention policy is declared", page.Markup, StringComparison.Ordinal);
    }

    // ---- Design alignment (console-canvas/screens-gitops-temporal-sync.jsx): the as-of header, time
    //      scrubber axis, now-vs-as-of split, replica auto-resolution rules, three-way merge columns,
    //      and AI advisory strip must be present in the rendered structure, not just the data.

    [Fact]
    public void TemporalViewer_InspectSource_RendersAsOfHeaderAndScrubberAxis()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList(
            [
                Checkpoint("cp-now", "Now"),
                ReleaseCheckpoint("cp-rel", "Release v4"),
                Checkpoint("cp-prev", "Before"),
            ])
        };
        var page = Render(client);

        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-temporal-region='timeline']")),
            TimeSpan.FromSeconds(5));
        // As-of header carries the capability badge and the "· as-of" title from the mockup.
        Assert.Contains("· as-of", page.Markup, StringComparison.Ordinal);
        Assert.Contains("temporal-enabled", page.Markup, StringComparison.Ordinal);
        // One axis tick per checkpoint, with the release checkpoint emphasized as a release tick.
        Assert.Equal(3, page.FindAll(".operate-temporal-tick").Count);
        Assert.Single(page.FindAll(".operate-temporal-tick-release"));
        // Legend distinguishes checkpoints from releases.
        Assert.Contains("release ·", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporalViewer_ScrubberTick_SelectsAsOfCheckpoint()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")])
        };
        var page = Render(client);
        InspectFirstSource(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".operate-temporal-tick")), TimeSpan.FromSeconds(5));

        // Clicking the second (older) tick moves the as-of playhead to it.
        page.FindAll(".operate-temporal-tick")[1].Click();

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".operate-temporal-tick-selected")),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TemporalViewer_ComputeDiff_RendersNowVersusAsOfSplit()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")]),
            Diff = new TemporalDiff("diff-1", "src-parcels", "cp-prev", "cp-now",
                AddedFeatures: 3, RemovedFeatures: 1, UpdatedFeatures: 4,
                GeometryChangedFeatures: 2, AttributeChangedFeatures: 5,
                SampleFeatureChanges: [], GeneratedAt: DateTimeOffset.UtcNow)
        };
        var page = Render(client);
        InspectFirstSource(page);
        page.WaitForAssertion(() => page.Find("button.console-button"), TimeSpan.FromSeconds(5));

        ClickByText(page, "Compare");

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find(".operate-temporal-split")),
            TimeSpan.FromSeconds(5));
        // Two panes: as-of and now, mirroring the mockup side-by-side comparison.
        Assert.Single(page.FindAll(".operate-temporal-pane-asof"));
        Assert.Single(page.FindAll(".operate-temporal-pane-now"));
    }

    [Fact]
    public void TemporalViewer_ComputeDiff_RendersAsOfAndNowMapPanes()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")]),
            Diff = new TemporalDiff("diff-1", "src-parcels", "cp-prev", "cp-now",
                AddedFeatures: 3, RemovedFeatures: 1, UpdatedFeatures: 4,
                GeometryChangedFeatures: 2, AttributeChangedFeatures: 5,
                SampleFeatureChanges: [], GeneratedAt: DateTimeOffset.UtcNow)
        };
        var page = Render(client);
        InspectFirstSource(page);
        page.WaitForAssertion(() => page.Find("button.console-button"), TimeSpan.FromSeconds(5));

        ClickByText(page, "Compare");

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-temporal-map='as-of']")),
            TimeSpan.FromSeconds(5));
        // Mockup split: an as-of map AND a now map, each a shared MapPreview figure.
        Assert.Single(page.FindAll("[data-temporal-map='as-of']"));
        Assert.Single(page.FindAll("[data-temporal-map='now']"));
        // Each pane mounts a shared MapPreview (schematic placeholder, no live backend bound).
        Assert.NotNull(page.Find("[data-temporal-map='as-of'] [data-map-mode='layer']"));
        Assert.NotNull(page.Find("[data-temporal-map='now'] [data-map-mode='layer']"));
        // Maps carry the as-of / now temporal framing badges from the mockup, not mock data.
        Assert.NotNull(page.Find("[data-temporal-map='as-of'] .map-preview-timeframe-asof"));
        Assert.NotNull(page.Find("[data-temporal-map='now'] .map-preview-timeframe-now"));
        // No live map is bound — the no-mock schematic is what renders.
        Assert.Equal("false", page.Find("[data-temporal-map='as-of'] [data-map-bound]").GetAttribute("data-map-bound"));
    }

    [Fact]
    public void TemporalViewer_ReviewReplica_RendersBaseMapInBaseColumn()
    {
        var page = Render(ClientWithConflicts());

        InspectFirstSource(page);
        page.WaitForAssertion(() => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        ClickByText(page, "Review");

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-temporal-map='base']")),
            TimeSpan.FromSeconds(5));
        // Mockup base column carries a "map at base" thumbnail; it lives inside the base merge column.
        var baseColumn = page.Find(".operate-temporal-col-base");
        Assert.Contains("data-temporal-map=\"base\"", baseColumn.InnerHtml, StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-temporal-map='base'] [data-map-mode='layer']"));
        // Schematic placeholder only — no live style source is bound for the conflict base map.
        Assert.Equal("false", page.Find("[data-temporal-map='base'] [data-map-bound]").GetAttribute("data-map-bound"));
    }

    [Fact]
    public void TemporalViewer_ReplicaQueue_RendersStatusBadgesAndAutoResolutionRules()
    {
        var page = Render(new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            ReplicaQueue = new ReplicaConflictQueue([SampleReplica])
        });

        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-temporal-region='sync-conflicts']")),
            TimeSpan.FromSeconds(5));
        // Auto-resolution rules footer from the mockup.
        Assert.NotNull(page.Find(".operate-temporal-rules"));
        Assert.Contains("latest-wins on identical fields", page.Markup, StringComparison.Ordinal);
        Assert.Contains("server wins past final-published", page.Markup, StringComparison.Ordinal);
        // Status surfaces as a badge pill, not bare text.
        Assert.Contains("console-status", page.Find("[data-temporal-region='sync-conflicts'] tbody tr").InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporalViewer_ReviewReplica_RendersThreeWayColumnsAndAdvisoryStrip()
    {
        var page = Render(ClientWithConflicts());

        InspectFirstSource(page);
        page.WaitForAssertion(() => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        ClickByText(page, "Review");

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-temporal-region='conflict-merge']")),
            TimeSpan.FromSeconds(5));
        // Three base/client/server merge columns from the mockup.
        Assert.Single(page.FindAll(".operate-temporal-col-base"));
        Assert.Single(page.FindAll(".operate-temporal-col-client"));
        Assert.Single(page.FindAll(".operate-temporal-col-server"));
        // AI advisory action strip is present with a server-keep recommendation.
        Assert.NotNull(page.Find(".operate-temporal-advisory"));
        Assert.Contains("3-way merge", page.Markup, StringComparison.Ordinal);
    }

    // ---- RBAC / capability denials surface explanations, never fabricated data.

    [Fact]
    public void TemporalViewer_HistoryReadForbidden_RendersForbiddenExplanation()
    {
        var forbidden = RollbackSource with { HistoryReadPermitted = false };
        var page = Render(new FakeTemporalClient { Workspace = WorkspaceWith(forbidden) });

        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.Contains("History access is not permitted", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Checkpoints", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporalViewer_NoTemporalSupport_RendersUnsupportedExplanation()
    {
        var none = RollbackSource with { Mode = TemporalMode.None, RollbackSupported = false, SyncConflictReviewSupported = false };
        var page = Render(new FakeTemporalClient { Workspace = WorkspaceWith(none) });

        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.Contains("This source has no temporal history", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    // ---- Diff (now-vs-as-of) renders counts on success and the blocked state on a binding failure.

    [Fact]
    public void TemporalViewer_ComputeDiff_RendersSummaryCounts()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")]),
            Diff = new TemporalDiff("diff-1", "src-parcels", "cp-prev", "cp-now",
                AddedFeatures: 3, RemovedFeatures: 1, UpdatedFeatures: 4,
                GeometryChangedFeatures: 2, AttributeChangedFeatures: 5,
                SampleFeatureChanges:
                [
                    new TemporalFeatureChange("f-1", TemporalChangeType.Updated, "r-1", "r-2",
                        [new TemporalAttributeChange("status", "open", "closed", Masked: false)],
                        GeometryChanged: true, ActorId: "alice", OperationRef: "op-1"),
                ],
                GeneratedAt: DateTimeOffset.UtcNow)
        };
        var page = Render(client);
        InspectFirstSource(page);

        page.WaitForAssertion(() => page.Find("button.console-button"), TimeSpan.FromSeconds(5));
        ClickByText(page, "Compare");

        page.WaitForAssertion(
            () => Assert.Contains("Diff Summary", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("status", page.Markup, StringComparison.Ordinal);
        Assert.Equal(1, client.DiffCalls);
    }

    [Fact]
    public void TemporalViewer_ComputeDiff_WhenBlocked_RendersBindingExplanationNotCounts()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")]),
            Diff = TemporalDiff.Blocked(MissingBinding)
        };
        var page = Render(client);
        InspectFirstSource(page);
        page.WaitForAssertion(() => page.Find("button.console-button"), TimeSpan.FromSeconds(5));

        ClickByText(page, "Compare");

        page.WaitForAssertion(
            () => Assert.Contains(MissingBinding.Detail, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The blocked state never renders fabricated diff rows.
        Assert.DoesNotContain("Sample feature changes", page.Markup, StringComparison.Ordinal);
    }

    // ---- AC #2: rollback creates a governed job + result checkpoint, never erasing history.

    [Fact]
    public void TemporalViewer_Rollback_PlanThenExecute_ProducesGovernedJobAndCheckpoint()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")]),
            RollbackPlan = new TemporalRollbackPlan("plan-1", "src-parcels", TemporalRollbackScope.ChangeSet,
                "cp-prev", "cp-now", AffectedFeatureCount: 12, RequiresJob: true, RequiresApproval: true,
                TemporalRollbackMode.DataRevert, "medium", ["validation ok"], []),
            RollbackOperation = new TemporalRollbackOperation("rb-op-1", "plan-1", "job-77", "queued",
                "alice", "bob", "cp-rollback-9", ["audit-1"], FailureReason: null)
        };
        var page = Render(client);
        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.Contains("Governed Rollback", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        ClickByText(page, "Plan Rollback");
        page.WaitForAssertion(
            () => Assert.Contains("Affected features", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        ClickByText(page, "Execute Rollback Job");

        page.WaitForAssertion(
            () => Assert.Contains("cp-rollback-9", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // AC #2: governed job + new result checkpoint + audit evidence, not history erasure.
        Assert.Contains("job-77", page.Markup, StringComparison.Ordinal);
        Assert.Contains("audit-1", page.Markup, StringComparison.Ordinal);
        Assert.Equal(1, client.ExecuteRollbackCalls);
    }

    [Fact]
    public void TemporalViewer_Rollback_WhenPlanBlocked_RendersExplanationNotPlan()
    {
        var client = new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")]),
            RollbackPlan = TemporalRollbackPlan.Blocked(MissingBinding)
        };
        var page = Render(client);
        InspectFirstSource(page);
        page.WaitForAssertion(
            () => Assert.Contains("Governed Rollback", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        ClickByText(page, "Plan Rollback");

        page.WaitForAssertion(
            () => Assert.Contains(MissingBinding.Detail, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Execute Rollback Job", page.Markup, StringComparison.Ordinal);
    }

    // ---- Disconnected replica conflict queue + base/client/server review.

    [Fact]
    public void TemporalViewer_ReplicaConflictQueue_RendersOwnerDeviceGenAndStatus()
    {
        var page = Render(new FakeTemporalClient
        {
            Workspace = WorkspaceWith(RollbackSource),
            ReplicaQueue = new ReplicaConflictQueue([SampleReplica])
        });

        InspectFirstSource(page);

        page.WaitForAssertion(
            () => Assert.Contains("Replica Conflict Queue", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal);   // replica name
        Assert.Contains("owner-42", page.Markup, StringComparison.Ordinal);        // owner
        Assert.Contains("device-9", page.Markup, StringComparison.Ordinal);        // device
        Assert.Contains("cp-base", page.Markup, StringComparison.Ordinal);         // base checkpoint
        Assert.Contains("Conflicted", page.Markup, StringComparison.Ordinal);      // status
    }

    [Fact]
    public void TemporalViewer_ReviewReplica_ShowsBaseClientServerAndConflictMarkers()
    {
        var page = Render(ClientWithConflicts());

        InspectFirstSource(page);
        page.WaitForAssertion(
            () => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        ClickByText(page, "Review");

        page.WaitForAssertion(
            () => Assert.Contains("Base / Client / Server Comparison", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Geometry conflict", page.Markup, StringComparison.Ordinal);      // geometry marker
        Assert.Contains("owner_name", page.Markup, StringComparison.Ordinal);             // field conflict row
        Assert.Contains("Client wins", page.Markup, StringComparison.Ordinal);            // resolution choice
        Assert.Contains("Field merge", page.Markup, StringComparison.Ordinal);
    }

    // ---- AC #3: conflict resolution writes audit evidence + a committed change set.

    [Fact]
    public void TemporalViewer_ResolveConflict_WritesAuditEvidenceAndCommittedChangeSet()
    {
        var client = ClientWithConflicts();
        client.Resolution = new SyncConflictResolutionResult(
            "res-1", ["conflict-1"], SyncResolutionAction.AcceptClient,
            ResultRevisionId: "rev-55", ResultChangeSetId: "cs-9",
            AuditEventIds: ["audit-1", "audit-2"], JobRunId: "job-3");
        // After resolution the page refreshes the review; return no remaining conflicts.
        client.ReviewAfterResolution = new ReplicaConflictReview(SampleReplica, []);
        var page = Render(client);

        InspectFirstSource(page);
        page.WaitForAssertion(() => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        ClickByText(page, "Review");
        page.WaitForAssertion(() => Assert.Contains("Client wins", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));

        ClickByText(page, "Client wins");

        page.WaitForAssertion(
            () => Assert.Contains("Committed Change Set", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // AC #3: audit events + committed change set are surfaced as evidence.
        Assert.Contains("cs-9", page.Markup, StringComparison.Ordinal);
        Assert.Contains("audit-1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("audit-2", page.Markup, StringComparison.Ordinal);
        Assert.True(client.LastResolution!.WroteAuditedChangeSet);
        Assert.Equal(["conflict-1"], client.LastResolution.ConflictIds);
    }

    [Fact]
    public void TemporalViewer_ResolveConflict_WhenBlocked_RendersExplanationNotSuccess()
    {
        var client = ClientWithConflicts();
        client.Resolution = SyncConflictResolutionResult.Blocked(["conflict-1"], SyncResolutionAction.KeepServer, MissingBinding);
        var page = Render(client);

        InspectFirstSource(page);
        page.WaitForAssertion(() => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        ClickByText(page, "Review");
        page.WaitForAssertion(() => Assert.Contains("Server wins", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));

        ClickByText(page, "Server wins");

        page.WaitForAssertion(
            () => Assert.Contains(MissingBinding.Detail, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Committed Change Set", page.Markup, StringComparison.Ordinal);
    }

    // ---- helpers --------------------------------------------------------------------------------------

    private static IRenderedComponent<OperateTemporalPage> Render(ITemporalCapabilityClient client)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(client);
        return ctx.RenderComponent<OperateTemporalPage>();
    }

    private static void InspectFirstSource(IRenderedComponent<OperateTemporalPage> page)
    {
        page.WaitForAssertion(() => ClickByText(page, "Inspect"), TimeSpan.FromSeconds(5));
    }

    private static void ClickByText(IRenderedComponent<OperateTemporalPage> page, string text)
    {
        var button = page.FindAll("button")
            .First(node => node.TextContent.Contains(text, StringComparison.Ordinal));
        button.Click();
    }

    private static TemporalViewerWorkspace WorkspaceWith(TemporalSourceCapability source) =>
        new([source], []);

    private static TemporalCheckpoint Checkpoint(string id, string label) =>
        new(id, "src-parcels", TemporalCursorType.NamedCheckpoint, "2026-05-01T00:00:00Z", label,
            DateTimeOffset.UtcNow, "alice", OperationRef: null, JobRunId: null, MetadataReleaseId: null);

    private static TemporalCheckpoint ReleaseCheckpoint(string id, string label) =>
        new(id, "src-parcels", TemporalCursorType.Release, "2026-05-01T00:00:00Z", label,
            DateTimeOffset.UtcNow, "alice", OperationRef: null, JobRunId: null, MetadataReleaseId: "rel-4");

    private static FakeTemporalClient ClientWithConflicts() => new()
    {
        Workspace = WorkspaceWith(RollbackSource),
        ReplicaQueue = new ReplicaConflictQueue([SampleReplica]),
        ConflictReview = new ReplicaConflictReview(SampleReplica,
        [
            new SyncConflict("conflict-1", "replica-1", "src-parcels", "0", "f-1",
                SyncConflictType.Attribute, "r-base", "r-server",
                [new SyncFieldConflict("owner_name", "Acme", "Acme LLC", "Acme Inc")],
                GeometryConflict: true, DateTimeOffset.UtcNow, SyncConflictStatus.Pending),
        ])
    };

    private static readonly TemporalSourceCapability RollbackSource = new(
        "src-parcels", "parcels", "0", TemporalMode.Rollback, TemporalSyncCapability.Bidirectional,
        RollbackSupported: true, SyncConflictReviewSupported: true, RetentionPolicyId: "retain-7y")
    {
        GeometryHistorySupported = true,
        AttributeHistorySupported = true,
        ReplicaTrackingSupported = true,
        HistoryReadPermitted = true,
    };

    private static readonly DisconnectedReplica SampleReplica = new(
        "replica-1", "Field Crew 7", "src-parcels", "owner-42", "device-9",
        TemporalSyncCapability.Bidirectional, "cp-base", ReplicaServerGen: 42,
        LastSyncAt: DateTimeOffset.UtcNow, ReplicaStatus.Conflicted, PendingConflictCount: 1);

    private static readonly TemporalCapabilityState MissingBindingState = new(
        "Temporal viewer", "Missing binding", "honua-server#1166 / honua-server#1167",
        "Temporal history and disconnected sync conflict review bind to honua-server#1166/#1167.");

    private static readonly TemporalBindingState MissingBinding = new(
        "Temporal viewer", TemporalBindingState.MissingBinding, "honua-server#1166 / honua-server#1167",
        "Configure Honua:Server:BaseUrl and wait for honua-server#1166/#1167.");

    private sealed class FakeTemporalClient : ITemporalCapabilityClient
    {
        public TemporalViewerWorkspace Workspace { get; set; } = new([], []);
        public TemporalCheckpointList Checkpoints { get; set; } = new([]);
        public TemporalDiff? Diff { get; set; }
        public TemporalFeatureTimeline? Timeline { get; set; }
        public TemporalRollbackPlan? RollbackPlan { get; set; }
        public TemporalRollbackOperation? RollbackOperation { get; set; }
        public ReplicaConflictQueue ReplicaQueue { get; set; } = new([]);
        public ReplicaConflictReview ConflictReview { get; set; } = new(Replica: null, []);
        public ReplicaConflictReview? ReviewAfterResolution { get; set; }
        public SyncConflictResolutionResult? Resolution { get; set; }

        public int DiffCalls { get; private set; }
        public int ExecuteRollbackCalls { get; private set; }
        public SyncConflictResolutionResult? LastResolution { get; private set; }

        private bool _resolved;

        public Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<TemporalCheckpointList> GetCheckpointsAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Checkpoints);

        public Task<TemporalDiff> GetDiffAsync(string sourceId, string fromCheckpointId, string toCheckpointId, CancellationToken cancellationToken = default)
        {
            DiffCalls++;
            return Task.FromResult(Diff ?? TemporalDiff.Blocked(MissingBinding));
        }

        public Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(string sourceId, string featureId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Timeline ?? new TemporalFeatureTimeline(featureId, sourceId, [], MissingBinding));

        public Task<TemporalRollbackPlan> CreateRollbackPlanAsync(string sourceId, TemporalRollbackScope scope, string targetCheckpointId, CancellationToken cancellationToken = default) =>
            Task.FromResult(RollbackPlan ?? TemporalRollbackPlan.Blocked(MissingBinding));

        public Task<TemporalRollbackOperation> ExecuteRollbackAsync(string rollbackPlanId, CancellationToken cancellationToken = default)
        {
            ExecuteRollbackCalls++;
            return Task.FromResult(RollbackOperation ?? TemporalRollbackOperation.Blocked(MissingBinding));
        }

        public Task<ReplicaConflictQueue> GetReplicaConflictQueueAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReplicaQueue);

        public Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(string replicaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_resolved && ReviewAfterResolution is not null ? ReviewAfterResolution : ConflictReview);

        public Task<SyncConflictResolutionResult> ResolveConflictsAsync(SyncConflictResolutionRequest request, CancellationToken cancellationToken = default)
        {
            var result = Resolution ?? SyncConflictResolutionResult.Blocked(request.ConflictIds, request.Action, MissingBinding);
            LastResolution = result;
            if (result.BindingState is null)
            {
                _resolved = true;
            }

            return Task.FromResult(result);
        }
    }
}
