using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio workflow editor. The client validator
/// (<see cref="StudioWorkflowValidator"/>), the inline render surfaces, and the server-error resolver
/// (<see cref="StudioWorkflowServerErrorBinder"/>) all share these so a client finding and a server finding
/// for the same input land on the same key. Form-level keys are constants; per-output-field keys are derived
/// from the schema/field position.
/// </summary>
public static class StudioWorkflowFieldKeys
{
    public const string Cron = "workflow.schedule.cron";
    public const string Cpu = "workflow.worker.cpu";
    public const string MemoryGb = "workflow.worker.memoryGb";
    public const string MaxParallelism = "workflow.worker.maxParallelism";
    public const string MaxAttempts = "workflow.retry.maxAttempts";
    public const string BackoffSeconds = "workflow.retry.backoffSeconds";
    public const string RouteSlug = "workflow.publication.routeSlug";
    public const string Graph = "workflow.graph";

    /// <summary>Per-output-field name key for output schema <paramref name="schemaIndex"/> field <paramref name="fieldIndex"/>.</summary>
    public static string OutputFieldName(int schemaIndex, int fieldIndex) =>
        $"workflow.output[{schemaIndex}].field[{fieldIndex}].name";
}

/// <summary>
/// Pure client-side validator for the Studio workflow editor, mirroring the other Studio validators: it
/// examines the console-owned <see cref="StudioWorkflowPackageDraft"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings keyed by <see cref="StudioWorkflowFieldKeys"/> so the editor can
/// surface them inline. It complements — never replaces — server validation; it covers the rules
/// expressible against console-owned state:
/// <list type="bullet">
///   <item>worker bounds: Cpu 1-32, MemoryGb 2-256, MaxParallelism 1-64 — finally enforcing the HTML min/max
///   that nothing checks today (no EditForm);</item>
///   <item>retry bounds: MaxAttempts 0-10, BackoffSeconds 0-3600;</item>
///   <item>Cron expression format when the schedule mode is <c>cron</c>;</item>
///   <item>publication RouteSlug format;</item>
///   <item>graph source→sink connectivity: no orphan nodes and at least one source-to-sink path;</item>
///   <item>unique output field names within each output schema.</item>
/// </list>
/// </summary>
public sealed class StudioWorkflowValidator : IFieldValidator<StudioWorkflowPackageDraft>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioWorkflowValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioWorkflowPackageDraft state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        EvaluateWorkerBounds(state.WorkerProfile, errors);
        EvaluateRetryBounds(state.RetryPolicy, errors);
        EvaluateSchedule(state.Schedule, errors);
        EvaluateRouteSlug(state.PublicationIntent, errors);
        EvaluateGraph(state, errors);
        EvaluateOutputFields(state, errors);

        return errors;
    }

    private static void EvaluateWorkerBounds(StudioWorkflowWorkerProfile worker, List<ConsoleFieldError> errors)
    {
        if (!NumericBoundsRule.IsWithin(worker.Cpu, 1, 32))
        {
            errors.Add(Error(StudioWorkflowFieldKeys.Cpu, "workflow.worker.cpu.range", "CPU must be between 1 and 32."));
        }

        if (!NumericBoundsRule.IsWithin(worker.MemoryGb, 2, 256))
        {
            errors.Add(Error(StudioWorkflowFieldKeys.MemoryGb, "workflow.worker.memoryGb.range", "Memory (GB) must be between 2 and 256."));
        }

        if (!NumericBoundsRule.IsWithin(worker.MaxParallelism, 1, 64))
        {
            errors.Add(Error(StudioWorkflowFieldKeys.MaxParallelism, "workflow.worker.maxParallelism.range", "Max parallelism must be between 1 and 64."));
        }
    }

    private static void EvaluateRetryBounds(StudioWorkflowRetryPolicy retry, List<ConsoleFieldError> errors)
    {
        if (!NumericBoundsRule.IsWithin(retry.MaxAttempts, 0, 10))
        {
            errors.Add(Error(StudioWorkflowFieldKeys.MaxAttempts, "workflow.retry.maxAttempts.range", "Max attempts must be between 0 and 10."));
        }

        if (!NumericBoundsRule.IsWithin(retry.BackoffSeconds, 0, 3600))
        {
            errors.Add(Error(StudioWorkflowFieldKeys.BackoffSeconds, "workflow.retry.backoffSeconds.range", "Backoff seconds must be between 0 and 3600."));
        }
    }

    private static void EvaluateSchedule(StudioWorkflowSchedule schedule, List<ConsoleFieldError> errors)
    {
        // The cron expression only matters when the schedule runs on a cron.
        if (!string.Equals(schedule.Mode?.Trim(), "cron", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!CronRule.IsValid(schedule.Cron))
        {
            errors.Add(Error(
                StudioWorkflowFieldKeys.Cron,
                "workflow.schedule.cron.format",
                "Cron must be five fields (minute hour day-of-month month day-of-week), for example \"0 6 * * *\"."));
        }
    }

    private static void EvaluateRouteSlug(StudioWorkflowPublicationIntent intent, List<ConsoleFieldError> errors) =>
        StudioPanelBindingRules.EvaluateRouteSlug(intent.RouteSlug, StudioWorkflowFieldKeys.RouteSlug, "workflow", errors);

    /// <summary>
    /// Enforces source→sink graph connectivity: there must be at least one source and one sink, every node
    /// must be wired into the graph (no orphan with neither inbound nor outbound edge once there is more than
    /// one node), and at least one sink must be reachable from a source.
    /// </summary>
    private static void EvaluateGraph(StudioWorkflowPackageDraft state, List<ConsoleFieldError> errors)
    {
        if (state.Nodes.Count == 0)
        {
            errors.Add(Blocker(StudioWorkflowFieldKeys.Graph, "workflow.graph.empty", "Add at least one node to the workflow graph."));
            return;
        }

        var sources = state.Nodes
            .Where(n => string.Equals(n.Category, StudioWorkflowContractValues.NodeCategorySource, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);
        var sinks = state.Nodes
            .Where(n => string.Equals(n.Category, StudioWorkflowContractValues.NodeCategorySink, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (sources.Count == 0)
        {
            errors.Add(Blocker(StudioWorkflowFieldKeys.Graph, "workflow.graph.noSource", "The workflow graph needs at least one source node."));
        }

        if (sinks.Count == 0)
        {
            errors.Add(Blocker(StudioWorkflowFieldKeys.Graph, "workflow.graph.noSink", "The workflow graph needs at least one sink node."));
        }

        // Adjacency + connectivity tracking from the edge list.
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var connected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in state.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.FromNodeId) || string.IsNullOrWhiteSpace(edge.ToNodeId))
            {
                continue;
            }

            if (!adjacency.TryGetValue(edge.FromNodeId, out var targets))
            {
                targets = [];
                adjacency[edge.FromNodeId] = targets;
            }

            targets.Add(edge.ToNodeId);
            connected.Add(edge.FromNodeId);
            connected.Add(edge.ToNodeId);
        }

        // Orphan check: with more than one node, every node must touch at least one edge.
        if (state.Nodes.Count > 1)
        {
            var orphan = state.Nodes.FirstOrDefault(n => !connected.Contains(n.Id));
            if (orphan is not null)
            {
                errors.Add(Error(
                    StudioWorkflowFieldKeys.Graph,
                    "workflow.graph.orphanNode",
                    $"Node '{(string.IsNullOrWhiteSpace(orphan.Label) ? orphan.Id : orphan.Label)}' is not connected to the graph."));
            }
        }

        // Reachability: at least one sink must be reachable from some source.
        if (sources.Count > 0 && sinks.Count > 0 && !HasSourceToSinkPath(sources, sinks, adjacency))
        {
            errors.Add(Blocker(
                StudioWorkflowFieldKeys.Graph,
                "workflow.graph.unreachableSink",
                "No path connects a source node to a sink node. Wire the graph from a source through to a sink."));
        }
    }

    private static bool HasSourceToSinkPath(
        HashSet<string> sources,
        HashSet<string> sinks,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(sources);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
            {
                continue;
            }

            if (sinks.Contains(node))
            {
                return true;
            }

            if (adjacency.TryGetValue(node, out var targets))
            {
                foreach (var target in targets)
                {
                    if (!visited.Contains(target))
                    {
                        stack.Push(target);
                    }
                }
            }
        }

        return false;
    }

    private static void EvaluateOutputFields(StudioWorkflowPackageDraft state, List<ConsoleFieldError> errors)
    {
        for (var schemaIndex = 0; schemaIndex < state.OutputSchemas.Count; schemaIndex++)
        {
            var schema = state.OutputSchemas[schemaIndex];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
            {
                var name = schema.Fields[fieldIndex].Name?.Trim() ?? string.Empty;
                if (name.Length == 0)
                {
                    errors.Add(Error(
                        StudioWorkflowFieldKeys.OutputFieldName(schemaIndex, fieldIndex),
                        "workflow.output.field.name.required",
                        "Give this output field a name."));
                    continue;
                }

                if (!seen.Add(name))
                {
                    errors.Add(Error(
                        StudioWorkflowFieldKeys.OutputFieldName(schemaIndex, fieldIndex),
                        "workflow.output.field.name.duplicate",
                        $"Output field name '{name}' is already used in this schema. Output field names must be unique."));
                }
            }
        }
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Binds the server-returned workflow-package validation failures (<c>{code,message,fieldPath}</c>, the
/// field-addressable <see cref="WorkflowPackageValidationFailure"/> the workflow validate/save endpoints
/// return, honua-server #1185) onto the workflow editor's <see cref="ValidationState"/> server channel,
/// keyed by the same <see cref="StudioWorkflowFieldKeys"/> the client validator uses, by resolving each
/// failure's <c>fieldPath</c> to the matching console field key. A failure whose path cannot be resolved
/// falls back to the raw locator / form-level key so it still surfaces.
/// </summary>
public static class StudioWorkflowServerErrorBinder
{
    /// <summary>Maps <paramref name="failures"/> onto console field keys via the field-path resolver.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(IEnumerable<WorkflowPackageValidationFailure>? failures)
    {
        if (failures is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioWorkflowPathResolver.Resolve(locator));
        return mapper.Map(failures);
    }
}

/// <summary>
/// Resolves a workflow-package validation field path (the <c>fieldPath</c> on a server failure) to the
/// console-owned <see cref="StudioWorkflowFieldKeys"/> for the offending input. The server addresses the
/// workflow.package body, so paths look like <c>/workerProfile/cpu</c>, <c>/retryPolicy/maxAttempts</c>,
/// <c>/schedule/cron</c>, <c>/publicationIntent/routeSlug</c>, <c>/graph</c>/<c>/nodes</c>/<c>/edges</c>, and
/// <c>/outputSchemas/{n}/fields/{m}/name</c>. A leading <c>body</c> token is tolerated. Returns
/// <see langword="null"/> for an unrecognised path so the mapper falls back to the raw locator.
/// </summary>
public static class StudioWorkflowPathResolver
{
    /// <summary>Resolves <paramref name="path"/> to a console field key, or <see langword="null"/>.</summary>
    public static string? Resolve(string? path)
    {
        var segments = JsonPointer.Split(path);
        if (segments.Count == 0)
        {
            return null;
        }

        var index = 0;
        if (string.Equals(segments[0], "body", StringComparison.OrdinalIgnoreCase))
        {
            index = 1;
        }

        if (index >= segments.Count)
        {
            return null;
        }

        var head = segments[index].ToLowerInvariant();
        var leaf = index + 1 < segments.Count ? segments[index + 1].ToLowerInvariant() : null;

        switch (head)
        {
            case "workerprofile" or "worker":
                return leaf switch
                {
                    "cpu" => StudioWorkflowFieldKeys.Cpu,
                    "memorygb" or "memory" => StudioWorkflowFieldKeys.MemoryGb,
                    "maxparallelism" => StudioWorkflowFieldKeys.MaxParallelism,
                    _ => null,
                };
            case "retrypolicy" or "retry":
                return leaf switch
                {
                    "maxattempts" => StudioWorkflowFieldKeys.MaxAttempts,
                    "backoffseconds" or "backoff" => StudioWorkflowFieldKeys.BackoffSeconds,
                    _ => null,
                };
            case "schedule":
                return leaf == "cron" ? StudioWorkflowFieldKeys.Cron : null;
            case "publicationintent" or "publication":
                return leaf is "routeslug" or "slug" ? StudioWorkflowFieldKeys.RouteSlug : null;
            case "graph" or "nodes" or "edges":
                return StudioWorkflowFieldKeys.Graph;
            case "outputschemas" or "outputschema":
                return ResolveOutputField(segments, index);
            default:
                return null;
        }
    }

    // /outputSchemas/{schema}/fields/{field}/name -> the per-field name key.
    private static string? ResolveOutputField(IReadOnlyList<string> segments, int index)
    {
        if (index + 3 < segments.Count
            && int.TryParse(segments[index + 1], out var schemaIndex) && schemaIndex >= 0
            && string.Equals(segments[index + 2], "fields", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[index + 3], out var fieldIndex) && fieldIndex >= 0)
        {
            return StudioWorkflowFieldKeys.OutputFieldName(schemaIndex, fieldIndex);
        }

        return null;
    }
}
