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
        Assert.Null(publish.InvocationEndpoint);
        Assert.Contains(publish.ParameterValidation, validation => !validation.Valid);
    }
}
