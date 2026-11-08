using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed partial class ServerStudioWorkflowPackageClient
{
    private static StudioWorkflowValidationIssue? UnsupportedFailureMode(StudioWorkflowPackageDraft draft)
    {
        var mode = draft.RetryPolicy.FailureMode;
        var message = mode switch
        {
            "route-failure-edges" => null,
            "fail-fast" when !draft.Edges.Any(edge => edge.Kind == StudioWorkflowContractValues.EdgeKindFailure) => null,
            "fail-fast" => "Fail-fast cannot be combined with Failure edges; remove them or select route-failure-edges.",
            _ => $"Failure mode '{mode}' cannot be executed by this server. Select fail-fast or route-failure-edges."
        };
        return message is null ? null : new StudioWorkflowValidationIssue { Severity = "error", Scope = "retryPolicy.failureMode", Message = message };
    }

    private static (int MaxAttempts, int BackoffSeconds, string FailureMode) RetryDefaults(StudioWorkflowRetryPolicy retry)
        => (retry.MaxAttempts, retry.BackoffSeconds, retry.FailureMode);

    private static void ApplyEditedRetryDefaults(StudioWorkflowPackageDraft draft)
    {
        var current = RetryDefaults(draft.RetryPolicy);
        if (draft.AppliedRetryDefaults == current)
        {
            return;
        }

        foreach (var node in draft.Nodes)
        {
            node.RetryPolicy = ExecutableRetry(draft.RetryPolicy);
            node.FailurePolicy = WorkflowStepFailurePolicy.Fail;
        }
        draft.AppliedRetryDefaults = current;
    }

    private static StepRetryPolicy ExecutableRetry(StudioWorkflowRetryPolicy retry) => new()
    {
        MaxAttempts = Math.Max(1, retry.MaxAttempts),
        InitialDelaySeconds = retry.BackoffSeconds,
        BackoffMultiplier = 1,
        MaxDelaySeconds = Math.Max(1, retry.BackoffSeconds)
    };

    private static StudioWorkflowRetryPolicy DefaultRetryPolicy(WorkflowGraph? graph, StudioWorkflowRetryPolicy? legacy)
    {
        var first = graph?.Nodes?.FirstOrDefault();
        return first?.RetryPolicy is { } retry
            ? new StudioWorkflowRetryPolicy { MaxAttempts = retry.MaxAttempts, BackoffSeconds = retry.InitialDelaySeconds }
            : legacy ?? new StudioWorkflowRetryPolicy { MaxAttempts = 1, BackoffSeconds = 0 };
    }

    private static WorkflowNode ToExecutableNode(StudioWorkflowNode node, StudioWorkflowRetryPolicy defaults) => new()
    {
        NodeId = node.Id,
        NodeTypeId = node.Type,
        Parameters = new Dictionary<string, string>(node.Configuration, StringComparer.Ordinal),
        RetryPolicy = node.RetryPolicy ?? ExecutableRetry(defaults),
        FailurePolicy = node.FailurePolicy ?? WorkflowStepFailurePolicy.Fail,
        TimeoutSeconds = node.TimeoutSeconds,
        Condition = node.Condition,
        ForEach = node.ForEach,
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NodeLabelKey] = node.Label,
            [NodeColumnKey] = node.Column.ToString(CultureInfo.InvariantCulture),
            [NodeRowKey] = node.Row.ToString(CultureInfo.InvariantCulture)
        }
    };

    private static WorkflowEdge ToExecutableEdge(StudioWorkflowEdge edge) => new()
    {
        SourceNodeId = edge.FromNodeId,
        TargetNodeId = edge.ToNodeId,
        Kind = edge.Kind == StudioWorkflowContractValues.EdgeKindFailure ? WorkflowEdgeKind.Failure : WorkflowEdgeKind.Data,
        SourcePort = edge.Kind == StudioWorkflowContractValues.EdgeKindFailure ? null : edge.FromPort,
        TargetPort = edge.Kind == StudioWorkflowContractValues.EdgeKindFailure ? null : edge.ToPort
    };
}
