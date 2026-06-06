using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Maps between the Console analysis plan-card editor state and the honua-server analysis content
/// contract (honua-server#1182). The Console editor is a thin projection over the server-owned
/// <see cref="HonuaAnalysisPackageContent"/> / <see cref="HonuaAnalysisPlan"/> graph; this mapper is the
/// single place that lowers an authored plan into the server document and lifts a loaded version back
/// into editor state, so the server contract is never duplicated across the data source and the UI.
/// </summary>
public static class StudioAnalysisPackageMapper
{
    /// <summary>A fresh, empty draft template for a not-yet-saved analysis (no server round-trip).</summary>
    public static StudioAnalysisPlanEditor CreateTemplate() => new();

    /// <summary>Lowers the authored plan card into the server analysis package document.</summary>
    public static HonuaAnalysisPackageContent ToPackageContent(StudioAnalysisPlanEditor plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var goal = string.IsNullOrWhiteSpace(plan.Goal) ? plan.Title : plan.Goal;
        var intentId = string.IsNullOrWhiteSpace(plan.AnalysisId)
            ? $"intent-{Guid.NewGuid():N}"
            : $"intent-{plan.AnalysisId}";
        var planId = string.IsNullOrWhiteSpace(plan.AnalysisId)
            ? $"plan-{Guid.NewGuid():N}"
            : $"plan-{plan.AnalysisId}-v{plan.Version.ToString(CultureInfo.InvariantCulture)}";

        var boundInputs = plan.Inputs
            .Where(input => !string.IsNullOrWhiteSpace(input.ServiceId))
            .ToList();

        var steps = new List<HonuaAnalysisPlanStep>();
        var loadStepIds = new List<string>();
        for (var i = 0; i < boundInputs.Count; i++)
        {
            var input = boundInputs[i];
            var stepId = $"load-{i.ToString(CultureInfo.InvariantCulture)}";
            loadStepIds.Add(stepId);
            steps.Add(new HonuaAnalysisPlanStep
            {
                StepId = stepId,
                Kind = HonuaAnalysisPlanStepKinds.QueryFeatures,
                Inputs = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["serviceId"] = input.ServiceId,
                    ["layerId"] = input.LayerId.ToString(CultureInfo.InvariantCulture),
                    ["role"] = string.IsNullOrWhiteSpace(input.Role) ? "input" : input.Role
                }
            });
        }

        var methodInputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = plan.Method
        };
        foreach (var parameter in plan.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            methodInputs[parameter.Name] = parameter.Value;
        }

        steps.Add(new HonuaAnalysisPlanStep
        {
            StepId = "method",
            Kind = HonuaAnalysisPlanStepKinds.Geoprocess,
            ProcessId = plan.Method,
            Inputs = methodInputs,
            DependsOn = loadStepIds.ToArray()
        });

        var requestedArtifact = ToArtifactKind(plan.OutputContentType);

        return new HonuaAnalysisPackageContent
        {
            Intent = new HonuaAnalysisIntent
            {
                IntentId = intentId,
                Goal = string.IsNullOrWhiteSpace(goal) ? plan.Method : goal,
                Mode = "analysis",
                RequestedOutputs = [requestedArtifact],
                Inputs = boundInputs.Select(i => $"{i.ServiceId}/{i.LayerId.ToString(CultureInfo.InvariantCulture)}").ToArray()
            },
            Plan = new HonuaAnalysisPlan
            {
                PlanId = planId,
                IntentId = intentId,
                Steps = steps.ToArray(),
                Outputs = [requestedArtifact]
            },
            Parameters = plan.Parameters
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal),
            RequestedArtifacts = [requestedArtifact],
            BindingHints =
            [
                new HonuaArtifactBindingRef
                {
                    ArtifactId = string.Empty,
                    Role = "output",
                    TargetKind = plan.OutputContentType,
                    TargetSlot = "source"
                }
            ],
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["console.method"] = plan.Method,
                ["console.computeProfile"] = plan.ComputeProfile,
                ["console.outputContentType"] = plan.OutputContentType,
                ["console.title"] = plan.Title,
                ["console.description"] = plan.Description,
                ["console.outputSchema"] = string.Join(
                    ";",
                    plan.OutputSchema
                        .Where(f => !string.IsNullOrWhiteSpace(f.Name))
                        .Select(f => $"{f.Name}:{f.Type}"))
            }
        };
    }

    /// <summary>Lifts a loaded server version back into plan-card editor state.</summary>
    public static StudioAnalysisPlanEditor ToEditorState(HonuaAnalysisContentVersionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var item = response.Item;
        var version = response.Version;
        var package = version.AnalysisPackage ?? new HonuaAnalysisPackageContent();
        var metadata = package.Metadata ?? EmptyMetadata;

        var plan = new StudioAnalysisPlanEditor
        {
            AnalysisId = string.IsNullOrWhiteSpace(item.ItemId) ? version.ItemId : item.ItemId,
            Version = version.Version,
            Status = HonuaAnalysisStatuses.Draft,
            ETag = version.ContentHash,
            Title = ResolveTitle(item, metadata),
            Description = metadata.GetValueOrDefault("console.description", string.Empty),
            Goal = package.Intent?.Goal ?? string.Empty,
            Method = ResolveMethod(package, metadata),
            ComputeProfile = ResolveComputeProfile(metadata),
            OutputContentType = ResolveOutputContentType(package, metadata)
        };

        foreach (var step in (package.Plan?.Steps ?? []).Where(s =>
                     string.Equals(s.Kind, HonuaAnalysisPlanStepKinds.QueryFeatures, StringComparison.OrdinalIgnoreCase)))
        {
            plan.Inputs.Add(new StudioAnalysisInputEditor
            {
                Role = (step.Inputs ?? EmptyMetadata).GetValueOrDefault("role", "input"),
                ServiceId = (step.Inputs ?? EmptyMetadata).GetValueOrDefault("serviceId", string.Empty),
                LayerId = int.TryParse(
                    (step.Inputs ?? EmptyMetadata).GetValueOrDefault("layerId"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var layerId)
                    ? layerId
                    : 0
            });
        }

        foreach (var (name, value) in package.Parameters ?? EmptyMetadata)
        {
            plan.Parameters.Add(new StudioAnalysisParameterEditor { Name = name, Value = value });
        }

        foreach (var field in ParseOutputSchema(metadata))
        {
            plan.OutputSchema.Add(field);
        }

        plan.ServerPipeline = ToPipeline(package.Plan, plan.Title);
        return plan;
    }

    /// <summary>
    /// Applies a server-proposed analysis package (from the natural-language generation contract) onto the
    /// current plan card, preserving the server-owned identity (analysis id / version / etag) so a refine on
    /// an already-saved draft does not lose it. Inputs/parameters/output schema/method/profile are replaced
    /// from the proposal, mirroring how a reopened version rehydrates. The proposal changed the plan, so it
    /// must be re-saved (a new immutable version) before estimate/submit; any prior estimate is cleared.
    /// </summary>
    public static StudioAnalysisPlanEditor ApplyGeneratedPackage(
        StudioAnalysisPlanEditor current,
        HonuaAnalysisPackageContent package)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(package);

        var metadata = package.Metadata ?? EmptyMetadata;

        // Server-owned identity stays put; only the authored content is replaced.
        current.Title = ResolveTitle(
            new HonuaAnalysisContentItem { Title = current.Title }, metadata);
        current.Description = metadata.GetValueOrDefault("console.description", current.Description);
        current.Goal = package.Intent?.Goal ?? current.Goal;
        current.Method = ResolveMethod(package, metadata);
        current.ComputeProfile = ResolveComputeProfile(metadata);
        current.OutputContentType = ResolveOutputContentType(package, metadata);

        current.Inputs.Clear();
        foreach (var step in (package.Plan?.Steps ?? []).Where(s =>
                     string.Equals(s.Kind, HonuaAnalysisPlanStepKinds.QueryFeatures, StringComparison.OrdinalIgnoreCase)))
        {
            current.Inputs.Add(new StudioAnalysisInputEditor
            {
                Role = (step.Inputs ?? EmptyMetadata).GetValueOrDefault("role", "input"),
                ServiceId = (step.Inputs ?? EmptyMetadata).GetValueOrDefault("serviceId", string.Empty),
                LayerId = int.TryParse(
                    (step.Inputs ?? EmptyMetadata).GetValueOrDefault("layerId"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var layerId)
                    ? layerId
                    : 0
            });
        }

        current.Parameters.Clear();
        foreach (var (name, value) in package.Parameters ?? EmptyMetadata)
        {
            current.Parameters.Add(new StudioAnalysisParameterEditor { Name = name, Value = value });
        }

        current.OutputSchema.Clear();
        foreach (var field in ParseOutputSchema(metadata))
        {
            current.OutputSchema.Add(field);
        }

        current.ServerPipeline = ToPipeline(package.Plan, current.Title);
        // A changed plan must be re-saved + re-estimated before submit.
        current.Estimate = null;
        return current;
    }

    /// <summary>
    /// Projects a server analysis content list item (honua-server#1237 list endpoint) into the
    /// /studio/analysis list row. The server item carries no separate draft/published version split, so the
    /// current version is surfaced as the draft version and the published column stays empty (the analysis
    /// content contract has no publish lifecycle distinct from immutable versioning).
    /// </summary>
    public static StudioAnalysisPlanListItem ToListItem(HonuaAnalysisContentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new StudioAnalysisPlanListItem(
            item.ItemId,
            string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title!,
            ResolveListMethod(item),
            ResolveListComputeProfile(item),
            DraftVersion: item.CurrentVersion,
            PublishedVersion: null,
            UpdatedAt: item.UpdatedAt);
    }

    /// <summary>
    /// Maps the server-computed runtime/cost estimate (honua-server#1237 estimate endpoint) into the plan
    /// card estimate. The runtime and input feature count are real server figures; the cost note carries the
    /// server's compute-units and USD cost so the operator reviews a server-priced figure before submit.
    /// </summary>
    public static StudioAnalysisComputeEstimate ToComputeEstimate(
        HonuaAnalysisContentEstimate estimate,
        string computeProfile)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        var costNote = string.Format(
            CultureInfo.InvariantCulture,
            "~{0:0.###} compute units · ${1:0.####} (server estimate)",
            estimate.EstimatedComputeUnits,
            estimate.EstimatedCostUsd);

        return new StudioAnalysisComputeEstimate(
            estimate.EstimatedRuntimeSeconds,
            estimate.EstimatedInputFeatureCount ?? 0,
            computeProfile,
            costNote);
    }

    private static string ResolveListMethod(HonuaAnalysisContentItem item)
    {
        // The list item carries only roots/metadata, not the package content. The Console-authored method is
        // encoded in the item name slug (BuildName: "<method-or-title-slug>-<guid>") as a best-effort label;
        // the authoritative method is resolved on open (LoadAsync) from the version content.
        var name = item.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return StudioAnalysisMethods.All[0];
        }

        var head = name.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return StudioAnalysisMethods.All.Contains(head, StringComparer.OrdinalIgnoreCase)
            ? head!
            : StudioAnalysisMethods.All[0];
    }

    private static string ResolveListComputeProfile(HonuaAnalysisContentItem item)
        // The list endpoint returns item roots without package metadata, so the compute profile is not
        // resolvable from the list. Surface the default; the real profile binds on open from the version.
        => StudioAnalysisComputeProfiles.All[0];

    /// <summary>Projects the server-compiled plan into the Console DAG/pipeline view (AC#2).</summary>
    public static IReadOnlyList<StudioAnalysisPipelineNode> ToPipeline(HonuaAnalysisPlan? plan, string title)
    {
        // Plan/Steps are non-null-typed but deserialize to null on an explicit server JSON null.
        return (plan?.Steps ?? [])
            .Select(step => new StudioAnalysisPipelineNode(
                step.StepId,
                ResolveStepLabel(step, title),
                step.ProcessId ?? step.Kind,
                step.DependsOn))
            .ToArray();
    }

    /// <summary>Lifts a resolved server artifact + binding into the result-artifact panel projection (AC#3).</summary>
    public static StudioAnalysisArtifactView ToArtifactView(
        HonuaResultArtifactRecord artifact,
        HonuaArtifactBindingRef binding)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(binding);

        return new StudioAnalysisArtifactView(
            artifact.ArtifactId,
            artifact.Kind,
            string.IsNullOrWhiteSpace(artifact.Label) ? artifact.ArtifactId : artifact.Label,
            artifact.Uri,
            artifact.ContentType,
            artifact.ByteSize,
            artifact.RetentionState,
            ResolveDownstreamTargets(artifact.Kind, binding.TargetKind));
    }

    /// <summary>
    /// The downstream content families this artifact can become (AC#3). The server binding's
    /// <c>targetKind</c> is always offered; the rest are filtered to what the artifact kind supports
    /// (e.g. a scalar cannot become a layer), so the panel never claims an impossible binding.
    /// </summary>
    public static IReadOnlyList<string> ResolveDownstreamTargets(string artifactKind, string boundTargetKind)
    {
        var targets = new List<string>();

        void Offer(string target)
        {
            if (!targets.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                targets.Add(target);
            }
        }

        if (!string.IsNullOrWhiteSpace(boundTargetKind))
        {
            Offer(boundTargetKind);
        }

        switch (artifactKind)
        {
            case HonuaArtifactKinds.FeatureLayer:
            case HonuaArtifactKinds.Raster:
                Offer("layer");
                Offer("content");
                Offer("dashboard");
                Offer("workflow");
                break;
            case HonuaArtifactKinds.Table:
                Offer("content");
                Offer("dashboard");
                Offer("report");
                Offer("workflow");
                break;
            case HonuaArtifactKinds.Scalar:
                Offer("report");
                Offer("dashboard");
                Offer("workflow");
                break;
            case HonuaArtifactKinds.Report:
                Offer("report");
                Offer("content");
                break;
            case HonuaArtifactKinds.Map:
                Offer("content");
                Offer("dashboard");
                break;
            default:
                Offer("content");
                Offer("workflow");
                break;
        }

        return targets;
    }

    /// <summary>Maps the Console output-content selection to the requested server artifact kind.</summary>
    public static string ToArtifactKind(string outputContentType) => outputContentType switch
    {
        "layer" => HonuaArtifactKinds.FeatureLayer,
        "report" => HonuaArtifactKinds.Report,
        "dashboard" => HonuaArtifactKinds.Table,
        "workflow" => HonuaArtifactKinds.Table,
        _ => HonuaArtifactKinds.FeatureLayer
    };

    // Deserialized DTO collections/maps are declared non-null but arrive null when the server emits an
    // explicit JSON null for an empty value (System.Text.Json overrides the property initializer), so the
    // mappers coalesce through this before any lookup.
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>(0);

    private static string ResolveTitle(HonuaAnalysisContentItem item, IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("console.title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            return item.Title!;
        }

        return item.Name;
    }

    private static string ResolveMethod(
        HonuaAnalysisPackageContent package,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("console.method", out var method) && !string.IsNullOrWhiteSpace(method))
        {
            return method;
        }

        var methodStep = (package.Plan?.Steps ?? []).FirstOrDefault(s =>
            string.Equals(s.Kind, HonuaAnalysisPlanStepKinds.Geoprocess, StringComparison.OrdinalIgnoreCase));
        if (methodStep?.ProcessId is { Length: > 0 } processId)
        {
            return processId;
        }

        return StudioAnalysisMethods.All[0];
    }

    private static string ResolveComputeProfile(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue("console.computeProfile", out var profile)
            && StudioAnalysisComputeProfiles.All.Contains(profile, StringComparer.OrdinalIgnoreCase)
            ? profile
            : StudioAnalysisComputeProfiles.All[0];

    private static string ResolveOutputContentType(
        HonuaAnalysisPackageContent package,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("console.outputContentType", out var outputContentType)
            && StudioAnalysisOutputContentTypes.All.Contains(outputContentType, StringComparer.OrdinalIgnoreCase))
        {
            return outputContentType;
        }

        var hint = package.BindingHints.FirstOrDefault()?.TargetKind;
        if (!string.IsNullOrWhiteSpace(hint)
            && StudioAnalysisOutputContentTypes.All.Contains(hint, StringComparer.OrdinalIgnoreCase))
        {
            return hint;
        }

        return StudioAnalysisOutputContentTypes.All[0];
    }

    private static IEnumerable<StudioAnalysisOutputFieldEditor> ParseOutputSchema(
        IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue("console.outputSchema", out var encoded) || string.IsNullOrWhiteSpace(encoded))
        {
            yield break;
        }

        foreach (var token in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':', 2);
            var name = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = parts.Length > 1 ? parts[1].Trim() : StudioAnalysisFieldTypes.All[0];
            yield return new StudioAnalysisOutputFieldEditor
            {
                Name = name,
                Type = StudioAnalysisFieldTypes.All.Contains(type, StringComparer.OrdinalIgnoreCase)
                    ? type
                    : StudioAnalysisFieldTypes.All[0]
            };
        }
    }

    private static string ResolveStepLabel(HonuaAnalysisPlanStep step, string title)
    {
        if (string.Equals(step.Kind, HonuaAnalysisPlanStepKinds.QueryFeatures, StringComparison.OrdinalIgnoreCase))
        {
            var inputs = step.Inputs ?? EmptyMetadata;
            var service = inputs.GetValueOrDefault("serviceId", "input");
            var layer = inputs.GetValueOrDefault("layerId", "0");
            var role = inputs.GetValueOrDefault("role", "input");
            return $"{role}: {service}/layer {layer}";
        }

        if (string.Equals(step.StepId, "method", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return step.ProcessId ?? step.StepId;
    }
}
