using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed partial class ServerStudioWorkflowPackageClient
{
    private static StudioWorkflowValidationIssue? ValidateFailureMode(StudioWorkflowPackageDraft draft)
    {
        var mode = draft.RetryPolicy.FailureMode;
        var message = mode switch
        {
            StudioWorkflowContractValues.FailureModeRouteEdges => null,
            StudioWorkflowContractValues.FailureModeFailFast when !draft.Edges.Any(edge => edge.Kind == StudioWorkflowContractValues.EdgeKindFailure) => null,
            StudioWorkflowContractValues.FailureModeFailFast => "Fail-fast cannot be combined with Failure edges; remove them or select route-failure-edges.",
            _ => $"Failure mode '{mode}' cannot be executed by this server. Select fail-fast or route-failure-edges."
        };
        return message is null ? null : new StudioWorkflowValidationIssue { Severity = "error", Scope = "retryPolicy.failureMode", Message = message };
    }

    private static StudioWorkflowRetryPolicy GlobalRetryFromGraph(WorkflowGraph? graph, StudioWorkflowRetryPolicy? legacy)
    {
        var first = graph?.Nodes?.FirstOrDefault();
        return first?.RetryPolicy is { } retry
            ? new StudioWorkflowRetryPolicy { MaxAttempts = retry.MaxAttempts, BackoffSeconds = retry.InitialDelaySeconds }
            : legacy ?? new StudioWorkflowRetryPolicy { MaxAttempts = 1, BackoffSeconds = 0 };
    }

    private static WorkflowNode ToExecutableNode(StudioWorkflowNode node) => new()
    {
        NodeId = node.Id,
        NodeTypeId = node.Type,
        Parameters = new Dictionary<string, string>(node.Configuration, StringComparer.Ordinal),
        RetryPolicy = node.RetryPolicy,
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

    private static WorkflowEdge ToExecutableEdge(StudioWorkflowEdge edge)
    {
        var isFailure = edge.Kind == StudioWorkflowContractValues.EdgeKindFailure;
        return new WorkflowEdge
        {
            SourceNodeId = edge.FromNodeId,
            TargetNodeId = edge.ToNodeId,
            Kind = isFailure ? WorkflowEdgeKind.Failure : WorkflowEdgeKind.Data,
            SourcePort = isFailure ? null : edge.FromPort,
            TargetPort = isFailure ? null : edge.ToPort
        };
    }
}
