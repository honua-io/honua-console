using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="StudioWorkflowValidator"/>, the Wave-4 client validator for the
/// Studio workflow editor. Each catalog rule (worker/retry bounds, cron format, route-slug format,
/// source→sink graph connectivity, unique output field names) is proven in its pass and fail state, keyed by
/// <see cref="StudioWorkflowFieldKeys"/>.
/// </summary>
public sealed class StudioWorkflowValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(StudioWorkflowPackageDraft draft) =>
        StudioWorkflowValidator.Instance.Evaluate(draft);

    /// <summary>A valid source→transform→sink graph with in-bounds worker/retry and a manual schedule.</summary>
    private static StudioWorkflowPackageDraft Valid()
    {
        var draft = new StudioWorkflowPackageDraft { Title = "Nightly normalizer" };
        draft.Schedule.Mode = "manual";
        draft.PublicationIntent.RouteSlug = "parcel-nightly-normalizer";

        draft.Nodes.Add(new StudioWorkflowNode { Id = "src", Category = StudioWorkflowContractValues.NodeCategorySource });
        draft.Nodes.Add(new StudioWorkflowNode { Id = "xf", Category = StudioWorkflowContractValues.NodeCategoryTransform });
        draft.Nodes.Add(new StudioWorkflowNode { Id = "sink", Category = StudioWorkflowContractValues.NodeCategorySink });
        draft.Edges.Add(new StudioWorkflowEdge { Id = "e1", FromNodeId = "src", ToNodeId = "xf" });
        draft.Edges.Add(new StudioWorkflowEdge { Id = "e2", FromNodeId = "xf", ToNodeId = "sink" });

        draft.OutputSchemas.Add(new StudioWorkflowOutputSchema
        {
            Name = "out",
            SinkNodeId = "sink",
            Fields = [new() { Name = "id" }, new() { Name = "status" }],
        });
        return draft;
    }

    [Fact]
    public void ValidWorkflow_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void CpuOutOfRange_ErrorsOnCpu(int cpu)
    {
        var draft = Valid();
        draft.WorkerProfile.Cpu = cpu;

        Assert.Contains(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.Cpu && e.Code == "workflow.worker.cpu.range");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(257)]
    public void MemoryOutOfRange_ErrorsOnMemory(int memory)
    {
        var draft = Valid();
        draft.WorkerProfile.MemoryGb = memory;

        Assert.Contains(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.MemoryGb);
    }

    [Fact]
    public void MaxParallelismOutOfRange_ErrorsOnParallelism()
    {
        var draft = Valid();
        draft.WorkerProfile.MaxParallelism = 65;

        Assert.Contains(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.MaxParallelism);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void MaxAttemptsOutOfRange_ErrorsOnAttempts(int attempts)
    {
        var draft = Valid();
        draft.RetryPolicy.MaxAttempts = attempts;

        Assert.Contains(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.MaxAttempts);
    }

    [Fact]
    public void BackoffOutOfRange_ErrorsOnBackoff()
    {
        var draft = Valid();
        draft.RetryPolicy.BackoffSeconds = 3601;

        Assert.Contains(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.BackoffSeconds);
    }

    [Fact]
    public void InvalidCronWhenModeCron_ErrorsOnCron()
    {
        var draft = Valid();
        draft.Schedule.Mode = "cron";
        draft.Schedule.Cron = "not a cron";

        var error = Assert.Single(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.Cron);
        Assert.Equal("workflow.schedule.cron.format", error.Code);
    }

    [Fact]
    public void ValidCronWhenModeCron_ProducesNoCronError()
    {
        var draft = Valid();
        draft.Schedule.Mode = "cron";
        draft.Schedule.Cron = "0 6 * * *";

        Assert.DoesNotContain(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.Cron);
    }

    [Fact]
    public void InvalidCronIgnoredWhenScheduleManual()
    {
        var draft = Valid();
        draft.Schedule.Mode = "manual";
        draft.Schedule.Cron = "garbage";

        Assert.DoesNotContain(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.Cron);
    }

    [Fact]
    public void InvalidRouteSlug_ErrorsOnRouteSlug()
    {
        var draft = Valid();
        draft.PublicationIntent.RouteSlug = "Not A Slug";

        Assert.Contains(Evaluate(draft), e => e.FieldKey == StudioWorkflowFieldKeys.RouteSlug);
    }

    [Fact]
    public void GraphWithNoSink_BlocksOnGraph()
    {
        var draft = Valid();
        draft.Nodes.RemoveAll(n => n.Category == StudioWorkflowContractValues.NodeCategorySink);
        draft.Edges.RemoveAll(e => e.ToNodeId == "sink");
        draft.OutputSchemas.Clear();

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == StudioWorkflowFieldKeys.Graph && e.Code == "workflow.graph.noSink");
    }

    [Fact]
    public void OrphanNode_ErrorsOnGraph()
    {
        var draft = Valid();
        draft.Nodes.Add(new StudioWorkflowNode { Id = "orphan", Category = StudioWorkflowContractValues.NodeCategoryTransform });

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == StudioWorkflowFieldKeys.Graph && e.Code == "workflow.graph.orphanNode");
    }

    [Fact]
    public void DisconnectedSink_BlocksWithUnreachableSink()
    {
        var draft = Valid();
        // Sever the transform→sink edge but keep the sink wired (as the only edge target) so it is not an
        // orphan: connect a stray transform→sink so the sink is "connected" yet unreachable from a source.
        draft.Edges.RemoveAll(e => e.Id == "e2");
        draft.Nodes.Add(new StudioWorkflowNode { Id = "stray", Category = StudioWorkflowContractValues.NodeCategoryTransform });
        draft.Edges.Add(new StudioWorkflowEdge { Id = "e3", FromNodeId = "stray", ToNodeId = "sink" });

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == StudioWorkflowFieldKeys.Graph && e.Code == "workflow.graph.unreachableSink");
    }

    [Fact]
    public void DuplicateOutputFieldNames_ErrorOnSecond()
    {
        var draft = Valid();
        draft.OutputSchemas[0].Fields.Add(new StudioWorkflowOutputField { Name = "id" });

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == StudioWorkflowFieldKeys.OutputFieldName(0, 2) && e.Code == "workflow.output.field.name.duplicate");
    }

    [Fact]
    public void ServerErrorBinder_ResolvesWorkerAndCronPaths()
    {
        var bound = StudioWorkflowServerErrorBinder.Map(
        [
            new WorkflowPackageValidationFailure { Code = "x", Message = "cpu", FieldPath = "/workerProfile/cpu" },
            new WorkflowPackageValidationFailure { Code = "y", Message = "cron", FieldPath = "/schedule/cron" },
            new WorkflowPackageValidationFailure { Code = "z", Message = "field", FieldPath = "/outputSchemas/0/fields/1/name" },
        ]);

        Assert.Contains(bound, e => e.FieldKey == StudioWorkflowFieldKeys.Cpu);
        Assert.Contains(bound, e => e.FieldKey == StudioWorkflowFieldKeys.Cron);
        Assert.Contains(bound, e => e.FieldKey == StudioWorkflowFieldKeys.OutputFieldName(0, 1));
    }
}
