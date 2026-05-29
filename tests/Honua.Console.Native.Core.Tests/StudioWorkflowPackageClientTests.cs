using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioWorkflowPackageClientTests
{
    [Fact]
    public async Task SeedWorkflowDraftContainsGraphFailureEdgesAndOutputSchemas()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();

        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);

        Assert.NotNull(draft);
        Assert.Equal(StudioWorkflowContractValues.PackageType, draft.PackageType);
        Assert.Contains(draft.Nodes, node => node.Category == StudioWorkflowContractValues.NodeCategorySource);
        Assert.Contains(draft.Nodes, node => node.Category == StudioWorkflowContractValues.NodeCategoryTransform);
        Assert.Contains(draft.Nodes, node => node.Category == StudioWorkflowContractValues.NodeCategorySink);
        Assert.Contains(draft.Edges, edge => edge.Kind == StudioWorkflowContractValues.EdgeKindFailure);
        Assert.Contains(draft.OutputSchemas, schema => schema.Fields.Any(field => field.Name == "geometry"));
        Assert.NotEmpty(draft.Parameters);
        Assert.Equal("cron", draft.Schedule.Mode);
        Assert.Equal("standard-geospatial", draft.WorkerProfile.ProfileId);
        Assert.Equal("route-failure-edges", draft.RetryPolicy.FailureMode);
    }

    [Fact]
    public async Task WorkflowCanDryRunSavePublishAndMonitorInOperate()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);

        var dryRun = await client.DryRunAsync(draft);
        var save = await client.SaveVersionAsync(draft, "unit test save");
        var publish = await client.PublishAsync(draft);
        var dryRunEvidence = await client.GetJobEvidenceAsync(dryRun.JobId);
        var publishEvidence = await client.GetJobEvidenceAsync(publish.JobId);

        Assert.Equal(StudioWorkflowContractValues.DryRunJobKind, dryRun.JobKind);
        Assert.Equal("succeeded", dryRun.Status);
        Assert.NotEmpty(dryRun.Artifacts);
        Assert.NotEmpty(dryRun.OutputSchemas);
        Assert.StartsWith("/operate/jobs/", dryRun.OperateJobUrl, StringComparison.Ordinal);
        Assert.StartsWith("/operate/events?jobId=", dryRun.OperateEventsUrl, StringComparison.Ordinal);

        Assert.Equal(StudioWorkflowContractValues.ContentItemType, save.ContentItemType);
        Assert.Equal(StudioWorkflowContractValues.PackageType, save.PackageType);
        Assert.StartsWith("workflow-", save.ContentItemId, StringComparison.Ordinal);
        Assert.Contains(":v", save.VersionId, StringComparison.Ordinal);

        Assert.Equal(save.ContentItemId, publish.ContentItemId);
        Assert.Equal(save.VersionId, publish.VersionId);
        Assert.Equal(StudioWorkflowContractValues.PublicationJobKind, publish.JobKind);
        Assert.StartsWith("/operate/jobs/", publish.OperateJobUrl, StringComparison.Ordinal);
        Assert.StartsWith("/operate/events?jobId=", publish.OperateEventsUrl, StringComparison.Ordinal);
        Assert.NotNull(dryRunEvidence);
        Assert.NotNull(publishEvidence);
        Assert.Equal(dryRun.JobId, dryRunEvidence.JobId);
        Assert.Equal(publish.JobId, publishEvidence.JobId);
        Assert.NotEmpty(publishEvidence.Logs);
    }

    [Fact]
    public async Task PublishCreatesNewContentVersionWhenDraftChangedAfterSave()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);

        var save = await client.SaveVersionAsync(draft, "baseline save");
        draft.Summary = "Edited after the saved version.";
        draft.Parameters[0].DefaultValue = "maui";

        var publish = await client.PublishAsync(draft);
        var publishEvidence = await client.GetJobEvidenceAsync(publish.JobId);
        var stored = await client.GetDraftAsync(draft.DraftId);

        Assert.NotEqual(save.VersionId, publish.VersionId);
        Assert.EndsWith(":v3", publish.VersionId, StringComparison.Ordinal);
        Assert.Equal(publish.VersionId, draft.CurrentVersionId);
        Assert.NotNull(publishEvidence);
        Assert.Equal(publish.VersionId, publishEvidence.VersionId);
        Assert.NotNull(stored);
        Assert.Equal(publish.VersionId, stored.CurrentVersionId);
        Assert.Equal("maui", stored.Parameters[0].DefaultValue);
    }

    [Fact]
    public async Task EligibleWorkflowPublishesInvocationEndpointWithParameterValidation()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.PublicationIntent.Mode = StudioWorkflowContractValues.PublicationModeProcessEndpoint;
        draft.PublicationIntent.ExposeInvocationEndpoint = true;

        await client.SaveVersionAsync(draft, "endpoint publication");
        var publish = await client.PublishAsync(draft);

        Assert.Equal("queued", publish.Status);
        Assert.NotNull(publish.InvocationEndpoint);
        Assert.Contains("/invoke", publish.InvocationEndpoint, StringComparison.Ordinal);
        Assert.NotEmpty(publish.ParameterValidation);
        Assert.All(publish.ParameterValidation, validation => Assert.True(validation.Valid, validation.Message));
    }

    [Fact]
    public async Task InvocationEndpointPublishBlocksInvalidParameterContract()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.PublicationIntent.Mode = StudioWorkflowContractValues.PublicationModeProcessEndpoint;
        draft.PublicationIntent.ExposeInvocationEndpoint = true;
        draft.Parameters[0].Name = string.Empty;

        await client.SaveVersionAsync(draft, "invalid endpoint publication");
        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Empty(publish.JobId);
        Assert.Empty(publish.JobKind);
        Assert.Null(publish.InvocationEndpoint);
        Assert.Contains(publish.ValidationIssues, issue => issue.Scope == "parameters" && issue.Severity == "error");
        Assert.Contains(publish.ParameterValidation, validation => !validation.Valid);
    }

    [Fact]
    public async Task PublishBlocksScheduledJobWithoutCronBeforeQueueingOperateEvidence()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.PublicationIntent.Mode = StudioWorkflowContractValues.PublicationModeScheduledJob;
        draft.Schedule.Cron = string.Empty;

        var blocked = await client.PublishAsync(draft);

        Assert.Equal("blocked", blocked.Status);
        Assert.Empty(blocked.PublicationId);
        Assert.Empty(blocked.JobId);
        Assert.Empty(blocked.JobKind);
        Assert.Empty(blocked.OperateJobUrl);
        Assert.Empty(blocked.OperateEventsUrl);
        Assert.Contains(blocked.ValidationIssues, issue =>
            issue.Scope == "schedule" &&
            issue.Severity == "error" &&
            issue.Message.Contains("cron", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(draft.ValidationIssues, issue => issue.Scope == "schedule" && issue.Severity == "error");

        draft.Schedule.Cron = "0 6 * * *";
        var queued = await client.PublishAsync(draft);

        Assert.Equal("queued", queued.Status);
        Assert.Equal("job-publish-0001", queued.JobId);
    }

    [Fact]
    public async Task PublishBlocksMissingOutputSchemaBeforeQueueingOperateEvidence()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.OutputSchemas.Clear();

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Empty(publish.PublicationId);
        Assert.Empty(publish.JobId);
        Assert.Empty(publish.JobKind);
        Assert.Empty(publish.OperateJobUrl);
        Assert.Empty(publish.OperateEventsUrl);
        Assert.Contains(publish.ValidationIssues, issue =>
            issue.Scope == "output-schema" &&
            issue.Severity == "error");
    }

    [Fact]
    public async Task PublishBlocksMissingTransformBeforeQueueingOperateEvidence()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.Nodes.RemoveAll(node => node.Category == StudioWorkflowContractValues.NodeCategoryTransform);

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Empty(publish.PublicationId);
        Assert.Empty(publish.JobId);
        Assert.Empty(publish.JobKind);
        Assert.Empty(publish.OperateJobUrl);
        Assert.Empty(publish.OperateEventsUrl);
        Assert.Contains(publish.ValidationIssues, issue =>
            issue.Scope == "graph" &&
            issue.Severity == "error" &&
            issue.Message.Contains("transform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublishBlocksMissingFailureEdgeBeforeQueueingOperateEvidence()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.Edges.RemoveAll(edge => edge.Kind == StudioWorkflowContractValues.EdgeKindFailure);

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Empty(publish.PublicationId);
        Assert.Empty(publish.JobId);
        Assert.Empty(publish.JobKind);
        Assert.Empty(publish.OperateJobUrl);
        Assert.Empty(publish.OperateEventsUrl);
        Assert.Contains(publish.ValidationIssues, issue =>
            issue.Scope == "failure-routing" &&
            issue.Severity == "error" &&
            issue.Message.Contains("failure edge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NeverSavedDraftHasEmptyBoundRunHistory()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();

        var history = await client.ListRunHistoryAsync(contentItemId: string.Empty);

        Assert.Null(history.BindingState);
        Assert.Empty(history.Runs);
    }

    [Fact]
    public async Task PublishBlocksEdgeReferencingMissingNode()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.Edges.Add(new StudioWorkflowEdge
        {
            Id = "edge-dangling",
            FromNodeId = "source-parcels",
            ToNodeId = "node-that-does-not-exist",
            Kind = StudioWorkflowContractValues.EdgeKindSuccess
        });

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Empty(publish.JobId);
        Assert.Contains(publish.ValidationIssues, issue =>
            issue.Scope == "graph" &&
            issue.Severity == "error" &&
            issue.Message.Contains("not part of the workflow graph", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublishBlocksUnsupportedEdgeKind()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.Edges[0].Kind = "side-channel";

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Contains(publish.ValidationIssues, issue =>
            issue.Scope == "graph" &&
            issue.Severity == "error" &&
            issue.Message.Contains("unsupported kind", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublishBlocksUnsupportedPublicationMode()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.PublicationIntent.Mode = "fire-and-forget";

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Empty(publish.PublicationId);
        Assert.Contains(publish.ValidationIssues, issue =>
            issue.Scope == "publication" &&
            issue.Severity == "error" &&
            issue.Message.Contains("publication mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DryRunAndPublishRecordRunHistoryWithStateRejectedRowsAndProvenance()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);

        var save = await client.SaveVersionAsync(draft, "history baseline");
        var dryRun = await client.DryRunAsync(draft);
        var publish = await client.PublishAsync(draft);

        var history = await client.ListRunHistoryAsync(save.ContentItemId);

        Assert.Null(history.BindingState);
        Assert.Equal(2, history.Runs.Count);

        // Newest run first: the publish run, then the dry-run.
        var publishedRun = Assert.Single(history.Runs, run => run.JobId == publish.JobId);
        var dryRunEntry = Assert.Single(history.Runs, run => run.JobId == dryRun.JobId);
        Assert.Equal(history.Runs[0].JobId, publish.JobId);

        Assert.Equal(StudioWorkflowRunStatus.Succeeded, dryRunEntry.Status);
        Assert.Equal("dry-run", dryRunEntry.Trigger);
        Assert.True(dryRunEntry.ProcessedRows > 0);
        Assert.Equal(0, dryRunEntry.RejectedRows);

        Assert.Equal("scheduled", publishedRun.Trigger);
        Assert.Equal(publish.PublicationId, publishedRun.PublicationId);
        // The seeded graph routes a failure sink, so a published run reports row-level rejected records.
        Assert.True(publishedRun.RejectedRows > 0);
        Assert.Equal($"/catalog/{save.ContentItemId}", publishedRun.ProvenanceUrl);
        Assert.StartsWith("/operate/jobs/", publishedRun.OperateJobUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveVersionRejectsBlankChangeNote()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SaveVersionAsync(draft, "   "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DryRunAndPublishRejectBlankDraftId(string draftId)
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.DraftId = draftId;

        await Assert.ThrowsAsync<ArgumentException>(() => client.DryRunAsync(draft));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PublishAsync(draft));
    }

    [Fact]
    public async Task SaveVersionNumberIsMonotonicAcrossRepeatedSaves()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);

        var first = await client.SaveVersionAsync(draft, "first save");
        var second = await client.SaveVersionAsync(draft, "second save");
        var third = await client.SaveVersionAsync(draft, "third save");

        Assert.True(second.VersionNumber > first.VersionNumber);
        Assert.True(third.VersionNumber > second.VersionNumber);
    }

    [Fact]
    public async Task BlockedPublishDoesNotQueueOperateJobEvidence()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        Assert.NotNull(draft);
        draft.OutputSchemas.Clear();

        var publish = await client.PublishAsync(draft);
        Assert.Equal("blocked", publish.Status);

        // A blocked publish must not have minted a job id, so there is no Operate
        // evidence to retrieve. The first real job id stays available for the next
        // (valid) publish, confirming the blocked path did not advance the sequence.
        var evidence = await client.GetJobEvidenceAsync("job-publish-0001");
        Assert.Null(evidence);
    }
}
