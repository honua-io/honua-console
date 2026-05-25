using System.Text.Json;

namespace Honua.Console.Shell.Models;

internal static class StudioWorkflowPackageDraftContent
{
    private static readonly JsonSerializerOptions FingerprintOptions = new(JsonSerializerDefaults.Web);

    public static string CreateFingerprint(StudioWorkflowPackageDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return JsonSerializer.Serialize(CreateSnapshot(draft), FingerprintOptions);
    }

    private static object CreateSnapshot(StudioWorkflowPackageDraft draft) =>
        new
        {
            draft.PackageType,
            draft.SchemaVersion,
            draft.Title,
            draft.Summary,
            draft.WorkspaceId,
            draft.Owner,
            Nodes = draft.Nodes.Select(node => new
            {
                node.Id,
                node.Type,
                node.Category,
                node.Label,
                node.Column,
                node.Row,
                InputPorts = node.InputPorts.ToArray(),
                OutputPorts = node.OutputPorts.ToArray(),
                Configuration = node.Configuration
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, entry.Value })
                    .ToArray()
            }).ToArray(),
            Edges = draft.Edges.Select(edge => new
            {
                edge.Id,
                edge.FromNodeId,
                edge.ToNodeId,
                edge.FromPort,
                edge.ToPort,
                edge.Kind,
                edge.Label
            }).ToArray(),
            Parameters = draft.Parameters.Select(parameter => new
            {
                parameter.Name,
                parameter.Type,
                parameter.Required,
                parameter.DefaultValue,
                parameter.Description
            }).ToArray(),
            Schedule = new
            {
                draft.Schedule.Mode,
                draft.Schedule.Cron,
                draft.Schedule.TimeZone
            },
            WorkerProfile = new
            {
                draft.WorkerProfile.ProfileId,
                draft.WorkerProfile.Runtime,
                draft.WorkerProfile.Cpu,
                draft.WorkerProfile.MemoryGb,
                draft.WorkerProfile.MaxParallelism
            },
            RetryPolicy = new
            {
                draft.RetryPolicy.MaxAttempts,
                draft.RetryPolicy.BackoffSeconds,
                draft.RetryPolicy.FailureMode
            },
            PublicationIntent = new
            {
                draft.PublicationIntent.Mode,
                draft.PublicationIntent.Visibility,
                draft.PublicationIntent.RouteSlug,
                draft.PublicationIntent.ExposeInvocationEndpoint,
                draft.PublicationIntent.RequiresApproval
            },
            OutputSchemas = draft.OutputSchemas.Select(schema => new
            {
                schema.Name,
                schema.SinkNodeId,
                Fields = schema.Fields.Select(field => new
                {
                    field.Name,
                    field.Type,
                    field.Nullable
                }).ToArray()
            }).ToArray(),
            Warnings = draft.Warnings.ToArray()
        };
}
